using Microsoft.Extensions.Options;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Services;

public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly DevicesOptions _options;

    public DeviceRegistry(IOptions<DevicesOptions> options)
    {
        _options = options.Value;
    }

    public CameraDeviceOptions GetCamera()
    {
        return _options.Devices.Single(d =>
            d.Enabled &&
            d.Type == DeviceType.Camera);
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