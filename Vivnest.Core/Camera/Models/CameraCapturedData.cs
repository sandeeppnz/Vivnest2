namespace Vivnest.Core.Camera.Models;

public sealed class CameraCapturedData
{
    public string BlobContainer { get; init; } = default!;
    public required string BlobName { get; init; }
    public required DateTime CapturedAt { get; init; }
    public TimeSpan CaptureDuration { get; init; }
    public TimeSpan UploadDuration { get; init; }
}
