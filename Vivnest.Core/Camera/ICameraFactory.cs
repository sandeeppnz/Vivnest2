using Vivnest.Core.Options;

namespace Vivnest.Core.Camera;

public interface ICameraFactory
{
    ICamera Create(DeviceOptions deviceOptions);
}