using Vivnest.Core.Enums;

namespace Vivnest.Core.Models.Heartbeats;

public class DeviceHeartbeat: BaseIdentity
{
    public required string DeviceId { get; init; }
    public required DeviceHeartbeatStatus Status { get; init; }
    public required DeviceType DeviceType { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public string? Error { get; init; }
    public TimeSpan ExpectedActivityInterval { get; init; }

}
