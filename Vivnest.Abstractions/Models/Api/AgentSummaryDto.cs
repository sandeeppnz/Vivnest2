namespace Vivnest.Abstractions.Models.Api;

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
    AgentVersionStatusDto VersionStatus,
    // Admin-set AgentRegistryStatus ("Active"/"Inactive"), decision-log.md
    // ADR-076 - null if this heartbeat's RuntimeAgentId doesn't map to any
    // registered Agent. Kept separate from Status (which becomes
    // NotApplicable when this is "Inactive") rather than collapsed into
    // it - the spec's own "DeviceStatus = Disabled, OperationalStatus =
    // N/A" example, shown side by side, never merged into one value.
    string? LifecycleStatus) : IMonitorable
{
    string IMonitorable.Id => AgentId;
    DateTime? IMonitorable.StatusSinceUtc => StatusSinceUtc;
}
