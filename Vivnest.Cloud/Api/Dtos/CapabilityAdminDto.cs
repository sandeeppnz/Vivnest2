namespace Vivnest.Cloud.Api.Dtos;

// Admin > Capabilities master-list record (decision-log.md ADR-042) -
// deliberately named to avoid any collision with the existing, unrelated
// CapabilityDto/CapabilityServiceDto used by the per-device Capabilities
// tab (ADR-040/041). CapabilityType travels as a string (Device/Service/
// System, renamed from BuiltIn/Derived/System - decision-log.md ADR-061),
// matching this codebase's existing enum-on-the-wire convention.
// ConfigurationSchema/ConfigurationSchemaVersion/DefaultConfiguration/
// Status are additive (ADR-062, Phase 5) - what configuration this
// capability needs, what it defaults to, and whether it's still
// assignable.
public sealed record CapabilityAdminDto(
    Guid CapabilityId,
    string CapabilityName,
    string CapabilityType,
    string Status,
    IReadOnlyList<CapabilityConfigurationFieldDto> ConfigurationSchema,
    int ConfigurationSchemaVersion,
    IReadOnlyDictionary<string, string> DefaultConfiguration);

// Create always starts Active (same "no Status on create" convention
// DeviceRegistryDto's CreateDeviceRegistryRequest already established) -
// schema/defaults are optional, defaulting to "no configuration needed."
public sealed record CreateCapabilityRequest(
    string CapabilityName,
    string CapabilityType,
    IReadOnlyList<CapabilityConfigurationFieldDto>? ConfigurationSchema,
    int? ConfigurationSchemaVersion,
    IReadOnlyDictionary<string, string>? DefaultConfiguration);

public sealed record UpdateCapabilityRequest(
    string CapabilityName,
    string CapabilityType,
    string Status,
    IReadOnlyList<CapabilityConfigurationFieldDto>? ConfigurationSchema,
    int? ConfigurationSchemaVersion,
    IReadOnlyDictionary<string, string>? DefaultConfiguration);
