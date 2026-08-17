using System.Text.Json.Nodes;

namespace Vivnest.Core.Configuration;

// Mirrors ObjectDetectionRuntimeAdapter exactly (decision-log.md
// ADR-066) - same nested-sub-object placement, matching the real
// SinkCleanlinessRoiOptions shape.
public sealed class SinkCleanlinessRuntimeAdapter : ICapabilityConfigRuntimeAdapter
{
    public string CapabilityName => "Sink Cleanliness";

    public void Apply(JsonObject flattenedDevice, JsonObject capabilityEntry)
    {
        if (capabilityEntry["Settings"] is not JsonObject settings)
            return;

        var deviceId = flattenedDevice["DeviceId"]?.GetValue<string>() ?? "(unknown)";

        var roiLeftOk = TryGetInt(settings, "RoiLeft", deviceId, out var roiLeft);
        var roiTopOk = TryGetInt(settings, "RoiTop", deviceId, out var roiTop);
        var roiRightOk = TryGetInt(settings, "RoiRight", deviceId, out var roiRight);
        var roiBottomOk = TryGetInt(settings, "RoiBottom", deviceId, out var roiBottom);

        if (!roiLeftOk || !roiTopOk || !roiRightOk || !roiBottomOk)
        {
            Console.WriteLine(
                $"[Startup] Device {deviceId}: Sink Cleanliness ROI values incomplete/invalid, SinkCleanliness left unconfigured.");
            return;
        }

        var enabled = capabilityEntry["Enabled"]?.GetValue<bool>() ?? false;
        var executingAgentId = capabilityEntry["ExecutingAgentId"]?.GetValue<string>() ?? "";

        flattenedDevice["SinkCleanliness"] = new JsonObject
        {
            ["Enabled"] = enabled,
            ["RoiLeft"] = roiLeft,
            ["RoiTop"] = roiTop,
            ["RoiRight"] = roiRight,
            ["RoiBottom"] = roiBottom,
            ["ExecutingAgentId"] = executingAgentId
        };
    }

    private static bool TryGetInt(JsonObject settings, string key, string deviceId, out int value)
    {
        value = default;

        var raw = settings[key]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (int.TryParse(raw, out value))
            return true;

        Console.WriteLine(
            $"[Startup] Device {deviceId}: Sink Cleanliness \"{key}\" value \"{raw}\" is not a whole number, ignored.");

        return false;
    }
}
