using System.Collections.Concurrent;
using Vivnest.Core.Interfaces;

namespace Vivnest.Core.Models.Camera;

public sealed class CaptureStatusStore: ICaptureStatusStore
{
    private readonly ConcurrentDictionary<string, DeviceRuntimeState> _statuses = new();

    public DeviceRuntimeState GetOrAdd(string deviceId)
    {
        return _statuses.GetOrAdd(deviceId, _ => new DeviceRuntimeState());
    }

    public bool TryGet(
    string deviceId,
    out DeviceRuntimeState status)
    {
        return _statuses.TryGetValue(
            deviceId,
            out status!);
    }

    public IReadOnlyDictionary<string, DeviceRuntimeState> All => _statuses;
}
