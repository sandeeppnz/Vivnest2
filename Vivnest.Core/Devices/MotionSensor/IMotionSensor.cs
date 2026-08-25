using Vivnest.Core.Devices.MotionSensor.Models;

namespace Vivnest.Core.Devices.MotionSensor;

public interface IMotionSensor
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    Task<MotionSensorState> GetStateAsync(CancellationToken cancellationToken = default);
}
