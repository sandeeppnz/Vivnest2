namespace Vivnest.Cloud.Api.Dtos;

// Admin > Agents pre-registration record (decision-log.md ADR-043) -
// deliberately separate from AgentSummaryDto, which reflects real,
// live heartbeat data. TenantId/SiteId are included here for display
// parity with AgentSummaryDto, but are never accepted on create/update -
// the service fills them from the authenticated TenantContext. This is
// also the "Agent" concept from the Machine/Agent/AgentInstallation spec
// (ADR-053) - Description/Status/CreatedUtc/UpdatedUtc added directly
// here rather than a parallel DTO. No CurrentMachineId/CurrentInstallationId
// - see AgentRegistryEntity's comment for why.
public sealed record AgentRegistryDto(
    Guid AgentId,
    string Name,
    string? Description,
    string Status,
    string FirmwareVersion,
    string Type,
    string TenantId,
    string SiteId,
    IReadOnlyList<Guid> CapabilityIds,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateAgentRegistryRequest(
    string Name,
    string? Description,
    string FirmwareVersion,
    string Type,
    IReadOnlyList<Guid>? CapabilityIds = null);

public sealed record UpdateAgentRegistryRequest(
    string Name,
    string? Description,
    string Status,
    string FirmwareVersion,
    string Type,
    IReadOnlyList<Guid>? CapabilityIds = null);
