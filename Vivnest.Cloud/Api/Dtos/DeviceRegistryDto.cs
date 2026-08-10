namespace Vivnest.Cloud.Api.Dtos;

// Admin > Devices pre-registration record (decision-log.md ADR-048) -
// deliberately separate from DeviceSummaryDto, which reflects real, live
// heartbeat data. Registering a device here just reserves its identity and
// declares planned facts about it - it does not configure a real device;
// the device-config blob workflow (ADR-036/037/038) is unchanged.
public sealed record DeviceRegistryDto(
    Guid DeviceId,
    string Name,
    string DeviceTypeId,
    string OwningAgentId,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    bool Enabled,
    IReadOnlyList<Guid> CapabilityIds,
    IReadOnlyDictionary<string, string> Settings,
    string TenantId,
    string SiteId);

public sealed record CreateDeviceRegistryRequest(
    string Name,
    string DeviceTypeId,
    string OwningAgentId,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    bool Enabled,
    IReadOnlyList<Guid>? CapabilityIds = null,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record UpdateDeviceRegistryRequest(
    string Name,
    string DeviceTypeId,
    string OwningAgentId,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    bool Enabled,
    IReadOnlyList<Guid>? CapabilityIds = null,
    IReadOnlyDictionary<string, string>? Settings = null);
