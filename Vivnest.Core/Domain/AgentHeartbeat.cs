namespace Vivnest.Core.Domain;

public class AgentHeartbeat : BaseIdentity
{
    public DateTime StartedUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;

    // Snapshot host facts, captured once at process start (RuntimeInformation) -
    // like FirmwareVersion, not a time-series metric, so these live on the
    // heartbeat, not AgentEvent.
    public string RuntimeVersion { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;

    public string? Error { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }

    // Null when Home Assistant integration is disabled, or hasn't
    // connected even once yet - not the same as "was connected, now stale."
    public DateTime? HomeAssistantLastConnectedUtc { get; set; }
}
