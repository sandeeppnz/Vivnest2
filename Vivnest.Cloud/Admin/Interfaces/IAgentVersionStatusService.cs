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
}
