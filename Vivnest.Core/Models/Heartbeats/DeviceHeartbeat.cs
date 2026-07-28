using Vivnest.Core.Enums;

namespace Vivnest.Core.Models.Heartbeats;

public class DeviceHeartbeat
{
    public required string AgentId { get; init; }
    public string AgentFirmwareVersion { get; init; } = string.Empty;
    public required string DeviceId { get; init; }
    public required DeviceHeartbeatStatus Status { get; init; }
    public required DeviceType DeviceType { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public string? Error { get; init; }
    public TimeSpan ExpectedActivityInterval { get; init; }

}
