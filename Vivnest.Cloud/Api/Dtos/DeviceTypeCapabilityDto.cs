namespace Vivnest.Cloud.Api.Dtos;

// Admin > Capability/DeviceType compatibility (decision-log.md ADR-062,
// Phase 5) - "Camera supports ObjectDetection." Global, not tenant-scoped,
// same reasoning as CapabilityAdminDto.
public sealed record DeviceTypeCapabilityDto(
    Guid DeviceTypeCapabilityId,
    Guid DeviceTypeId,
    Guid CapabilityId);

public sealed record AddDeviceTypeCapabilityRequest(
    string DeviceTypeId,
    string CapabilityId);
