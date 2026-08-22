using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Enums;

namespace Vivnest.Agent.Capabilities.DeviceHealth;

public sealed class OfflineDetection : IOfflineDetection
{
    public DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedLivenessInterval,
        double warningMultiplier)
    {
        if (!string.IsNullOrWhiteSpace(runtime.LastError))
            return DeviceHeartbeatStatus.Error;

        if (runtime.LastActivityUtc is not { } lastActivityUtc)
            return DeviceHeartbeatStatus.Unknown;

        var elapsed = DateTime.UtcNow - lastActivityUtc;

        if (elapsed > expectedLivenessInterval * warningMultiplier)
            return DeviceHeartbeatStatus.Degraded;

        return DeviceHeartbeatStatus.Healthy;
    }
}
