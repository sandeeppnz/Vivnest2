using System.Text.Json.Nodes;

namespace Vivnest.Agent.Runtime.Configuration;

// Translates the new capabilities[]-shaped device-config document
// (decision-log.md ADR-064) into the legacy flat DeviceOptions shape
// Program.cs's TryLoadRemoteDeviceConfigsAsync already assembles into
// configuration - the seam that lets Admin publish a different document
// shape without any other Agent code (workers, DeviceOptions itself)
// needing to change. Detects shape by the presence of a top-level
// "Capabilities" key (only the new shape's IDeviceRuntimeConfigurationPublisher
// writes one) - a legacy-shape object is returned completely unchanged,
// the dual-shape guarantee behind "the MVP runtime must keep working
// throughout" during ADR-064's migration.
public static class DeviceConfigRuntimeAdapter
{
    public static JsonObject Adapt(JsonObject deviceObject)
    {
        if (deviceObject["Capabilities"] is not JsonArray capabilities)
            return deviceObject;

        var device = deviceObject["Device"] as JsonObject;

        var flattened = new JsonObject
        {
            ["DeviceId"] = deviceObject["RuntimeDeviceId"]?.DeepClone(),
            ["Name"] = device?["Name"]?.DeepClone(),
            ["Type"] = device?["Type"]?.DeepClone(),
            ["Enabled"] = device?["Enabled"]?.DeepClone(),
            ["Location"] = device?["Location"]?.DeepClone(),
            ["Brand"] = device?["Brand"]?.DeepClone(),
            ["Model"] = device?["Model"]?.DeepClone(),
            ["Firmware"] = device?["Firmware"]?.DeepClone(),
            ["OwningAgentId"] = deviceObject["OwningAgentId"]?.DeepClone(),
            ["Settings"] = device?["Connection"]?.DeepClone() ?? new JsonObject()
        };

        // Capability translation (ADR-064 migration step 2 onward) - no
        // per-capability-type adapters exist yet, so Capabilities is
        // parsed for visibility but not yet wired into SinkCleanliness/
        // ObjectDetection/Schedule/Trigger. Every remaining DeviceOptions
        // field this adapter doesn't set (LivenessInterval, Schedule,
        // Trigger, Sensors, ParentDeviceId, SinkCleanliness,
        // ObjectDetection) simply keeps its C# default - safe, since the
        // publisher only ever marks a device publishable once every
        // assigned capability has a registered projector (none do yet).
        // A future per-capability-type adapter registry, mirrored with
        // the Cloud-side ICapabilityRuntimeProjector registry, plugs in
        // here without reshaping the document again.
        if (capabilities.Count > 0)
        {
            Console.WriteLine(
                $"[Startup] Device config declares {capabilities.Count} capabilit{(capabilities.Count == 1 ? "y" : "ies")} not yet translated by the Runtime Adapter; ignored.");
        }

        return flattened;
    }
}
