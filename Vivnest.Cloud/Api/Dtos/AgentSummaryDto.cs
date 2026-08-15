namespace Vivnest.Cloud.Api.Dtos;

// ConfigurationStatus/VersionStatus (decision-log.md ADR-075, Phase 8
// Pass 2) - computed live per row via the lightweight
// ConfigurationSyncStatusService/AgentVersionStatusService overloads, same
// "fine at this scale" reasoning already used elsewhere in this codebase
// for per-row O(N) computation (DeviceCapabilitiesQueryService,
// AgentInstallationsAdmin's own per-agent fetch).
public sealed record AgentSummaryDto(
    string AgentId,
    string Name,
    string HostName,
    string FirmwareVersion,
    string RuntimeVersion,
    string OsDescription,
    string Status,
    DateTime StartedUtc,
    DateTime LastHeartbeatUtc,
    TimeSpan HeartbeatInterval,
    DateTime StatusSinceUtc,
    string TenantId,
    string SiteId,
    string? Error,
    ConfigurationSyncStatusDto ConfigurationStatus,
    AgentVersionStatusDto VersionStatus) : IMonitorable
{
    string IMonitorable.Id => AgentId;
    DateTime? IMonitorable.StatusSinceUtc => StatusSinceUtc;
}
