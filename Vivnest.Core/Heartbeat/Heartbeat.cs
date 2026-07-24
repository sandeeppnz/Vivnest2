using Vivnest.Core.Models;

namespace Vivnest.Core.Heartbeat;

public enum HeartbeatStatus
{
    Healthy,
    Unhealthy,
    Error,
    Starting,
    Stopping,
    Offline,
    Degraded
}

public class Heartbeat
{
    public required string AgentId { get; init; }
    public required HeartbeatStatus Status { get; init; }
    public required string Version { get; init; }
    public string? BlobName { get; init; }
    public string? Error { get; init; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastCaptureUtc { get; set; }
    public static Heartbeat FromCaptureResult(
    CaptureResult result,
    string agentId,
    string version)
    {
        return new Heartbeat
        {
            AgentId = agentId,
            Status = result.Success
                ? HeartbeatStatus.Healthy
                : HeartbeatStatus.Error,
            LastSeenUtc = DateTime.UtcNow,
            LastCaptureUtc = result.CapturedAt,
            Version = version,
            BlobName = result.BlobName,
            Error = result.Error
        };
    }
}
