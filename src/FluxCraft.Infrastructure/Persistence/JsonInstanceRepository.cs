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
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

            List<GameInstance> instances;
            if (File.Exists(_filePath))
            {
                await using var read = File.OpenRead(_filePath);
                instances = await JsonSerializer.DeserializeAsync<List<GameInstance>>(read, JsonOptions, cancellationToken) ?? [];
            }
            else instances = [];

            var index = instances.FindIndex(x => x.Id == instance.Id);
            if (index >= 0) instances[index] = instance;
            else instances.Add(instance);

            var temporaryPath = _filePath + ".tmp";
            await using (var write = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(write, instances, JsonOptions, cancellationToken);

            File.Move(temporaryPath, _filePath, true);
        }
        finally { _gate.Release(); }
    }
}
