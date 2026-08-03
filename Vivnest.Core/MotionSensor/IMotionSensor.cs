using Vivnest.Core.MotionSensor.Models;

namespace Vivnest.Core.MotionSensor;

public interface IMotionSensor
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    Task<MotionSensorState> GetStateAsync(CancellationToken cancellationToken = default);
}
