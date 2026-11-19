using FluxCraft.Domain.Instances;
using Xunit;

namespace FluxCraft.Tests.Domain;

public sealed class GameInstanceTests
{
    [Fact]
    public void NewInstance_HasVanillaDefaults()
    {
        var instance = new GameInstance { Name = "Survival" };

        Assert.NotEqual(Guid.Empty, instance.Id);
        Assert.Equal("latest-release", instance.MinecraftVersion);
        Assert.Equal("Vanilla", instance.Loader);
    }
}
