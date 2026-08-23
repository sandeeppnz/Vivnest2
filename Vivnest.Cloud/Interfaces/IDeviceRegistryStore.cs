using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

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

    // The device-side counterpart to
    // IAgentRegistryStore.GetByRuntimeAgentIdAsync (ADR-072), missing
    // until ADR-104. Anything arriving from the runtime side - a
    // heartbeat, a device API response, a command's TargetDeviceId -
    // carries the RuntimeDeviceId, never the admin DeviceId this store's
    // own GetAsync expects. Null covers both "no Device has this
    // RuntimeDeviceId yet" and "more than one somehow does" identically:
    // either way there is no single unambiguous Device to act on.
    Task<DeviceRegistryEntity?> GetByRuntimeDeviceIdAsync(
        string tenantId,
        string siteId,
        string runtimeDeviceId,
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
