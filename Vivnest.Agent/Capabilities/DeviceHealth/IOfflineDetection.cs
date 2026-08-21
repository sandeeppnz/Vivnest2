using Vivnest.Abstractions.Enums;
using Vivnest.Core.Devices.Stores;

namespace Vivnest.Agent.Capabilities.DeviceHealth;

public interface IOfflineDetection
{
    DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedLivenessInterval,
        double warningMultiplier);
}
