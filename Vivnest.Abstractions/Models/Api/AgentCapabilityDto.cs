namespace Vivnest.Abstractions.Models.Api;

// Admin > Agent Capability declaration record (decision-log.md ADR-059) -
// "this Agent has the ability to execute this Capability," independent of
// any device. A lifecycle record, not plain CRUD - Assign/Unassign are
// POST actions, same shape DeviceCapabilityDto/AssignCapabilityRequest
// already established.
public sealed record AgentCapabilityDto(
    string AgentCapabilityId,
    string AgentId,
    string CapabilityId,
    string Status,
    DateTime AssignedUtc,
    DateTime? RemovedUtc,
    DateTime UpdatedUtc,
    string TenantId,
    string SiteId);

public sealed record AssignAgentCapabilityRequest(
    string AgentId,
    string CapabilityId);

public sealed record UnassignAgentCapabilityRequest(
    string AgentId,
    string CapabilityId);
