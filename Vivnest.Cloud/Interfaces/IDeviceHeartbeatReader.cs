using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceHeartbeatReader
{
    Task<IReadOnlyList<DeviceHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceHeartbeatEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task UpdateNotificationStateAsync(
        DeviceHeartbeatEntity entity,
        DeviceNotificationState notificationState,
        DateTime? lastOfflineNotificationUtc,
        DateTime? lastRecoveredUtc,
        CancellationToken cancellationToken = default);
}
