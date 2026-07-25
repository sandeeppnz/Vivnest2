using Vivnest.Core.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceEventRepository
{
    Task<DeviceEventEntity?> GetAsync(
        string partitionKey,
        string rowKey,
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