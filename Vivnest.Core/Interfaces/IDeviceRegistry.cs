using Vivnest.Core.Options;

namespace Vivnest.Core.Interfaces;

public interface IDeviceRegistry
{
    DeviceOptions GetDevice(string id);

    IReadOnlyCollection<DeviceOptions> GetDevices();

    IReadOnlyCollection<DeviceOptions> GetCameras();
}