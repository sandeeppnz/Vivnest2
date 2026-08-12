namespace Vivnest.Cloud.Api.Dtos;

// Admin > Device Capability assignment record (decision-log.md ADR-057) -
// the join between Device and Capability, carrying ExecutingAgentId
// (which Agent runs this capability for this device - may differ from
// the Device's own OwningAgentId, see DeviceCapability domain class's own
// comment). A lifecycle record, not a plain CRUD resource - Assign/
// Unassign are POST actions, same shape AgentInstallationDto/
// InstallAgentRequest/UninstallAgentRequest already established.
public sealed record DeviceCapabilityDto(
    string DeviceCapabilityId,
    string DeviceId,
    string CapabilityId,
    string ExecutingAgentId,
    bool Enabled,
    IReadOnlyDictionary<string, string> Settings,
    string Status,
    DateTime AssignedUtc,
    DateTime? RemovedUtc,
    DateTime UpdatedUtc,
    string TenantId,
    string SiteId);

public sealed record AssignCapabilityRequest(
    string DeviceId,
    string CapabilityId,
    string? ExecutingAgentId,
    bool Enabled,
    IReadOnlyDictionary<string, string>? Settings);

public sealed record UpdateCapabilityAssignmentRequest(
    string? ExecutingAgentId,
    bool Enabled,
    IReadOnlyDictionary<string, string>? Settings);

public sealed record UnassignCapabilityRequest(
    string DeviceId,
    string CapabilityId);
