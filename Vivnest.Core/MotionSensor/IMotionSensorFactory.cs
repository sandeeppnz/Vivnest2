using Vivnest.Core.Options;

namespace Vivnest.Core.MotionSensor;

public interface IMotionSensorFactory
{
    IMotionSensor Create(DeviceOptions deviceOptions);
}
