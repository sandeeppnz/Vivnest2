namespace Vivnest.Core.Camera.Models;

public class CameraCaptureResult
{
    public bool Success { get; init; }

    public string DeviceId { get; set; } = string.Empty;

    public DateTime CapturedAtUtc { get; init; }

    public string? BlobName { get; init; }

    public string? BlobContainer { get; init; }

    public TimeSpan CaptureDuration { get; init; }

    public TimeSpan UploadDuration { get; init; }

    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}
