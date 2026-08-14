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
//
// No longer carries CapabilityIds (ADR-059) - which capabilities an
// Agent declares now lives in AgentCapabilityDto, a real per-declaration
// record instead of a flat id list here (same move ADR-057 made for
// Device.CapabilityIds -> DeviceCapabilityDto).
// RuntimeAgentId (ADR-063) - the explicit, admin-typed link to the real
// Vivnest.Agent process's own appsettings.json "Agent:AgentId" value.
// Empty means not linked yet - same identity-space gap ADR-058 documented
// for Device, confirmed to also exist for Agent.
public sealed record AgentRegistryDto(
    Guid AgentId,
    string Name,
    string? Description,
    string Status,
    string FirmwareVersion,
    string Type,
    string RuntimeAgentId,
    string TenantId,
    string SiteId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateAgentRegistryRequest(
    string Name,
    string? Description,
    string FirmwareVersion,
    string Type,
    string? RuntimeAgentId = null);

public sealed record UpdateAgentRegistryRequest(
    string Name,
    string? Description,
    string Status,
    string FirmwareVersion,
    string Type,
    string? RuntimeAgentId = null);
