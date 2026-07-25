namespace Vivnest.Core.Models.Camera;

public class CaptureResult
{
    public bool Success { get; init; }

    public string DeviceId { get; set; } = string.Empty;

    public DateTime CapturedAt { get; init; }

    public string? BlobName { get; init; }

    public string? BlobContainer { get; init; }

    public TimeSpan CaptureDuration { get; init; }

    public TimeSpan UploadDuration { get; init; }

    public string? Error { get; init; }
}
