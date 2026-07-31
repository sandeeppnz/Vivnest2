using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Rules;

public sealed class OfflineDetectionRule : IOfflineDetectionRule
{
    public bool ShouldNotifyOffline(
        DeviceHeartbeatStatus finalStatus,
        DeviceNotificationState currentNotificationState)
    {
        var isOffline =
            finalStatus is DeviceHeartbeatStatus.Offline or DeviceHeartbeatStatus.Error;

        return isOffline
            && currentNotificationState != DeviceNotificationState.OfflineNotified;
    }
}
