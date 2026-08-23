using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentCapabilityStore
{
    Task<AgentCapabilityEntity?> GetAsync(
        string tenantId,
        string siteId,
        string agentCapabilityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentCapabilityEntity>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);

    // At most one active declaration should ever exist per (Agent,
    // Capability) pair - enforced by AgentCapabilityAssignmentService.AssignAsync,
    // not by any table-level constraint, same reasoning
    // DeviceCapabilityEntity's own store uses.
    Task<AgentCapabilityEntity?> GetActiveByAgentAndCapabilityAsync(
        string tenantId,
        string siteId,
        string agentId,
        string capabilityId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        AgentCapabilityEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AgentCapabilityEntity entity,
        CancellationToken cancellationToken = default);
}
