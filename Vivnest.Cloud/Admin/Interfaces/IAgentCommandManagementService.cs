using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin.Interfaces;

// Decision-log.md ADR-079 - plain tenantId/siteId strings throughout
// rather than TenantContext, same convention
// AgentInstallationManagementService.NoteAgentHeartbeatAsync/
// ReportDeployCompleteAsync already established for methods called from
// both a dashboard's authenticated route and an Agent-trusted one (no
// tenant x-api-key involved) - the Function layer extracts tenantId/
// siteId from whichever context applies before calling in.
public interface IAgentCommandManagementService
{
    Task<IReadOnlyList<AgentCommandDto>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentCommandDto?> GetAsync(
        string tenantId,
        string siteId,
        string commandId,
        CancellationToken cancellationToken = default);

    // Idempotent - a status-transition guard, not a blind write. Only
    // applies if the command isn't already in a terminal state
    // (Succeeded/Failed/Expired/Cancelled); a duplicate/late call after
    // that point is a no-op that returns the already-persisted result
    // instead of re-applying - see decision-log.md ADR-079's idempotency
    // design. Returns null only if the command doesn't exist.
    Task<AgentCommandDto?> UpdateStatusAsync(
        string tenantId,
        string siteId,
        string commandId,
        AgentCommandStatus status,
        string? result,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-079 - called from HealthMonitorService on every
    // processed heartbeat, best-effort (must never break notification
    // processing) - same calling convention as
    // AgentInstallationManagementService.NoteAgentHeartbeatAsync, right
    // next to it. Confirms completion for commands that can't self-report
    // (the process that would report Succeeded is gone by the time a
    // restart actually happens) by correlating a fresh heartbeat's
    // StartedUtc against the command's DispatchedUtc.
    Task EvaluateAgentCommandsAsync(
        AgentHeartbeatEntity agent,
        CancellationToken cancellationToken = default);
}
