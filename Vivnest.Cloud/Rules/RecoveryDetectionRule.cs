using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Rules;

public sealed class RecoveryDetectionRule : IRecoveryDetectionRule
{
    public bool ShouldNotifyRecovery(
        DeviceHeartbeatStatus finalStatus,
        DeviceNotificationState currentNotificationState)
    {
        return finalStatus == DeviceHeartbeatStatus.Healthy
            && currentNotificationState == DeviceNotificationState.OfflineNotified;
    }
}
