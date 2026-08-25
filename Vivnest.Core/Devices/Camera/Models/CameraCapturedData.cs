namespace Vivnest.Core.Devices.Camera.Models;

public sealed class CameraCapturedData
{
    public string BlobContainer { get; init; } = default!;
    public required string BlobName { get; init; }
    public required DateTime CapturedAt { get; init; }
    public TimeSpan CaptureDuration { get; init; }
    public TimeSpan UploadDuration { get; init; }

    // Set only for a capture fired by CaptureOnTriggerHandler (e.g.
    // "Motion:motion-001") - null for a normal scheduled capture. Lets the
    // dashboard badge triggered captures instead of showing them
    // identically to routine ones.
    public string? TriggerReason { get; init; }
}
