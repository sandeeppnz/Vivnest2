using System.Text.Json.Nodes;

namespace Vivnest.Core.Configuration;

// Mirrors ImageCaptureRuntimeAdapter's shape (decision-log.md ADR-066)
// but writes a NESTED DeviceOptions sub-object instead of root fields -
// matches the real ObjectDetectionRoiOptions shape exactly:
// {Enabled, RoiLeft, RoiTop, RoiRight, RoiBottom, ExecutingAgentId}.
// ExecutingAgentId comes straight from the capability entry's own
// ExecutingAgentId - already the resolved RuntimeAgentId by the time it
// reaches here (the Cloud-side projector resolved it), exactly the value
// SinkCleanlinessHandler/the classify-request queue message already
// expects. No ModelPath/ConfidenceThreshold/ExpectedClasses here at all -
// those live entirely on the executing agent's own blob, read directly
// via AiClassificationOptions binding with no adapter (ADR-064).
public sealed class ObjectDetectionRuntimeAdapter : ICapabilityConfigRuntimeAdapter
{
    public string CapabilityName => "Object Detection";

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
                $"[Startup] Device {deviceId}: Object Detection ROI values incomplete/invalid, ObjectDetection left unconfigured.");
            return;
        }

        var enabled = capabilityEntry["Enabled"]?.GetValue<bool>() ?? false;
        var executingAgentId = capabilityEntry["ExecutingAgentId"]?.GetValue<string>() ?? "";

        flattenedDevice["ObjectDetection"] = new JsonObject
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
            $"[Startup] Device {deviceId}: Object Detection \"{key}\" value \"{raw}\" is not a whole number, ignored.");

        return false;
    }
}
