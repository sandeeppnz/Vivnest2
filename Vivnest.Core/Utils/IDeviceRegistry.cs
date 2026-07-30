using Vivnest.Core.Options;

namespace Vivnest.Core.Utils;

public interface IDeviceRegistry
{
    DeviceOptions GetDevice(string id);

    IReadOnlyCollection<DeviceOptions> GetDevices();

    IReadOnlyCollection<DeviceOptions> GetCameras();
}