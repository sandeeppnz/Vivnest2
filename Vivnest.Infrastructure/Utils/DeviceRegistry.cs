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
        var matches = _options.Devices
            .Where(d => d.DeviceId == id)
            .ToList();

        if (matches.Count == 0)
            throw new KeyNotFoundException(
                $"No device configured with id '{id}'.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple devices configured with id '{id}'.");

        return matches[0];
    }

    public IReadOnlyCollection<DeviceOptions> GetDevices()
    {
        return _options.Devices;
    }
}