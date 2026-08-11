using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IMachineStore
{
    Task<IReadOnlyList<MachineEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<MachineEntity?> GetAsync(
        string tenantId,
        string siteId,
        string machineId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        MachineEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        MachineEntity entity,
        CancellationToken cancellationToken = default);
}
