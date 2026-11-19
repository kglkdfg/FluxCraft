using System.Text.Json;
using FluxCraft.Application.Abstractions;
using FluxCraft.Domain.Instances;

namespace FluxCraft.Infrastructure.Persistence;

public sealed class JsonInstanceRepository : IInstanceRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonInstanceRepository(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluxCraft", "instances.json");
    }

    public async Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<GameInstance>();

            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<GameInstance>>(stream, JsonOptions, cancellationToken)
                   ?? [];
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var instances = await ReadUnsafeAsync(cancellationToken);
            var index = instances.FindIndex(x => x.Id == instance.Id);
            if (index >= 0) instances[index] = instance;
            else instances.Add(instance);
            await WriteUnsafeAsync(instances, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var instances = await ReadUnsafeAsync(cancellationToken);
            instances.RemoveAll(x => x.Id == instanceId);
            await WriteUnsafeAsync(instances, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<GameInstance>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return [];
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<GameInstance>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteUnsafeAsync(List<GameInstance> instances, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        await using (var write = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(write, instances, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }
}
