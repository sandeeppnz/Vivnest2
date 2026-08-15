namespace Vivnest.Cloud.Api.Dtos;

// Read-only preview of what the Admin domain WOULD produce for this
// Device's runtime device-config/*.json shape (decision-log.md ADR-063,
// extended ADR-064) - nothing writes anywhere, this is purely for a human
// to eyeball-diff against the real file (or, once published, what was
// actually written). Identity + connection Settings are always projected;
// Capabilities is populated by the shared ICapabilityRuntimeProjector
// registry - a capability with no registered projector is simply absent
// here, with a Warnings entry explaining why. LivenessInterval/Schedule/
// Trigger/Sensors are still not projected (ADR-063's original scope).
public sealed record DeviceRuntimeConfigurationDocumentDto(
    string? DeviceId,
    string Name,
    string? Type,
    bool Enabled,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    string? OwningAgentId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<CapabilityDocumentEntryDto> Capabilities,
    IReadOnlyList<string> Warnings,
    // Populated by the Function handler via a `with` expression after
    // projection (decision-log.md ADR-068) - computing it needs a blob
    // download + heartbeat lookup neither projector does, so it's never
    // set at construction time. Null only for callers that never attach
    // it (there are none left after ADR-068 - GetDeviceProjectedConfig
    // always does).
    ConfigurationSyncStatusDto? SyncStatus = null);

// One assigned DeviceCapability's device-local contribution (ADR-064) -
// produced by an ICapabilityRuntimeProjector's DeviceEntry, not a raw
// DeviceCapability.Settings passthrough.
public sealed record CapabilityDocumentEntryDto(
    string CapabilityId,
    string Name,
    bool Enabled,
    string? ExecutingAgentId,
    IReadOnlyDictionary<string, string> Settings);
