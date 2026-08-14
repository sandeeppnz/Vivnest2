using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

public interface IAgentRegistryManagementService
{
    Task<IReadOnlyList<AgentRegistryDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    Task<AgentRegistryDto> CreateAsync(
        TenantContext tenant,
        string name,
        string? description,
        string firmwareVersion,
        string type,
        string? runtimeAgentId,
        CancellationToken cancellationToken = default);

    Task<AgentRegistryDto?> UpdateAsync(
        TenantContext tenant,
        string agentId,
        string name,
        string? description,
        string status,
        string firmwareVersion,
        string type,
        string? runtimeAgentId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);
}
