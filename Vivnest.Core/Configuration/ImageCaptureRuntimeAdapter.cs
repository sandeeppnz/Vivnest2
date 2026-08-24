using System.Globalization;
using System.Text.Json.Nodes;

namespace Vivnest.Core.Configuration;

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

        if (TryGetSeconds(settings, "ScheduleIntervalSeconds", deviceId, out var scheduleInterval))
        {
            var schedule = GetOrAddObject(flattenedDevice, "Schedule");
            schedule["Interval"] = scheduleInterval.ToString();
        }

        if (TryGetSeconds(settings, "BurstIntervalSeconds", deviceId, out var burstInterval)
            | TryGetSeconds(settings, "BurstDurationSeconds", deviceId, out var burstDuration))
        {
            var schedule = GetOrAddObject(flattenedDevice, "Schedule");
            var burst = GetOrAddObject(schedule, "Burst");

            if (burstInterval != default)
                burst["Interval"] = burstInterval.ToString();

            if (burstDuration != default)
                burst["Duration"] = burstDuration.ToString();
        }

        // LivenessInterval and WarningMultiplier are NOT written here any
        // more (ADR-099). They are device liveness policy, and this adapter
        // and MotionDetectionRuntimeAdapter both used to write them onto the
        // device root - so a device carrying both capabilities got whichever
        // value the later capabilities[] entry happened to supply. Same
        // configuration, different behaviour depending on array order.
        //
        // They now arrive on the device section itself, projected from the
        // device row, and no capability adapter may write them.
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

        // InvariantCulture is load-bearing here for the same reason
        // EventRowKey documents at length: these are wire values from a
        // published blob, and their meaning must not depend on the host's
        // locale. Under CurrentCulture this was not a skip-on-mismatch
        // hazard but a silent-corruption one - a comma-decimal culture
        // (de-DE) reads "1.5" as fifteen, because "." is its group
        // separator, and the parse SUCCEEDS. NumberStyles.Float excludes
        // AllowThousands, so that reading is impossible in any culture.
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        Console.WriteLine(
            $"[Startup] Device {deviceId}: Image Capture \"{key}\" value \"{raw}\" is not a number, ignored.");

        return false;
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
