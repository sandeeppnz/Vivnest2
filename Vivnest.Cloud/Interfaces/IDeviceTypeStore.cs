using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceTypeStore
{
    Task<IReadOnlyList<DeviceTypeEntity>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceTypeEntity?> GetAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        DeviceTypeEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        DeviceTypeEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default);
}
