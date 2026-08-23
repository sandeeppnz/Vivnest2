using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Infrastructure.Utils;


public sealed class DeviceRuntimeStore : IDeviceRuntimeStore
{
    private readonly DevicesOptions _options;

    public DeviceRuntimeStore(IOptions<DevicesOptions> options)
    {
        _options = options.Value;
    }

    public DeviceOptions GetDevice(string id, DeviceType type)
    {
        var matches = _options.Devices
            .Where(d => d.DeviceId == id && d.Type == type)
            .ToList();

        if (matches.Count == 0)
            throw new KeyNotFoundException(
                $"No device configured with id '{id}' and type '{type}'.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple devices configured with id '{id}' and type '{type}'.");

        return matches[0];
    }

    public IReadOnlyCollection<DeviceOptions> GetDevices(string id)
    {
        return _options.Devices
            .Where(d => d.DeviceId == id)
            .ToList();
    }

    public IReadOnlyCollection<DeviceOptions> GetDevices()
    {
        return _options.Devices
            .Where(d => d.Enabled)
            .ToList();
    }
}
