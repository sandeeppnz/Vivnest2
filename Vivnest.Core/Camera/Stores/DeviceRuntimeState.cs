using Vivnest.Core.Enums;

namespace Vivnest.Core.Camera.Stores;

public sealed class DeviceRuntimeState
{
    public bool IsRunning { get; set; }
    public string? LastBlobName { get; set; }
    public string? LastError { get; set; }

    public DateTime? LastCaptureUtc { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public DateTime? LastStartedUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }

    // Last status sent via DeviceHeartbeat, for change detection.
    public DeviceHeartbeatStatus? LastReportedStatus { get; set; }
}