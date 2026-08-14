using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface ICapabilityDependencyStore
{
    Task<IReadOnlyList<CapabilityDependencyEntity>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CapabilityDependencyEntity>> ListByCapabilityAsync(
        string capabilityId,
        CancellationToken cancellationToken = default);

    Task<CapabilityDependencyEntity?> GetAsync(
        string dependencyId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        CapabilityDependencyEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string dependencyId,
        CancellationToken cancellationToken = default);
}
