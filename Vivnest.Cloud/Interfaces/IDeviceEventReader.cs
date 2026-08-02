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

    Task<IReadOnlyList<DeviceEventEntity>> GetByDeviceAndDateRangeAsync(
        string tenantId,
        string siteId,
        string deviceId,
        string? eventType,
        DateTime fromUtc,
        DateTime toUtc,
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

    // Full-table scan, filtered on the business timestamp (OccurredAtUtc),
    // not the system Timestamp - ProcessingStatus updates (MarkCompletedAsync
    // etc.) bump the system Timestamp after creation, which would make
    // retention age unpredictable if that were the filter instead. Fine at
    // this data volume; would need partition-aware batching to scale further.
    Task<int> DeleteOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default);
}
