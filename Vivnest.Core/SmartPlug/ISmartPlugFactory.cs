using Vivnest.Core.Options;

namespace Vivnest.Core.SmartPlug;

public interface ISmartPlugFactory
{
    ISmartPlug Create(DeviceOptions deviceOptions);
}
