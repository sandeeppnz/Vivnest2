using Vivnest.Core.Models;

namespace Vivnest.Core.Heartbeat;

public enum HeartbeatStatus
{
    Healthy,
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

    public required DateTime LastCaptureUtc { get; init; }
    public DateTime HeartbeatUtc { get; init; }

    public required string Version { get; init; }

    public string? BlobName { get; init; }

    public double CaptureDurationMs { get; init; }

    public double UploadDurationMs { get; init; }

    public string? Error { get; init; }

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
            LastCaptureUtc = result.CapturedAt,
            HeartbeatUtc = DateTime.UtcNow,
            Version = version,
            BlobName = result.BlobName,
            CaptureDurationMs = result.CaptureDuration.TotalMilliseconds,
            UploadDurationMs = result.UploadDuration.TotalMilliseconds,
            Error = result.Error
        };
    }
}
