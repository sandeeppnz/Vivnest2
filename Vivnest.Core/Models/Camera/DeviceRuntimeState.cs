namespace Vivnest.Core.Models.Camera;

public sealed class DeviceRuntimeState
{
    public bool HasStarted { get; set; }
    public string? LastBlobName { get; set; }
    public string? LastError { get; set; }

    public DateTime? LastCaptureUtc { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public DateTime? LastStartedUtc { get; set; }
}