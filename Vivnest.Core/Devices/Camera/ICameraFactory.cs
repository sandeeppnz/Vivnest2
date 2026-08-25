using Vivnest.Core.Options;

namespace Vivnest.Core.Devices.Camera;

public interface ICameraFactory
{
    ICamera Create(DeviceOptions deviceOptions);
}