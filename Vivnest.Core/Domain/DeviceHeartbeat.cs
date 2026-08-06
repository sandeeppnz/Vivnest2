using Vivnest.Core.Enums;

namespace Vivnest.Core.Domain;

public class DeviceHeartbeat : BaseIdentity
{
    public required string DeviceId { get; init; }

    // The configured, human-friendly display name (DeviceOptions.Name) -
    // same pattern as AgentHeartbeat.Name. Empty when never configured;
    // consumers fall back to DeviceId in that case.
    public string Name { get; init; } = string.Empty;

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
