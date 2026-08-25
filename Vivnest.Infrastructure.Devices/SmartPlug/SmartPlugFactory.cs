using Vivnest.Core.Options;
using Vivnest.Core.Devices.SmartPlug;

namespace Vivnest.Infrastructure.SmartPlug;

public sealed class SmartPlugFactory : ISmartPlugFactory
{
    public ISmartPlug Create(DeviceOptions deviceOptions)
    {
        // Every smart plug today is a legacy Kasa-protocol device. If a
        // newer Tapo-protocol plug is added later, branch on a setting
        // here the same way CameraFactory would for a second camera type.
        return new KasaSmartPlug(deviceOptions);
    }
}
