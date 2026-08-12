namespace Vivnest.Cloud.Api.Dtos;

// Admin > Devices pre-registration record (decision-log.md ADR-048/057) -
// deliberately separate from DeviceSummaryDto, which reflects real, live
// heartbeat data. Registering a device here just reserves its identity and
// declares planned facts about it - it does not configure a real device;
// the device-config blob workflow (ADR-036/037/038) is unchanged.
//
// No longer carries CapabilityIds - which capabilities a device has, and
// who executes each one, now lives in DeviceCapabilityDto (ADR-057), a
// real per-assignment record instead of a flat id list here.
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
    IReadOnlyDictionary<string, string>? Settings = null);
