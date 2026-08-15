using System.Text.Json.Nodes;

namespace Vivnest.Agent.Runtime.Configuration;

// Translates the new capabilities[]-shaped device-config document
// (decision-log.md ADR-064, extended ADR-065) into the legacy flat
// DeviceOptions shape Program.cs's TryLoadRemoteDeviceConfigsAsync
// already assembles into configuration - the seam that lets Admin
// publish a different document shape without any other Agent code
// (workers, DeviceOptions itself) needing to change. Detects shape by
// the presence of a top-level "Capabilities" key (only the new shape's
// IDeviceRuntimeConfigurationPublisher writes one) - a legacy-shape
// object is returned completely unchanged, the dual-shape guarantee
// behind "the MVP runtime must keep working throughout" during ADR-064's
// migration.
public static class DeviceConfigRuntimeAdapter
{
    // No DI here - this runs in Program.cs's config-loading phase, before
    // the host is built (no IServiceProvider exists yet to resolve
    // IEnumerable<ICapabilityConfigRuntimeAdapter> from). A hardcoded list
    // is fine for the handful of implementations this will ever have, same
    // reasoning the Cloud-side registry uses for not introducing a formal
    // registry type.
    private static readonly IReadOnlyList<ICapabilityConfigRuntimeAdapter> DefaultCapabilityAdapters =
    [
        new ImageCaptureRuntimeAdapter()
    ];

    public static JsonObject Adapt(JsonObject deviceObject) =>
        Adapt(deviceObject, DefaultCapabilityAdapters);

    public static JsonObject Adapt(
        JsonObject deviceObject, IReadOnlyList<ICapabilityConfigRuntimeAdapter> capabilityAdapters)
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

        // Phase 6C / decision-log.md ADR-065 - captured for
        // DeviceHeartbeat.ConfigurationPublishedUtc reporting. Absent on
        // any device never published through this pipeline, which is
        // itself informative.
        var publishedUtc = deviceObject["PublishedUtc"]?.DeepClone();

        if (publishedUtc != null)
            flattened["ConfigurationPublishedUtc"] = publishedUtc;

        // Per-capability-type translation (decision-log.md ADR-065) -
        // mirrors the Cloud-side ICapabilityRuntimeProjector registry
        // exactly. A capability with no registered adapter is skipped
        // with a console warning rather than guessed at - the publisher's
        // own capability-coverage gate already prevents this in practice
        // (a device with an unregistered capability can't be published),
        // but the Agent still validates independently, never trusting a
        // blob just because Admin generated it.
        foreach (var entry in capabilities)
        {
            if (entry is not JsonObject capabilityEntry)
                continue;

            var name = capabilityEntry["Name"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var adapter = CapabilityConfigRuntimeAdapterLookup.Find(capabilityAdapters, name);

            if (adapter == null)
            {
                Console.WriteLine(
                    $"[Startup] No runtime adapter registered for capability \"{name}\" on device {flattened["DeviceId"]}; ignored.");
                continue;
            }

            adapter.Apply(flattened, capabilityEntry);
        }

        return flattened;
    }
}
