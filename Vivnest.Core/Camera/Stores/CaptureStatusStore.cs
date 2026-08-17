using System.Collections.Concurrent;

namespace Vivnest.Core.Camera.Stores;

public sealed class CaptureStatusStore : ICaptureStatusStore
{
    private readonly ConcurrentDictionary<string, DeviceRuntimeState> _statuses = new();

    public DeviceRuntimeState GetOrAdd(string deviceId)
    {
        return _statuses.GetOrAdd(deviceId, _ => new DeviceRuntimeState());
    }
}
