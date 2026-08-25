using Vivnest.Core.Devices.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Camera;

public sealed class CameraFactory : ICameraFactory
{
    public ICamera Create(DeviceOptions deviceOptions)
    {
        // Every camera is currently an RTSP/Tapo device. If you add other
        // camera types later, branch on a setting here (e.g. deviceOptions
        // .Settings.Model) and return the right ICamera implementation.
        return new TapoC120Camera(new RtspCamera(deviceOptions));
    }
}
