namespace Vivnest.Abstractions.Models.Api;

// Shape for the dashboard's Capabilities tab (decision-log.md ADR-040) -
// assembled by IDeviceCapabilitiesQueryService from device-config/
// agent-config blobs, not stored anywhere as-is.
public sealed record DeviceCapabilitiesDto(
    IReadOnlyList<CapabilityDto> Capabilities,
    IReadOnlyList<TriggeredByDto> TriggeredBy,
    IReadOnlyList<SourceSensorDto> SourceSensors);

// A capability (e.g. "Image Classification") is a fixed, canonical concept
// distinct from whatever device/service actually provides it for a given
// device - see decision-log.md ADR-041. Services is a list, not a single
// field, because a capability can in principle be achieved by more than one
// device/service; every capability in this codebase happens to have exactly
// one today, but the shape doesn't assume that.
public sealed record CapabilityDto(
    string Name,
    string Source,
    IReadOnlyList<CapabilityServiceDto> Services);

// Wide/sparse record (many nullable fields, not all populated per service)
// rather than several strongly-typed variants - matches this project's
// existing flat-record DTO style, avoids inventing a discriminated-union
// JSON convention it doesn't already have. ModelPath/ConfidenceThreshold
// (only populated for AI-derived services) replace the old standalone
// DerivedFromDto - that was just this same information in a second list.
public sealed record CapabilityServiceDto(
    string Name,
    bool Enabled,
    string? ExecutingAgentId,
    int? RoiLeft,
    int? RoiTop,
    int? RoiRight,
    int? RoiBottom,
    string? Host,
    string? Username,
    string? ModelPath,
    double? ConfidenceThreshold,
    string? LivenessInterval,
    double? WarningMultiplier,
    // Decision-log.md ADR-078 - Running/NotRunning/Unknown, stored as a
    // string like every other status field on this DTO family
    // (AgentSummaryDto.Status etc.), not the raw enum - see ADR-076's
    // JsonConverter gotcha for why that convention exists.
    string OperationalStatus);

public sealed record TriggeredByDto(
    string DeviceId,
    string DeviceName,
    string DeviceType);

public sealed record SourceSensorDto(
    string Name,
    bool Accessible,
    string? InaccessibleReason,
    int UsedByCount);
