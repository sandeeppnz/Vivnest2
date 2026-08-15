using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// Projects one DeviceCapability assignment into its runtime contribution(s)
// (decision-log.md ADR-064) - dispatched by Capability.Name (free-text
// admin-typed master data, matched case/whitespace-insensitively via
// CapabilityRuntimeProjectorLookup, same convention
// DeviceRuntimeConfigurationProjector.MatchRuntimeDeviceType already uses
// for DeviceType). Deliberately not a single generic
// "foreach DeviceCapability, serialize Settings" loop - different
// capabilities affect different runtime configuration locations (a
// scheduling-only capability is device-local only; ObjectDetection/
// SinkCleanliness need part of their settings on the device's own entry -
// ROI - and part on the executing agent's own document - model params).
// A DI-resolved IEnumerable<ICapabilityRuntimeProjector> is the registry -
// no separate registry type exists while there are only ever a handful of
// implementations to enumerate.
public interface ICapabilityRuntimeProjector
{
    string CapabilityName { get; }

    CapabilityProjectionResult Project(
        DeviceCapabilityEntity assignment,
        DeviceRegistryEntity device,
        string? executingRuntimeAgentId);
}

// DeviceEntry -> this device's own capabilities[] entry (Device
// Configuration Projection). AgentEntry -> a contribution destined for the
// executing agent's own AiClassification document (Agent Configuration
// Projection) - null for capabilities that are entirely device-local. A
// capability projector may populate either, both, or neither. Warnings
// from either half propagate up to the owning projection's own Warnings
// list and block a clean publish on that side.
public sealed record CapabilityProjectionResult(
    CapabilityDocumentEntryDto? DeviceEntry,
    AgentCapabilityContribution? AgentEntry,
    IReadOnlyList<string> Warnings);

// TargetRuntimeAgentId keys which agent's blob this contributes to (may
// differ from the Device's own OwningAgentId, and may differ between two
// capabilities on the same device - e.g. SinkCleanliness and
// ObjectDetection routing to different High-type agents, per
// ObjectDetectionRoiOptions/SinkCleanlinessRoiOptions's own doc comments).
// RuntimeDeviceId keys which AiDeviceClassification entry within that
// agent's Devices[] array. CapabilityName selects which sub-block
// ("ObjectDetection"/"SinkCleanliness") on that entry this contributes to.
public sealed record AgentCapabilityContribution(
    string TargetRuntimeAgentId,
    string RuntimeDeviceId,
    string CapabilityName,
    IReadOnlyDictionary<string, string> Settings);
