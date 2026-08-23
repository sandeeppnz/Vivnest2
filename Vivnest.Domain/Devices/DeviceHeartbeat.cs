using Vivnest.Domain.Shared;

namespace Vivnest.Domain.Devices;

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

    // Whether this camera has SinkCleanliness/ObjectDetection configured
    // and enabled (DeviceOptions, ADR-032/034) - config, not a live
    // reading, but safe to denormalize the same way: any config change
    // needs an Agent restart to take effect, and DeviceHeartbeatWorker
    // already republishes once on every process start (ADR-005), so
    // there's no staleness window unlike ADR-030's thumbnail case.
    public bool SinkCleanlinessEnabled { get; init; }
    public bool ObjectDetectionEnabled { get; init; }

    // DeviceOptions.ConfigurationPublishedUtc (decision-log.md ADR-065) -
    // when this device's config was last published by Admin; null for a
    // legacy-shape device never published through that pipeline. Same
    // "config change needs a restart, and a restart always republishes"
    // reasoning as SinkCleanlinessEnabled/ObjectDetectionEnabled above -
    // no staleness window despite this being config, not a live reading.
    public DateTime? ConfigurationPublishedUtc { get; init; }

    // DeviceOptions.ConfigurationVersion/ConfigurationHash (decision-log.md
    // ADR-069) - null unless this device was loaded via the new versioned
    // manifest path. Same no-staleness-window reasoning as
    // ConfigurationPublishedUtc above.
    public int? ConfigurationVersion { get; init; }
    public string? ConfigurationHash { get; init; }

    // Cloud
    public DeviceHeartbeatStatus Status { get; set; }
    public DeviceNotificationState? NotificationState { get; set; }
    public DateTime? LastOfflineNotificationUtc { get; set; }
    public DateTime? LastRecoveredUtc { get; set; }

}
