using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

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

    // Decision-log.md ADR-072 - a focused update for the one field the
    // registration flow actually needs to set, rather than routing through
    // UpdateAsync's full field list (which would require the registration
    // endpoint to already know the Agent's Name/Description/FirmwareVersion/
    // Type just to leave them unchanged). Returns null if AgentId doesn't
    // exist.
    Task<AgentRegistryDto?> SetRuntimeAgentIdAsync(
        string tenantId,
        string siteId,
        string agentId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default);
}
