namespace Vivnest.Agent.Runtime.Shell;

public interface INetworkUsageTracker
{
    void AddBytesUploaded(long bytes);

    // Reads and resets to zero, so each read is "since the last read" -
    // matches the delta semantics AgentMetricsWorker already uses for CPU%.
    long TakeBytesUploaded();
}
