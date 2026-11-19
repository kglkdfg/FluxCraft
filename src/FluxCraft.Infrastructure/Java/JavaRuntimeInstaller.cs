using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using FluxCraft.Application.Abstractions;

namespace FluxCraft.Infrastructure.Java;

public sealed class JavaRuntimeInstaller : IJavaRuntimeInstaller
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string RuntimeIndexUrl = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly string _runtimeRoot;

    public JavaRuntimeInstaller(HttpClient httpClient, string? runtimeRoot = null)
    {
        _httpClient = httpClient;
        _runtimeRoot = runtimeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluxCraft", "runtimes");
    }

    public async Task<JavaRuntimeInstallation> EnsureInstalledAsync(
        string minecraftVersion,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var lockKey = Path.GetFullPath(_runtimeRoot);
        var installLock = InstallLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await installLock.WaitAsync(cancellationToken);
        try
        {
            return await EnsureInstalledCoreAsync(minecraftVersion, progress, cancellationToken);
        }
        finally
        {
            installLock.Release();
        }
    }

    private async Task<JavaRuntimeInstallation> EnsureInstalledCoreAsync(
        string minecraftVersion,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("Автоматическая установка Java пока поддерживает только Windows x64.");

        progress?.Report(0);
        var versionDocument = await GetJsonAsync(VersionManifestUrl, cancellationToken);
        var versions = versionDocument.RootElement.GetProperty("versions");
        JsonElement versionEntry = default;
        var resolvedVersion = minecraftVersion;

        if (minecraftVersion == "latest-release")
            resolvedVersion = versionDocument.RootElement.GetProperty("latest").GetProperty("release").GetString()!;
        else if (minecraftVersion == "latest-snapshot")
            resolvedVersion = versionDocument.RootElement.GetProperty("latest").GetProperty("snapshot").GetString()!;

        foreach (var entry in versions.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == resolvedVersion)
            {
                versionEntry = entry;
                break;
            }
        }

        if (versionEntry.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"Версия Minecraft «{resolvedVersion}» не найдена в официальном манифесте Mojang.");

        var versionUrl = versionEntry.GetProperty("url").GetString()
                         ?? throw new InvalidDataException("В манифесте версии отсутствует URL.");
        var versionJson = await GetJsonAsync(versionUrl, cancellationToken);
        string component;
        int majorVersion;
        if (versionJson.RootElement.TryGetProperty("javaVersion", out var javaVersion) &&
            javaVersion.TryGetProperty("component", out var componentElement))
        {
            component = componentElement.GetString()
                        ?? throw new InvalidDataException("Не удалось определить компонент Java.");
            majorVersion = javaVersion.GetProperty("majorVersion").GetInt32();
        }
        else
        {
            // Legacy Vanilla versions before javaVersion was added use Mojang's Java 8 runtime.
            component = "jre-legacy";
            majorVersion = 8;
        }
        progress?.Report(5);

        var runtimeIndex = await GetJsonAsync(RuntimeIndexUrl, cancellationToken);
        var windowsRuntimes = runtimeIndex.RootElement.GetProperty("windows-x64");
        if (!windowsRuntimes.TryGetProperty(component, out var candidates) || candidates.GetArrayLength() == 0)
            throw new InvalidOperationException($"В манифесте Mojang нет runtime «{component}» для Windows x64.");

        JsonElement selected = default;
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.TryGetProperty("availability", out var availability) &&
                availability.TryGetProperty("progress", out var available) && available.GetInt32() == 100)
            {
                selected = candidate;
                break;
            }
        }
        if (selected.ValueKind == JsonValueKind.Undefined)
            selected = candidates[0];

        var packageUrl = selected.GetProperty("manifest").GetProperty("url").GetString()
                         ?? throw new InvalidDataException("В runtime-индексе отсутствует URL пакета.");
        var packageJson = await GetJsonAsync(packageUrl, cancellationToken);
        var runtimeVersion = selected.GetProperty("version").GetProperty("name").GetString() ?? majorVersion.ToString();
        var files = packageJson.RootElement.GetProperty("files");
        var fileEntries = files.EnumerateObject()
            .Where(x => x.Value.GetProperty("type").GetString() == "file")
            .ToArray();

        // Never overwrite files in a previously published runtime directory. A file such as
        // COPYRIGHT may be held open by Windows Defender, an indexer, or another process.
        // Reuse the active directory only when every file still matches Mojang's manifest;
        // otherwise install into a fresh directory and publish it only after full validation.
        var runtimeParent = Path.Combine(_runtimeRoot, component, "windows-x64", runtimeVersion);
        Directory.CreateDirectory(runtimeParent);
        var activeMarker = Path.Combine(runtimeParent, ".fluxcraft-active");
        string? activeDirectoryName = null;
        if (File.Exists(activeMarker))
        {
            try { activeDirectoryName = (await File.ReadAllTextAsync(activeMarker, cancellationToken)).Trim(); }
            catch (IOException) { /* A missing/unreadable marker simply triggers a fresh installation. */ }
        }

        var installDirectory = !string.IsNullOrWhiteSpace(activeDirectoryName)
            ? GetSafePath(runtimeParent, activeDirectoryName)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(installDirectory) ||
            !await IsRuntimeCompleteAsync(installDirectory, fileEntries, cancellationToken))
        {
            installDirectory = Path.Combine(runtimeParent, "runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installDirectory);
        }

        var completed = 0;

        foreach (var entry in files.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            var destination = GetSafePath(installDirectory, relativePath);
            var type = entry.Value.GetProperty("type").GetString();

            if (type == "directory")
            {
                Directory.CreateDirectory(destination);
            }
            else if (type == "file")
            {
                var raw = entry.Value.GetProperty("downloads").GetProperty("raw");
                var expectedHash = raw.GetProperty("sha1").GetString()
                                   ?? throw new InvalidDataException($"Для {entry.Name} отсутствует SHA-1.");
                var expectedSize = raw.GetProperty("size").GetInt64();
                var downloadUrl = raw.GetProperty("url").GetString()
                                  ?? throw new InvalidDataException($"Для {entry.Name} отсутствует URL.");

                if (!await IsValidFileAsync(destination, expectedHash, expectedSize, cancellationToken))
                    await DownloadVerifiedFileAsync(downloadUrl, destination, expectedHash, expectedSize, cancellationToken);
                completed++;
                progress?.Report(10 + 85d * completed / Math.Max(1, fileEntries.Length));
            }
            else if (type == "link")
            {
                var target = entry.Value.GetProperty("target").GetString();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    var targetPath = GetSafePath(installDirectory, target.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(targetPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(targetPath, destination, true);
                    }
                }
            }
        }

        var javaw = Path.Combine(installDirectory, "bin", "javaw.exe");
        var java = Path.Combine(installDirectory, "bin", "java.exe");
        var executable = File.Exists(javaw) ? javaw : java;
        if (!File.Exists(executable))
            throw new InvalidDataException("Java скачана, но исполняемый файл bin/javaw.exe не найден.");

        // Publish the new runtime only after every file has been downloaded and verified.
        var markerTemporary = activeMarker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(markerTemporary, Path.GetFileName(installDirectory), cancellationToken);
            File.Move(markerTemporary, activeMarker, true);
        }
        finally
        {
            if (File.Exists(markerTemporary))
            {
                try { File.Delete(markerTemporary); }
                catch (IOException) { }
            }
        }

        progress?.Report(100);
        return new JavaRuntimeInstallation(component, majorVersion, executable);
    }

    private static async Task<bool> IsRuntimeCompleteAsync(
        string directory,
        IEnumerable<JsonProperty> fileEntries,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return false;

        foreach (var entry in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            var filePath = GetSafePath(directory, relativePath);
            var raw = entry.Value.GetProperty("downloads").GetProperty("raw");
            var hash = raw.GetProperty("sha1").GetString();
            var size = raw.GetProperty("size").GetInt64();
            if (string.IsNullOrWhiteSpace(hash) ||
                !await IsValidFileAsync(filePath, hash, size, cancellationToken))
                return false;
        }

        return true;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string GetSafePath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Runtime manifest содержит недопустимый путь.");
        return fullPath;
    }

    private static async Task<bool> IsValidFileAsync(string path, string sha1, long size, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(sha1, StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadVerifiedFileAsync(
        string url, string destination, string expectedSha1, long expectedSize, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // A unique temporary name prevents two FluxCraft processes from sharing the same .part file.
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, cancellationToken);

            var info = new FileInfo(temporary);
            if (info.Length != expectedSize)
                throw new InvalidDataException($"Размер Java-файла {Path.GetFileName(destination)} не совпадает с манифестом.");
            await using var verify = File.OpenRead(temporary);
            var hash = await SHA1.HashDataAsync(verify, cancellationToken);
            if (!Convert.ToHexString(hash).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-1 Java-файла {Path.GetFileName(destination)} не совпадает с манифестом.");

            // Windows may briefly lock files while Java, Defender or the indexer inspects them.
            // Retry atomic publication; never delete the existing runtime file before the new one is verified.
            IOException? lastLockError = null;
            for (var attempt = 0; attempt < 11; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temporary, destination, true);
                    lastLockError = null;
                    break;
                }
                catch (IOException exception)
                {
                    // Another installer may have completed this exact file while we were downloading.
                    if (await IsValidFileAsync(destination, expectedSha1, expectedSize, cancellationToken))
                    {
                        lastLockError = null;
                        break;
                    }

                    lastLockError = exception;
                    if (attempt < 10)
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }

            if (lastLockError is not null)
                throw new IOException(
                    $"Не удалось обновить файл Java «{destination}»: файл занят другим процессом. " +
                    "Закройте другие запущенные экземпляры FluxCraft/Minecraft и повторите установку.",
                    lastLockError);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException) { /* A pending temporary file is harmless; the next install uses a unique name. */ }
                catch (UnauthorizedAccessException) { /* Do not hide the original installation error. */ }
            }
        }
    }
}
