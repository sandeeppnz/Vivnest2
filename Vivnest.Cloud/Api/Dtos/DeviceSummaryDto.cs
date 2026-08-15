namespace Vivnest.Cloud.Api.Dtos;

// ConfigurationStatus (decision-log.md ADR-075, Phase 8 Pass 2) - same
// live-per-row computation as AgentSummaryDto.ConfigurationStatus, via the
// lightweight ConfigurationSyncStatusService overload.
public sealed record DeviceSummaryDto(
    string DeviceId,
    string Name,
    string DeviceType,
    string Status,
    DateTime? StatusSinceUtc,
    DateTime LastHeartbeatUtc,
    DateTime? LastActivityUtc,
    TimeSpan HeartbeatInterval,
    string AgentId,
    string TenantId,
    string SiteId,
    string? Error,
    string? ParentDeviceId,
    string Timezone,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    string? ThumbnailUrl,
    bool SinkCleanlinessEnabled,
    bool ObjectDetectionEnabled,
    ConfigurationSyncStatusDto ConfigurationStatus,
    // Admin-set DeviceRegistryStatus ("Active"/"Disabled"/"Retired"),
    // decision-log.md ADR-076 - null if this heartbeat's RuntimeDeviceId
    // doesn't map to any registered Device. See AgentSummaryDto.LifecycleStatus
    // for why this stays a separate field from Status.
    string? LifecycleStatus) : IMonitorable
{
    string IMonitorable.Id => DeviceId;
}
