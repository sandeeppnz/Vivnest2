using System.Text.Json.Nodes;

namespace Vivnest.Abstractions.Configuration;

// Mirrors ImageCaptureRuntimeAdapter's shape (decision-log.md ADR-067)
// minus the Burst fields - Motion Detection has no burst-capture concept.
// Writes directly onto the device's own root LivenessInterval/
// WarningMultiplier/Schedule.Interval fields, exactly what
// MotionSensorMonitorWorker already reads off DeviceOptions. No
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

        if (TryGetMinutes(settings, "LivenessIntervalMinutes", deviceId, out var livenessInterval))
            flattenedDevice["LivenessInterval"] = livenessInterval.ToString();

        if (TryGetDouble(settings, "WarningMultiplier", deviceId, out var warningMultiplier))
            flattenedDevice["WarningMultiplier"] = warningMultiplier;

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
