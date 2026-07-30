using Microsoft.Extensions.Options;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Infrastructure.Utils;


public sealed class DeviceRegistry : IDeviceRuntimeStore
{
    private readonly DevicesOptions _options;

    public DeviceRegistry(IOptions<DevicesOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyCollection<DeviceOptions> GetCameras()
    {
        return _options.Devices
            .Where(d => d.Enabled && d.Type == DeviceType.Camera)
            .ToList();
    }

    public DeviceOptions GetDevice(string id)
    {
        return _options.Devices.Single(d => d.DeviceId == id);
    }

    public IReadOnlyCollection<DeviceOptions> GetDevices()
    {
        return _options.Devices;
    }
}