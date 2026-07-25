using Vivnest.Core.Options;

namespace Vivnest.Core.Interfaces;

public interface ICameraFactory
{
    ICamera Create(DeviceOptions deviceOptions);
}