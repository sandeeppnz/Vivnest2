using Vivnest.Agent.Interfaces;

namespace Vivnest.Agent.Services;

// Bridges HomeAssistantWorker's connection state (writer, updates on every
// successful handshake/received frame) to AgentHeartbeatWorker (reader,
// includes it in every heartbeat tick) - the two don't otherwise share
// state, and neither should reach into the other directly.
public sealed class HomeAssistantConnectionTracker : IHomeAssistantConnectionTracker
{
    private readonly object _lock = new();
    private DateTime? _lastConnectedUtc;

    public DateTime? LastConnectedUtc
    {
        get
        {
            lock (_lock)
            {
                return _lastConnectedUtc;
            }
        }
    }

    public void MarkConnected()
    {
        lock (_lock)
        {
            _lastConnectedUtc = DateTime.UtcNow;
        }
    }
}
