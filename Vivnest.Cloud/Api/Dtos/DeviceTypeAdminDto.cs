namespace Vivnest.Cloud.Api.Dtos;

// Admin > Device Types master-list record (decision-log.md ADR-047) -
// deliberately unrelated to Vivnest.Core.Enums.DeviceType, the fixed enum
// real Agent code branches on. Same split as CapabilityAdminDto vs.
// CapabilityDto/CapabilityServiceDto (ADR-040/041/042).
public sealed record DeviceTypeAdminDto(
    Guid DeviceTypeId,
    string DeviceTypeName);

public sealed record CreateDeviceTypeRequest(
    string DeviceTypeName);

public sealed record UpdateDeviceTypeRequest(
    string DeviceTypeName);
