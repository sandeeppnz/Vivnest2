using System.Text.Json.Nodes;

namespace Vivnest.Abstractions.Configuration;

// Agent-side mirror of Vivnest.Cloud's ICapabilityRuntimeProjector
// (decision-log.md ADR-065 / Phase 6C) - one implementation per capability
// name, applying that capability's Settings onto the flattened device
// JsonObject before DeviceOptions binding. Different capabilities land in
// different places (a nested DeviceOptions sub-object for
// ObjectDetection/SinkCleanliness; the device's own root Schedule/
// LivenessInterval/WarningMultiplier fields for Image Capture) - that
// placement is each adapter's own business, not a shared contract concern.
// A DI-resolved IEnumerable<ICapabilityConfigRuntimeAdapter> is the
// registry, same "no separate registry type for a handful of
// implementations" reasoning the Cloud-side registry uses.
public interface ICapabilityConfigRuntimeAdapter
{
    string CapabilityName { get; }

    void Apply(JsonObject flattenedDevice, JsonObject capabilityEntry);
}
