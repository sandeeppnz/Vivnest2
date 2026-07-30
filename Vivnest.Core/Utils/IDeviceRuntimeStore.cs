using Vivnest.Core.Options;

namespace Vivnest.Core.Utils;

public interface IDeviceRuntimeStore
{
    DeviceOptions GetDevice(string id);

    IReadOnlyCollection<DeviceOptions> GetDevices();

    IReadOnlyCollection<DeviceOptions> GetCameras();
}