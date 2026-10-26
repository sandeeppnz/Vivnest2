using System.Globalization;
using System.Text.Json.Nodes;

namespace Vivnest.Core.Configuration;

// Mirrors Vivnest.Cloud's AudioAlarmRuntimeProjector. Writes a nested
// AudioAlarm object onto the flattened device - the presence of that object
// is what tells AudioAlarmWorker this device is to be listened to, so a
// disabled assignment writes nothing at all rather than a disabled one.
//
// Re-validates every value though the projector already did (ADR-065): a
// value that fails to parse is skipped, leaving AudioAlarmOptions' own
// default in force rather than throwing and losing the whole device.
public sealed class AudioAlarmRuntimeAdapter : ICapabilityConfigRuntimeAdapter
{
    public string CapabilityName => "Audio Alarm";

    public void Apply(JsonObject flattenedDevice, JsonObject capabilityEntry)
    {
        if (capabilityEntry["Enabled"]?.GetValue<bool>() == false)
            return;

        var deviceId = flattenedDevice["DeviceId"]?.GetValue<string>() ?? "(unknown)";

        var audioAlarm = new JsonObject();

        if (capabilityEntry["Settings"] is JsonObject settings)
        {
            if (TryGetNumber(settings, "ScheduleIntervalSeconds", deviceId, out var interval))
                audioAlarm["Interval"] = TimeSpan.FromSeconds(interval).ToString();

            if (TryGetNumber(settings, "ClipSeconds", deviceId, out var clip))
                audioAlarm["ClipDuration"] = TimeSpan.FromSeconds(clip).ToString();

            if (TryGetNumber(settings, "CooldownMinutes", deviceId, out var cooldown))
                audioAlarm["Cooldown"] = TimeSpan.FromMinutes(cooldown).ToString();
        }

        flattenedDevice["AudioAlarm"] = audioAlarm;
    }

    private static bool TryGetNumber(JsonObject settings, string key, string deviceId, out double value)
    {
        value = default;

        var raw = settings[key]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // InvariantCulture is load-bearing - see ImageCaptureRuntimeAdapter.
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        Console.WriteLine(
            $"[Startup] Device {deviceId}: Audio Alarm \"{key}\" value \"{raw}\" is not a number, ignored.");

        return false;
    }
}
