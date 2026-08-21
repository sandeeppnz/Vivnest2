namespace Vivnest.Abstractions;

public interface INetworkUsageTracker
{
    void AddBytesUploaded(long bytes);

    // Reads and resets to zero, so each read is "since the last read" -
    // matches the delta semantics PlatformAgentMetricsWorker already uses for CPU%.
    long TakeBytesUploaded();
}
