using System.Text.Json.Nodes;

namespace Vivnest.Agent.Runtime.Configuration;

// First real ICapabilityConfigRuntimeAdapter (decision-log.md ADR-065 /
// Phase 6C) - mirrors Vivnest.Cloud's ImageCaptureRuntimeProjector.
// "Image Capture" is deliberately the proof case for a capability whose
// runtime shape is NOT a nested DeviceOptions sub-object: it writes
// directly onto the device's own root Schedule/LivenessInterval/
// WarningMultiplier fields. CameraCaptureWorker never changes - it
// already reads these exact fields off DeviceOptions via
// IDeviceRuntimeStore, unaware of where they came from.
//
// Re-validates every value even though the Cloud-side projector already
// did (decision-log.md ADR-065, matching the user's explicit "Agent
// validation protects runtime safety, never assume a Blob is trustworthy
// just because Admin generated it" instruction) - a value that fails to
// parse here is skipped with a console warning, leaving that one field at
// DeviceOptions' own default rather than throwing and losing the whole
// device.
public sealed class ImageCaptureRuntimeAdapter : ICapabilityConfigRuntimeAdapter
{
    public string CapabilityName => "Image Capture";

    public void Apply(JsonObject flattenedDevice, JsonObject capabilityEntry)
    {
        if (capabilityEntry["Settings"] is not JsonObject settings)
            return;

        var deviceId = flattenedDevice["DeviceId"]?.GetValue<string>() ?? "(unknown)";

        if (TryGetMinutes(settings, "ScheduleIntervalMinutes", deviceId, out var scheduleInterval))
        {
            var schedule = GetOrAddObject(flattenedDevice, "Schedule");
            schedule["Interval"] = scheduleInterval.ToString();
        }

        if (TryGetSeconds(settings, "BurstIntervalSeconds", deviceId, out var burstInterval)
            | TryGetMinutes(settings, "BurstDurationMinutes", deviceId, out var burstDuration))
        {
            var schedule = GetOrAddObject(flattenedDevice, "Schedule");
            var burst = GetOrAddObject(schedule, "Burst");

            if (burstInterval != default)
                burst["Interval"] = burstInterval.ToString();

            if (burstDuration != default)
                burst["Duration"] = burstDuration.ToString();
        }

        if (TryGetMinutes(settings, "LivenessIntervalMinutes", deviceId, out var livenessInterval))
            flattenedDevice["LivenessInterval"] = livenessInterval.ToString();

        if (TryGetDouble(settings, "WarningMultiplier", deviceId, out var warningMultiplier))
            flattenedDevice["WarningMultiplier"] = warningMultiplier;
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
            $"[Startup] Device {deviceId}: Image Capture \"{key}\" value \"{raw}\" is not a number, ignored.");

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

    private static bool TryGetSeconds(JsonObject settings, string key, string deviceId, out TimeSpan value)
    {
        value = default;

        if (!TryGetDouble(settings, key, deviceId, out var seconds))
            return false;

        value = TimeSpan.FromSeconds(seconds);

        return true;
    }
}
