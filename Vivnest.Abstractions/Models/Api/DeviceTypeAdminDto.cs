namespace Vivnest.Abstractions.Models.Api;

// Admin > Device Types master-list record (decision-log.md ADR-047/057) -
// deliberately unrelated to Vivnest.Core.Enums.DeviceType, the fixed enum
// real Agent code branches on. Same split as CapabilityAdminDto vs.
// CapabilityDto/CapabilityServiceDto (ADR-040/041/042). Field kept named
// DeviceTypeName (not renamed to Name) - the dashboard already consumes
// this shape and there's no reason to break it just for symmetry with the
// new domain class's property name.
public sealed record DeviceTypeAdminDto(
    Guid DeviceTypeId,
    string DeviceTypeName,
    string? Description,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateDeviceTypeRequest(
    string DeviceTypeName,
    string? Description = null);

public sealed record UpdateDeviceTypeRequest(
    string DeviceTypeName,
    string? Description,
    string Status);
