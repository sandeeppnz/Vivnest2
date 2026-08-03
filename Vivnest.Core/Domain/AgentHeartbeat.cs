namespace Vivnest.Core.Domain;

public class AgentHeartbeat : BaseIdentity
{
    public DateTime StartedUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string? Error { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }

    // Null when Home Assistant integration is disabled, or hasn't
    // connected even once yet - not the same as "was connected, now stale."
    public DateTime? HomeAssistantLastConnectedUtc { get; set; }
}
