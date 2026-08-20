namespace Vivnest.Agent.Runtime.Shell;

// Sprint 8. One Error-level log call, captured for the worker that turns it
// into an AgentEvent.
public sealed record AgentErrorSignal(string Category, string Message, string? Exception);

public interface IAgentErrorSignalBuffer
{
    void Add(AgentErrorSignal signal);

    IReadOnlyList<AgentErrorSignal> DrainAll();
}

// Deliberately a buffer drained by a BackgroundService rather than doing
// the persist-and-publish inline in the logger, for three reasons:
//
//   1. Re-entrancy. The publish path logs. If a failure inside it logged an
//      Error that synchronously produced another event, that is an
//      infinite loop - and the errors most worth alerting on are exactly
//      the ones where storage is unhappy.
//   2. The logger provider is constructed before the DI container is
//      built (Program.cs registers it on builder.Logging), so it cannot
//      hold the writer or the queue publisher anyway. AgentLogBuffer
//      already has this exact shape for the same reason.
//   3. Logging must not block on network I/O.
//
// Bounded, and drops NEWEST when full rather than oldest. That is the
// opposite of AgentLogBuffer's ring, and deliberate: this buffer exists to
// notice the first error of a fault, and under a crash-loop the first ones
// are the informative ones. Dropping them to make room for the thousandth
// repetition would defeat the point.
public sealed class AgentErrorSignalBuffer : IAgentErrorSignalBuffer
{
    private readonly object _lock = new();
    private readonly List<AgentErrorSignal> _signals = [];
    private readonly int _maxSignals;

    public AgentErrorSignalBuffer(int maxSignals)
    {
        _maxSignals = maxSignals;
    }

    public void Add(AgentErrorSignal signal)
    {
        lock (_lock)
        {
            if (_signals.Count >= _maxSignals)
                return;

            _signals.Add(signal);
        }
    }

    public IReadOnlyList<AgentErrorSignal> DrainAll()
    {
        lock (_lock)
        {
            if (_signals.Count == 0)
                return [];

            var drained = _signals.ToArray();
            _signals.Clear();

            return drained;
        }
    }
}
