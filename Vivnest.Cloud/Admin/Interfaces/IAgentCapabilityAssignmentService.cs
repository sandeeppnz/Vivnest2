using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Owns the AgentCapability lifecycle (decision-log.md ADR-059) - Assign/
// Unassign, not plain CRUD, same reasoning ICapabilityAssignmentService
// documents for DeviceCapability.
public interface IAgentCapabilityAssignmentService
{
    Task<IReadOnlyList<AgentCapabilityDto>> ListByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    // Returns null if AgentId or CapabilityId doesn't exist, or this
    // (Agent, Capability) pair already has an active declaration - use
    // UnassignAsync first to replace one.
    Task<AgentCapabilityDto?> AssignAsync(
        TenantContext tenant,
        string agentId,
        string capabilityId,
        CancellationToken cancellationToken = default);

    // Returns null if this (Agent, Capability) pair has no active
    // declaration.
    Task<AgentCapabilityDto?> UnassignAsync(
        TenantContext tenant,
        string agentId,
        string capabilityId,
        CancellationToken cancellationToken = default);
}
