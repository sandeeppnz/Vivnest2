using System.Text.Json.Nodes;

namespace Vivnest.Core.Configuration;

// Mirrors ImageCaptureRuntimeAdapter's shape (decision-log.md ADR-067)
// minus the Burst fields - Motion Detection has no burst-capture concept.
// Writes Schedule.Interval onto the device's own root, exactly what
// MotionSensorMonitorWorker already reads off DeviceOptions.
//
// It no longer writes LivenessInterval/WarningMultiplier (ADR-099): those
// are device liveness policy. This adapter and ImageCaptureRuntimeAdapter
// both used to write them, so a device carrying both capabilities got
// whichever value the later capabilities[] entry supplied - the same
// configuration producing different behaviour depending on array order. No
// device-type check needed here - MotionDetectionRuntimeProjector's own
// Cloud-side gate already guarantees (by construction, not convention)
// that a "Motion Detection" Capabilities[] entry only ever appears on a
// real DeviceType.MotionSensor device's document.
public sealed class MotionDetectionRuntimeAdapter : ICapabilityConfigRuntimeAdapter
{
    public string CapabilityName => "Motion Detection";

    public void Apply(JsonObject flattenedDevice, JsonObject capabilityEntry)
    {
        if (capabilityEntry["Settings"] is not JsonObject settings)
            return;

        var deviceId = flattenedDevice["DeviceId"]?.GetValue<string>() ?? "(unknown)";

        if (TryGetMinutes(settings, "BatteryReportIntervalMinutes", deviceId, out var batteryReportInterval))
        {
            var schedule = GetOrAddObject(flattenedDevice, "Schedule");
            schedule["Interval"] = batteryReportInterval.ToString();
        }
    }

    private static JsonObject GetOrAddObject(JsonObject parent, string key)
    {
        if (parent[key] is not JsonObject existing)
        {
            existing = new JsonObject();
            parent[key] = existing;
        }

        return existing;
    }

    private static bool TryGetDouble(JsonObject settings, string key, string deviceId, out double value)
    {
        value = default;

        var raw = settings[key]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (double.TryParse(raw, out value))
            return true;

        Console.WriteLine(
            $"[Startup] Device {deviceId}: Motion Detection \"{key}\" value \"{raw}\" is not a number, ignored.");

        return false;
    }

    private static bool TryGetMinutes(JsonObject settings, string key, string deviceId, out TimeSpan value)
    {
        value = default;

        if (!TryGetDouble(settings, key, deviceId, out var minutes))
            return false;

        value = TimeSpan.FromMinutes(minutes);

        return true;
    }
}
