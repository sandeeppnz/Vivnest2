using Vivnest.Runtime.State;
using Vivnest.Domain.Devices;

namespace Vivnest.Agent.Shell.DeviceHealth;

// Despite the name, this never returns DeviceHeartbeatStatus.Offline. It
// returns Error, Unknown, Degraded or Healthy - what this Agent can
// observe about a device it is polling.
//
// Offline is Cloud's call, not the Agent's: Vivnest.Cloud's
// DeviceStatusResolver produces it, and OfflineDetectionRule decides
// whether it is worth telling someone about. An Agent cannot honestly
// report a device as Offline anyway - if the Agent itself is down, the
// device it would have reported on is unreachable in a way no local
// evaluation can see.
//
// The name is kept because it names the ROLE, and it pairs deliberately
// with the Cloud-side OfflineDetectionRule it feeds. Renamed, the pair
// would stop reading as a pair.
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
