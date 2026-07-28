using Vivnest.Core.Enums;

namespace Vivnest.Core.Models.Heartbeats;

public class AgentHeartbeat: BaseIdentity
{
    public DateTime StartedUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    //public required string FirmwareVersion { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string? Error { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }
}
