using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Interfaces;

public interface IOfflineDetectionRule
{
    bool ShouldNotifyOffline(
        DeviceHeartbeatStatus finalStatus,
        DeviceNotificationState currentNotificationState);
}
