using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceSnapshotStateReader
{
    Task<DeviceSnapshotStateEntity?> GetAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task UpdateLastNotifiedAsync(
        string tenantId,
        string siteId,
        string agentId,
        string deviceId,
        DateTime lastNotifiedUtc,
        CancellationToken cancellationToken = default);
}
