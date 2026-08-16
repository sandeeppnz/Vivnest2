namespace Vivnest.Cloud.Api.Dtos;

// Read-only preview of what the Admin domain WOULD produce for this
// Agent's real agent-config/{agentId}.json "aiClassification" section
// (decision-log.md ADR-064) - deliberately the exact shape
// AiClassificationOptions.Devices[] already has, not a new document
// format, since Vivnest.Agent already understands this structure. Built
// by querying every DeviceCapability across the tenant/site whose
// ExecutingAgentId is this Agent, running each through the shared
// ICapabilityRuntimeProjector registry, and keeping only each result's
// AgentEntry. Reconstructed fresh every projection - a capability
// unassigned or reassigned to a different agent simply doesn't appear.
// HomeAssistant (the other real section on some agents' blobs) has no
// Admin equivalent and is never touched by this pipeline.
public sealed record AgentRuntimeConfigurationDocumentDto(
    string? AgentId,
    // decision-log.md ADR-087 - the Admin registry's own Name
    // (AgentRegistryEntity.Name), carried through so the publisher can
    // write it as a top-level sibling key without a second registry
    // lookup, and so this preview DTO shows what will actually be
    // published.
    string? Name,
    IReadOnlyList<AiDeviceClassificationEntryDto> Devices,
    IReadOnlyList<string> Warnings,
    // See DeviceRuntimeConfigurationDocumentDto.SyncStatus
    // (decision-log.md ADR-068) - same reasoning, attached after
    // projection by the Function handler.
    ConfigurationSyncStatusDto? SyncStatus = null);

// Mirrors the real AiDeviceClassification shape (Vivnest.Core.Options) -
// ObjectDetection/SinkCleanliness are each an opaque Settings dictionary
// here (Admin doesn't validate their contents; each key set is whatever
// the corresponding ICapabilityRuntimeProjector's AgentEntry produced),
// not the fully-typed ObjectDetectionModelOptions/SinkCleanlinessModelOptions
// - those types live in Vivnest.Agent, not Vivnest.Cloud.
public sealed record AiDeviceClassificationEntryDto(
    string DeviceId,
    IReadOnlyDictionary<string, string>? ObjectDetection,
    IReadOnlyDictionary<string, string>? SinkCleanliness);
