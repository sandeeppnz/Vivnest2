using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// First real ICapabilityRuntimeProjector (Phase 6C / decision-log.md
// ADR-065) - proves capability-specific translation for a capability
// whose real runtime shape is NOT itself capability-shaped: "Image
// Capture" doesn't correspond to a nested DeviceOptions sub-object the
// way ObjectDetection/SinkCleanliness do - it's flat fields directly on
// DeviceOptions (Schedule.Interval, Schedule.Burst.Interval/.Duration,
// LivenessInterval, WarningMultiplier). This projector still emits the
// same uniform CapabilityDocumentEntryDto shape every other capability
// does (CapabilityId/Name/Enabled/ExecutingAgentId/Settings) - where the
// Settings ultimately land is a Runtime Adapter decision on the Agent
// side (ImageCaptureRuntimeAdapter), not a wire-contract concern.
//
// Purely device-local - AgentEntry is always null, Image Capture never
// needs anything on the executing/owning agent's own document.
public sealed class ImageCaptureRuntimeProjector : ICapabilityRuntimeProjector
{
    public string CapabilityName => "Image Capture";

    // Admin-typed key names on DeviceCapability.Settings - the real
    // "Image Capture" Capability.ConfigurationSchema must be redefined to
    // exactly these (data change, not a domain-model change). All five
    // required - a capability meant to fully specify capture cadence
    // shouldn't silently fall back to guessed defaults.
    private const string ScheduleIntervalSecondsKey = "ScheduleIntervalSeconds";
    private const string BurstIntervalSecondsKey = "BurstIntervalSeconds";
    private const string BurstDurationSecondsKey = "BurstDurationSeconds";
    private const string LivenessIntervalSecondsKey = "LivenessIntervalSeconds";
    private const string WarningMultiplierKey = "WarningMultiplier";

    private static readonly string[] RequiredKeys =
    [
        ScheduleIntervalSecondsKey,
        BurstIntervalSecondsKey,
        BurstDurationSecondsKey,
        LivenessIntervalSecondsKey,
        WarningMultiplierKey
    ];

    public CapabilityProjectionResult Project(
        DeviceCapabilityEntity assignment,
        DeviceRegistryEntity device,
        string? executingRuntimeAgentId)
    {
        var warnings = new List<string>();
        var settings = new Dictionary<string, string>();
        var assignedSettings = ParseSettings(assignment.Settings);

        foreach (var key in RequiredKeys)
        {
            if (!assignedSettings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                warnings.Add($"Image Capture: \"{key}\" is not set.");
                continue;
            }

            if (!double.TryParse(value, out _))
            {
                warnings.Add($"Image Capture: \"{key}\" value \"{value}\" is not a number.");
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

    // Same JSON-serialized string->string map convention DeviceRuntimeConfigurationProjector.ParseSettings
    // already uses for Device.Settings.
    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
