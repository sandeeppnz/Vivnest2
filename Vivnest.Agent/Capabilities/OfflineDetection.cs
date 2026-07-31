using Vivnest.Agent.Interfaces;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Enums;

namespace Vivnest.Agent.Capabilities;

public sealed class OfflineDetection : IOfflineDetection
{
    public DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedActivityInterval)
    {
        if (!string.IsNullOrWhiteSpace(runtime.LastError))
            return DeviceHeartbeatStatus.Error;

        if (runtime.LastCaptureUtc is not { } lastCaptureUtc)
            return DeviceHeartbeatStatus.Unknown;

        var elapsed = DateTime.UtcNow - lastCaptureUtc;

        if (elapsed > expectedActivityInterval + expectedActivityInterval)
            return DeviceHeartbeatStatus.Warning;

        return DeviceHeartbeatStatus.Online;
    }
}
