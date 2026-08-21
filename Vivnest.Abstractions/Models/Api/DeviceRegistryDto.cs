namespace Vivnest.Abstractions.Models.Api;

// Admin > Devices pre-registration record (decision-log.md ADR-048/057/058) -
// deliberately separate from DeviceSummaryDto, which reflects real, live
// heartbeat data. Registering a device here just reserves its identity and
// declares planned facts about it - it does not configure a real device;
// the device-config blob workflow (ADR-036/037/038) is unchanged.
//
// No longer carries CapabilityIds - which capabilities a device has, and
// who executes each one, now lives in DeviceCapabilityDto (ADR-057), a
// real per-assignment record instead of a flat id list here.
//
// Status replaces the old Enabled bool (ADR-058) - Active/Disabled/Retired,
// same convention as MachineDto.Status. Not accepted on create - a new
// Device always starts Active server-side, same as Machine.
//
// RuntimeDeviceId (ADR-063) - the explicit, admin-typed link to the real
// device-config/*.json blob's own DeviceId (ADR-058's "two unrelated
// identity spaces" gap). Empty means not linked yet.
public sealed record DeviceRegistryDto(
    Guid DeviceId,
    string Name,
    string DeviceTypeId,
    string OwningAgentId,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    string Status,
    string RuntimeDeviceId,
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
    string? RuntimeDeviceId = null,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed record UpdateDeviceRegistryRequest(
    string Name,
    string DeviceTypeId,
    string OwningAgentId,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    string Status,
    string? RuntimeDeviceId = null,
    IReadOnlyDictionary<string, string>? Settings = null);
