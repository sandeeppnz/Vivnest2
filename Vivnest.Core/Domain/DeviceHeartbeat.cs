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

    // The owning agent's configured IANA timezone (AgentOptions.Timezone),
    // stamped by the same Agent process that already knows AgentId/TenantId/
    // SiteId - denormalized onto the device row so Cloud-side day-grouping
    // (capture galleries) doesn't need a separate agent lookup. Empty falls
    // back to UTC.
    public string Timezone { get; init; } = string.Empty;

    // Descriptive-only fields from DeviceOptions - never read by any
    // capability's worker to decide behavior (see DeviceOptions.cs). A
    // fallback for device types that can't self-report them; where a
    // capability already reports one as a live reading (e.g.
    // SmartPlugState.Brand), that reading should take precedence for
    // display - not implemented yet, these are the static config value only.
    public string Location { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Firmware { get; init; } = string.Empty;


    // Cloud
    public DeviceHeartbeatStatus Status { get; set; }
    public DeviceNotificationState? NotificationState { get; set; }
    public DateTime? LastOfflineNotificationUtc { get; set; }
    public DateTime? LastRecoveredUtc { get; set; }

}
