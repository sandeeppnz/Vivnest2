using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Enums;

namespace Vivnest.Agent.Capabilities.DeviceHealth;

public interface IOfflineDetection
{
    DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedLivenessInterval,
        double warningMultiplier);
}
