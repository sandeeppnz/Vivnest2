using Vivnest.Runtime.State;
using Vivnest.Domain.Devices;

namespace Vivnest.Agent.Shell.DeviceHealth;

public interface IOfflineDetection
{
    DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedLivenessInterval,
        double warningMultiplier);
}
