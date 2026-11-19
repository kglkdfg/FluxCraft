namespace FluxCraft.Application.Abstractions;

public sealed record JavaRuntimeInstallation(string Component, int MajorVersion, string ExecutablePath);

public interface IJavaRuntimeInstaller
{
    Task<JavaRuntimeInstallation> EnsureInstalledAsync(
        string minecraftVersion,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
