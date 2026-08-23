using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Interfaces;

public interface IRecoveryDetectionRule
{
    bool ShouldNotifyRecovery(
        DeviceHeartbeatStatus finalStatus,
        DeviceNotificationState currentNotificationState);
}
