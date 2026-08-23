namespace Vivnest.Cloud.Api.Dtos;

// One field of CapabilityAdminDto.ConfigurationSchema (decision-log.md
// ADR-062, Phase 5) - mirrors Vivnest.Domain.Capabilities.CapabilityConfigurationField
// 1:1. Type travels as a string (String/Number/Boolean), same
// enum-on-the-wire convention as CapabilityAdminDto.CapabilityType.
public sealed record CapabilityConfigurationFieldDto(
    string Name,
    string Type,
    bool Required,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string>? AllowedValues,
    string? DefaultValue);
