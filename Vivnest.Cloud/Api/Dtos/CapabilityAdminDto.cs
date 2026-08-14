namespace Vivnest.Cloud.Api.Dtos;

// Admin > Capabilities master-list record (decision-log.md ADR-042) -
// deliberately named to avoid any collision with the existing, unrelated
// CapabilityDto/CapabilityServiceDto used by the per-device Capabilities
// tab (ADR-040/041). CapabilityType travels as a string (Device/Service/
// System, renamed from BuiltIn/Derived/System - decision-log.md ADR-061),
// matching this codebase's existing enum-on-the-wire convention.
public sealed record CapabilityAdminDto(
    Guid CapabilityId,
    string CapabilityName,
    string CapabilityType);

public sealed record CreateCapabilityRequest(
    string CapabilityName,
    string CapabilityType);

public sealed record UpdateCapabilityRequest(
    string CapabilityName,
    string CapabilityType);
