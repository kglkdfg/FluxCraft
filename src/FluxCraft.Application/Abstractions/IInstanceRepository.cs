using FluxCraft.Domain.Instances;

namespace FluxCraft.Application.Abstractions;

public interface IInstanceRepository
{
    Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GameInstance instance, CancellationToken cancellationToken = default);
}
