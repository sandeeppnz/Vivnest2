using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Models.Api;

// Admin > Machines record (decision-log.md ADR-053) - the physical/virtual
// host a Vivnest Agent runs on. TenantId/SiteId included for display
// parity with other admin DTOs, but never accepted on create/update - the
// service fills them from the authenticated TenantContext. MachineId is a
// generated Guid (same convention as AgentRegistryDto.AgentId) - not
// accepted on create, only ever server-assigned.
// OperationalStatus (decision-log.md ADR-076) is attached at the Function
// layer via a `with { ... }` expression, same pattern ADR-073 used for
// AgentInstallationDto.VersionStatus - Status above is the Admin lifecycle
// field (Active/Offline/Retired/Decommissioned, hand-set), OperationalStatus
// is derived live from the Machine's installed Agents' own health, and the
// two are never the same field.
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
    string SiteId,
    DeviceHeartbeatStatus OperationalStatus = DeviceHeartbeatStatus.Unknown);

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
