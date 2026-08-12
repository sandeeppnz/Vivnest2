namespace Vivnest.Cloud.Api.Dtos;

// Admin > Machines record (decision-log.md ADR-053) - the physical/virtual
// host a Vivnest Agent runs on. TenantId/SiteId included for display
// parity with other admin DTOs, but never accepted on create/update - the
// service fills them from the authenticated TenantContext. MachineId is a
// generated Guid (same convention as AgentRegistryDto.AgentId) - not
// accepted on create, only ever server-assigned.
public sealed record MachineDto(
    Guid MachineId,
    string Name,
    string? Hostname,
    string? Description,
    string Status,
    string? OperatingSystem,
    string? Architecture,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string TenantId,
    string SiteId);

public sealed record CreateMachineRequest(
    string Name,
    string? Hostname,
    string? Description,
    string? OperatingSystem,
    string? Architecture);

public sealed record UpdateMachineRequest(
    string Name,
    string? Hostname,
    string? Description,
    string Status,
    string? OperatingSystem,
    string? Architecture);
