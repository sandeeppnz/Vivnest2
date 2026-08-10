using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceRegistryStore
{
    Task<IReadOnlyList<DeviceRegistryEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<DeviceRegistryEntity?> GetAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        DeviceRegistryEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        DeviceRegistryEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default);
}
