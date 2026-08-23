using Vivnest.Core.MotionSensor.Models;
using Vivnest.Core.Options;

namespace Vivnest.Capabilities.MotionSensor;

public interface IMotionSensorMonitorService
{
    Task<MotionSensorReadingResult> ReadAsync(
        DeviceOptions sensorOptions,
        CancellationToken cancellationToken);

    Task<bool> CheckReachabilityAsync(
        DeviceOptions sensorOptions,
        CancellationToken cancellationToken);
}
