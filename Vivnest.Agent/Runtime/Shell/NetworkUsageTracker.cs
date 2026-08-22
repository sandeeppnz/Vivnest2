using Vivnest.Abstraction.Agent.Runtime;

namespace Vivnest.Agent.Runtime.Shell;

// Deliberately measures at the application level (bytes actually handed to
// blob upload), not OS network-interface counters - those are Linux-only
// (/proc/net/dev), which would break the same "runs via dotnet run on
// Windows and in a Linux container" duality CPU/Memory sampling already
// has to respect (see decision-log.md ADR-020).
public sealed class NetworkUsageTracker : INetworkUsageTracker
{
    private long _bytesUploaded;

    public void AddBytesUploaded(long bytes)
    {
        Interlocked.Add(ref _bytesUploaded, bytes);
    }

    public long TakeBytesUploaded()
    {
        return Interlocked.Exchange(ref _bytesUploaded, 0);
    }
}
