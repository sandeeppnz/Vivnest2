namespace Vivnest.Cloud.Api.Dtos;

// Decision-log.md ADR-079 - the body for PUT .../commands/{commandId}/status,
// the Agent's own status-transition callback. TenantId/SiteId travel in
// the body rather than a tenant x-api-key - same "no key, trust the
// explicit identity" model AgentInstallationManagementService.
// ReportDeployCompleteAsync's own callback already established for
// Agent/Updater-originated calls.
public sealed record AgentCommandStatusUpdateRequest(
    string TenantId,
    string SiteId,
    string Status,
    string? Result,
    string? ErrorCode,
    string? ErrorMessage);
