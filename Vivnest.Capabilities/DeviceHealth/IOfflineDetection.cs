using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Enums;

namespace Vivnest.Capabilities.DeviceHealth;

public interface IOfflineDetection
{
    DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedLivenessInterval,
        double warningMultiplier);
}
