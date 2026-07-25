using Vivnest.Core.Options;

namespace Vivnest.Core.Interfaces;

public interface IDeviceRegistry
{
    DeviceOptions GetCamera();

    DeviceOptions GetDevice(string id);

    IReadOnlyCollection<DeviceOptions> GetDevices();
}