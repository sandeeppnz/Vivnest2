using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Interfaces;

public interface IRecoveryDetectionRule
{
    bool ShouldNotifyRecovery(
        DeviceHeartbeatStatus finalStatus,
        DeviceNotificationState currentNotificationState);
}
