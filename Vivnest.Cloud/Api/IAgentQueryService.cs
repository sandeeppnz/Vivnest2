using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Api;

public interface IAgentQueryService
{
    Task<IReadOnlyList<AgentSummaryDto>> GetAgentsAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    Task<AgentSummaryDto?> GetAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);
}
