using Vivnest.Core.Options;

namespace Vivnest.Core.Devices.SmartPlug;

public interface ISmartPlugFactory
{
    ISmartPlug Create(DeviceOptions deviceOptions);
}
