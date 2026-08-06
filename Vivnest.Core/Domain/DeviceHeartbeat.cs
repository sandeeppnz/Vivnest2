using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

public class DeviceHeartbeat : BaseIdentity
{
    public required string DeviceId { get; init; }
    public required DeviceType DeviceType { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public string? Error { get; init; }
    public TimeSpan ExpectedLivenessInterval { get; init; }
    public TimeSpan ExpectedHeartbeatInterval { get; init; }
    public DeviceHeartbeatSource Source { get; init; }
    public string? ParentDeviceId { get; init; }


    // Cloud
    public DeviceHeartbeatStatus Status { get; set; }
    public DeviceNotificationState? NotificationState { get; set; }
    public DateTime? LastOfflineNotificationUtc { get; set; }
    public DateTime? LastRecoveredUtc { get; set; }

}
