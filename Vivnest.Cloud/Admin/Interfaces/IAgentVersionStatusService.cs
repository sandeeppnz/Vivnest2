using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Decision-log.md ADR-073 - computes Desired/Running software-version
// status for a single Agent. desiredVersion is passed in (the caller
// already has it off the AgentInstallation it's building a response for)
// rather than re-fetched here, same "don't re-derive what the caller
// already has" convention ConfigurationSyncStatusService follows for its
// own "Desired."
public interface IAgentVersionStatusService
{
    Task<AgentVersionStatusDto> GetStatusAsync(
        TenantContext tenant,
        string agentId,
        string? desiredVersion,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-075 (Phase 8 Pass 2) - for a caller that's
    // already iterating AgentHeartbeatEntity rows (keyed by RuntimeAgentId,
    // not the admin AgentId GetStatusAsync above expects) and already has
    // the running version in hand (FirmwareVersion off that same row) -
    // avoids a redundant re-fetch of the heartbeat this method's caller
    // already read. Only the desired version (AgentInstallation.ImageVersion)
    // still needs resolving, via the existing RuntimeAgentId reverse lookup
    // (AgentInstallationManagementService.GetActiveImageVersionByRuntimeAgentIdAsync).
    Task<AgentVersionStatusDto> GetStatusForRuntimeAgentAsync(
        TenantContext tenant,
        string runtimeAgentId,
        string? runningVersion,
        CancellationToken cancellationToken = default);
}
