namespace FluxCraft.Domain.Instances;

public sealed class GameInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string MinecraftVersion { get; set; } = "latest-release";
    public string Loader { get; set; } = "Vanilla";
    public string? JavaExecutablePath { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
