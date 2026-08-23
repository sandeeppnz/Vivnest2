using Vivnest.Core.Devices.Stores;
using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.DeviceHealth;

public interface IOfflineDetection
{
    DeviceHeartbeatStatus Evaluate(
        DeviceRuntimeState runtime,
        TimeSpan expectedLivenessInterval,
        double warningMultiplier);
}
