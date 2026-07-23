namespace Vivnest.Core.Models;

public class CaptureResult
{
    public bool Success { get; init; }

    public DateTime CapturedAt { get; init; }

    public string? BlobName { get; init; }

    public TimeSpan CaptureDuration { get; init; }

    public TimeSpan UploadDuration { get; init; }

    public string? Error { get; init; }
}
