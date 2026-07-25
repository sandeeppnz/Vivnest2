using Vivnest.Core.Enums;

namespace Vivnest.Core.Models;

public class Heartbeat
{
    public required string AgentId { get; init; }
    public required string DeviceId { get; init; }
    public required HeartbeatStatus Status { get; init; }
    public required string Version { get; init; }
    public string? Error { get; init; }
    public DateTime LastSeenUtc { get; set; }
    public Dictionary<string, object>? Payload { get; init; }
    public string? BlobName { get; init; }
    public DateTime? LastCaptureUtc { get; set; }

}
