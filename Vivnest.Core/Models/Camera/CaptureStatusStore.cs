using System.Collections.Concurrent;

namespace Vivnest.Core.Models.Camera;

public sealed class CaptureStatusStore
{
    private readonly ConcurrentDictionary<string, CaptureStatus> _statuses = new();

    public CaptureStatus GetOrAdd(string deviceId)
    {
        return _statuses.GetOrAdd(deviceId, _ => new CaptureStatus());
    }

    public IReadOnlyDictionary<string, CaptureStatus> All => _statuses;
}
