using FluxCraft.Domain.Instances;
using FluxCraft.Infrastructure.Persistence;
using Xunit;

namespace FluxCraft.Tests.Persistence;

public sealed class JsonInstanceRepositoryTests
{
    [Fact]
    public async Task SaveAndGetAll_PersistsInstance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxCraft.Tests", Guid.NewGuid().ToString("N"));
        var repository = new JsonInstanceRepository(Path.Combine(directory, "instances.json"));
        var instance = new GameInstance { Name = "Creative", MinecraftVersion = "1.20.4" };

        try
        {
            await repository.SaveAsync(instance);
            var loaded = await repository.GetAllAsync();

            var saved = Assert.Single(loaded);
            Assert.Equal(instance.Id, saved.Id);
            Assert.Equal("Creative", saved.Name);
            Assert.Equal("1.20.4", saved.MinecraftVersion);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_RemovesOnlyRequestedInstance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxCraft.Tests", Guid.NewGuid().ToString("N"));
        var repository = new JsonInstanceRepository(Path.Combine(directory, "instances.json"));
        var first = new GameInstance { Name = "First" };
        var second = new GameInstance { Name = "Second" };

        try
        {
            await repository.SaveAsync(first);
            await repository.SaveAsync(second);
            await repository.DeleteAsync(first.Id);

            var remaining = Assert.Single(await repository.GetAllAsync());
            Assert.Equal(second.Id, remaining.Id);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
