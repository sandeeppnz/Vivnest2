using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface ICapabilityStore
{
    Task<IReadOnlyList<CapabilityEntity>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<CapabilityEntity?> GetAsync(
        string capabilityId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        CapabilityEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        CapabilityEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string capabilityId,
        CancellationToken cancellationToken = default);
}
