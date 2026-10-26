using System.Globalization;
using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// "Audio Alarm" - listen to a camera's audio track for fire-alarm tones
// (audio.alarm, Vivnest.Capabilities/Audio/). Device-local and flat like
// ImageCaptureRuntimeProjector: AgentEntry is always null, and where the
// settings land is AudioAlarmRuntimeAdapter's decision on the Agent side.
//
// Nothing is required. Every key has a default on the Agent
// (AudioAlarmOptions), so an assignment with no settings still listens -
// every 30 s, a 10 s clip, a 10 minute cooldown - rather than being
// rejected for not having chosen numbers. Any key that IS supplied must be
// a number, so a typo fails the assignment visibly here instead of being
// ignored silently on the Agent.
public sealed class AudioAlarmRuntimeProjector : ICapabilityRuntimeProjector
{
    public string CapabilityName => "Audio Alarm";

    private static readonly string[] NumericKeys =
    [
        "ScheduleIntervalSeconds",
        "ClipSeconds",
        "CooldownMinutes"
    ];

    public CapabilityProjectionResult Project(
        DeviceCapabilityEntity assignment,
        DeviceRegistryEntity device,
        string? executingRuntimeAgentId)
    {
        var warnings = new List<string>();
        var settings = new Dictionary<string, string>();
        var assignedSettings = ParseSettings(assignment.Settings);

        if (assignedSettings == null)
        {
            return new CapabilityProjectionResult(
                null, null,
                ["Audio Alarm: assignment Settings is not valid JSON; nothing published for it."]);
        }

        foreach (var key in NumericKeys)
        {
            if (!assignedSettings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || number < 0)
            {
                warnings.Add($"Audio Alarm: \"{key}\" value \"{value}\" is not a non-negative number.");
                continue;
            }

            settings[key] = value;
        }

        if (warnings.Count > 0)
            return new CapabilityProjectionResult(null, null, warnings);

        var deviceEntry = new CapabilityDocumentEntryDto(
            assignment.CapabilityId,
            CapabilityName,
            assignment.Enabled,
            executingRuntimeAgentId,
            settings);

        return new CapabilityProjectionResult(deviceEntry, null, warnings);
    }

    // Null means the stored Settings string is not valid JSON; the caller
    // turns that into a warning on the ASSIGNMENT, not a failed projection.
    private static IReadOnlyDictionary<string, string>? ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
