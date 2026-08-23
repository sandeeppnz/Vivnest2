using Vivnest.Core.Runtime;

namespace Vivnest.Agent.Shell;

// Thread-safe ring buffer, not unbounded - constructed before the DI
// container builds (the logger provider needs it immediately), then
// registered as the same singleton instance so LogShippingWorker reads
// from exactly what the provider writes to.
public sealed class AgentLogBuffer : IAgentLogBuffer
{
    private readonly object _lock = new();
    private readonly Queue<string> _lines = new();
    private readonly int _maxLines;

    public AgentLogBuffer(int maxLines)
    {
        _maxLines = maxLines;
    }

    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Enqueue(line);

            while (_lines.Count > _maxLines)
                _lines.Dequeue();
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            return _lines.ToArray();
        }
    }
}
