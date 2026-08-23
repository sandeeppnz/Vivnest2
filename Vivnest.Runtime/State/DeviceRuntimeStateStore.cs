using System.Collections.Concurrent;

namespace Vivnest.Runtime.State;

public sealed class DeviceRuntimeStateStore : IDeviceRuntimeStateStore
{
    private readonly ConcurrentDictionary<string, DeviceRuntimeState> _statuses = new();

    public DeviceRuntimeState GetOrAdd(string deviceId)
    {
        return _statuses.GetOrAdd(deviceId, _ => new DeviceRuntimeState());
    }
}
