using Vivnest.Core.MotionSensor;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.MotionSensor;

public sealed class MotionSensorFactory : IMotionSensorFactory
{
    public IMotionSensor Create(DeviceOptions deviceOptions)
    {
        // Every motion sensor today is a Tapo hub child (T100 under an
        // H100). If a differently-connected motion sensor is added later,
        // branch on a setting here the same way CameraFactory would.
        return new TapoMotionSensor(deviceOptions);
    }
}
