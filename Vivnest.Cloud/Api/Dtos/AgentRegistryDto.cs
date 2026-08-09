namespace Vivnest.Cloud.Api.Dtos;

// Admin > Agents pre-registration record (decision-log.md ADR-043) -
// deliberately separate from AgentSummaryDto, which reflects real,
// live heartbeat data. TenantId/SiteId are included here for display
// parity with AgentSummaryDto, but are never accepted on create/update -
// the service fills them from the authenticated TenantContext.
public sealed record AgentRegistryDto(
    Guid AgentId,
    string Name,
    string FirmwareVersion,
    string Type,
    string TenantId,
    string SiteId);

public sealed record CreateAgentRegistryRequest(
    string Name,
    string FirmwareVersion,
    string Type);

public sealed record UpdateAgentRegistryRequest(
    string Name,
    string FirmwareVersion,
    string Type);
