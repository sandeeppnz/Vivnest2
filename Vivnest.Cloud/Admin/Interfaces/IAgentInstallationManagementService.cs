using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin.Interfaces;

public interface IAgentInstallationManagementService
{
    Task<IReadOnlyList<AgentInstallationDto>> GetByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationDto>> GetByMachineAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);

    Task<AgentInstallationDto?> GetActiveByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationDto>> GetActiveByMachineAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);

    // Returns null if AgentId doesn't exist, MachineId doesn't exist, or
    // the Agent already has an active installation (must Move or
    // Uninstall first - see decision-log.md ADR-053's "at most one active
    // installation per Agent" invariant). Decision-log.md ADR-071 - the
    // new installation always starts Pending, with a one-time install
    // token issued alongside it (never retrievable again after this
    // call) for whoever provisions the target Machine to register with.
    Task<AgentInstallationCreationResult?> InstallAsync(
        TenantContext tenant,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        CancellationToken cancellationToken = default);

    // Retires the Agent's current active installation (if any) and
    // creates a new one on the given Machine - the old installation is
    // never mutated to point at the new Machine, preserving history.
    // Returns null if AgentId or the new MachineId doesn't exist. Same
    // Pending + one-time install token as InstallAsync - a Move targets a
    // genuinely different Machine (e.g. hardware replacement), which
    // needs its own registration just like a first-ever install.
    Task<AgentInstallationCreationResult?> MoveAsync(
        TenantContext tenant,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        CancellationToken cancellationToken = default);

    // Returns null if the Agent has no active installation.
    Task<AgentInstallationDto?> UninstallAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-072 - deliberately no TenantContext parameter,
    // unlike every other method here: the caller (Vivnest.Agent.Updater,
    // before it has any identity Cloud recognizes) has no tenant x-api-key
    // to present. The install token itself resolves tenant/site/
    // installation - see RegisterInstallation's own comment for the full
    // trust model. Returns null for any invalid token (unknown, expired,
    // already used) or if the installation it names is no longer Pending -
    // the caller doesn't need to distinguish why, only that registration
    // didn't happen.
    Task<AgentRegistrationResult?> RegisterAsync(
        string installToken,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-072 - same "no TenantContext" reasoning as
    // RegisterAsync; tenantId/siteId come from the registration response
    // the Updater is relaying back, not from a tenant key. Best-effort by
    // design (see AgentInstallationsFunction.ReportDeployComplete) - a
    // missed call just means the installation catches up to Active on the
    // next real heartbeat instead (HealthMonitorService's own hook covers
    // Installing/Installed/Updating identically). Returns false if the
    // installation doesn't exist.
    Task<bool> ReportDeployCompleteAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-072 - called from HealthMonitorService on every
    // processed heartbeat, best-effort (must never break notification
    // processing). No-ops silently whenever there's nothing to do: no
    // Agent linked to this RuntimeAgentId yet, no installation, or the
    // installation is already Active/Pending/Decommissioned.
    Task NoteAgentHeartbeatAsync(
        string tenantId,
        string siteId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-073 - the same RuntimeAgentId -> admin Agent ->
    // active installation resolution RegisterAsync already does
    // internally, exposed for AgentsFunction.DeployAgent (whose own
    // {agentId} route parameter is a RuntimeAgentId, not the admin AgentId
    // AgentInstallation is actually keyed by - see ADR-063's identity
    // split). Null covers "no Agent found," "no active installation," and
    // "installation has no ImageVersion set" identically - the caller
    // falls back to :latest in every case, same as today.
    Task<string?> GetActiveImageVersionByRuntimeAgentIdAsync(
        TenantContext tenant,
        string runtimeAgentId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-076 - derived live from the Machine's active
    // installations' own Agent health (IAgentStatusResolver), never a
    // stored field. Unknown when nothing's installed; Online/Offline only
    // when every installed Agent agrees; Warning for any real mix - see
    // the implementation's own comment for the full aggregation rule.
    Task<DeviceHeartbeatStatus> GetMachineOperationalStatusAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);
}
