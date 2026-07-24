namespace Vivnest.Core.Models;

public sealed class CaptureStatus
{
    public bool HasStarted { get; set; }
    public DateTime? LastCaptureUtc { get; set; }
    public string? LastBlobName { get; set; }
    public string? LastError { get; set; }
}