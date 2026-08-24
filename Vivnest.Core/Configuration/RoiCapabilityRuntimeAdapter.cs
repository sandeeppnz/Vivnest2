using System.Globalization;
using System.Text.Json.Nodes;

namespace Vivnest.Core.Configuration;

// The shared shape of a capability that classifies a region of interest in
// a camera frame: read four ROI integers out of the capability entry's
// Settings, and write a nested DeviceOptions sub-object
// {Enabled, RoiLeft, RoiTop, RoiRight, RoiBottom, ExecutingAgentId}.
//
// Why this exists (ADR-108). ObjectDetectionRuntimeAdapter and
// SinkCleanlinessRuntimeAdapter were 307 tokens each and differed in five
// lines: the class name, CapabilityName, and three strings inside log and
// JSON keys. They were not two capabilities that resembled each other -
// they were one concept built twice, and a third ROI capability would have
// been a third copy.
//
// ExecutingAgentId comes straight from the capability entry and is already
// the resolved RuntimeAgentId by the time it reaches here (the Cloud-side
// projector resolved it), which is exactly what SinkCleanlinessHandler and
// the classify-request queue message expect. No ModelPath /
// ConfidenceThreshold / ExpectedClasses here at all: those live entirely
// on the executing agent's own blob, read via AiClassificationOptions
// binding with no adapter (ADR-064).
//
// Mirrors ImageCaptureRuntimeAdapter's contract (ADR-066) but writes a
// nested sub-object rather than root fields.
public abstract class RoiCapabilityRuntimeAdapter : ICapabilityConfigRuntimeAdapter
{
    // The catalogue's display name, matched case- and whitespace-
    // insensitively by CapabilityConfigRuntimeAdapterLookup. Also the name
    // used in the startup diagnostics below, so an operator reads
    // "Sink Cleanliness ROI values incomplete", not a class name.
    public abstract string CapabilityName { get; }

    // The DeviceOptions sub-object this writes into - "ObjectDetection",
    // "SinkCleanliness". Deliberately separate from CapabilityName: one is
    // a human label that Admin can edit, the other is a binding key that
    // DeviceOptions depends on and must not move.
    protected abstract string DeviceOptionsKey { get; }

    public void Apply(JsonObject flattenedDevice, JsonObject capabilityEntry)
    {
        if (capabilityEntry["Settings"] is not JsonObject settings)
            return;

        var deviceId = flattenedDevice["DeviceId"]?.GetValue<string>() ?? "(unknown)";

        var roiLeftOk = TryGetInt(settings, "RoiLeft", deviceId, out var roiLeft);
        var roiTopOk = TryGetInt(settings, "RoiTop", deviceId, out var roiTop);
        var roiRightOk = TryGetInt(settings, "RoiRight", deviceId, out var roiRight);
        var roiBottomOk = TryGetInt(settings, "RoiBottom", deviceId, out var roiBottom);

        // All four or none: a half-configured ROI is a rectangle with a
        // missing edge, which would silently classify the wrong region.
        if (!roiLeftOk || !roiTopOk || !roiRightOk || !roiBottomOk)
        {
            Console.WriteLine(
                $"[Startup] Device {deviceId}: {CapabilityName} ROI values incomplete/invalid, {DeviceOptionsKey} left unconfigured.");
            return;
        }

        var enabled = capabilityEntry["Enabled"]?.GetValue<bool>() ?? false;
        var executingAgentId = capabilityEntry["ExecutingAgentId"]?.GetValue<string>() ?? "";

        flattenedDevice[DeviceOptionsKey] = new JsonObject
        {
            ["Enabled"] = enabled,
            ["RoiLeft"] = roiLeft,
            ["RoiTop"] = roiTop,
            ["RoiRight"] = roiRight,
            ["RoiBottom"] = roiBottom,
            ["ExecutingAgentId"] = executingAgentId
        };
    }

    private bool TryGetInt(JsonObject settings, string key, string deviceId, out int value)
    {
        value = default;

        var raw = settings[key]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Invariant for consistency with the double parses in the other
        // adapters (see ImageCaptureRuntimeAdapter). Integer parsing is
        // less exposed - digits are ASCII everywhere - but a wire value
        // should not consult the host locale at all.
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        Console.WriteLine(
            $"[Startup] Device {deviceId}: {CapabilityName} \"{key}\" value \"{raw}\" is not a whole number, ignored.");

        return false;
    }
}
