using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceEventReader
{
    Task<DeviceEventEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceEventEntity>> GetByDeviceAsync(
        string tenantId,
        string siteId,
        string deviceId,
        string? eventType,
        int take,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string partitionKey,
        string rowKey,
        string error,
        CancellationToken cancellationToken = default);
}
