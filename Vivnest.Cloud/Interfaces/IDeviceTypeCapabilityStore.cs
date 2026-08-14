using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceTypeCapabilityStore
{
    Task<IReadOnlyList<DeviceTypeCapabilityEntity>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceTypeCapabilityEntity>> ListByDeviceTypeAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default);

    Task<DeviceTypeCapabilityEntity?> GetAsync(
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        DeviceTypeCapabilityEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken = default);
}
