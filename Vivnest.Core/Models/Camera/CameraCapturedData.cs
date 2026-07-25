namespace Vivnest.Core.Models.Camera;

public sealed class CameraCapturedData
{
    public required string BlobName { get; init; }
    public required DateTime CapturedAt { get; init; }
    public TimeSpan CaptureDuration { get; init; }
    public TimeSpan UploadDuration { get; init; }
}