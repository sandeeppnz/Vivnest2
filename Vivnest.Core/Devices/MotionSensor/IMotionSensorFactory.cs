using Vivnest.Core.Options;

namespace Vivnest.Core.Devices.MotionSensor;

public interface IMotionSensorFactory
{
    IMotionSensor Create(DeviceOptions deviceOptions);
}
