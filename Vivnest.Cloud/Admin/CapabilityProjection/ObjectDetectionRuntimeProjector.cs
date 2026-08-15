using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// The cross-agent case ADR-064's worked example (decision-log.md §1a)
// designed but never implemented (decision-log.md ADR-066) - unlike
// Image Capture (ADR-065, purely device-local), Object Detection's real
// runtime shape is split across two files: ROI on the device's own
// entry (ObjectDetectionRoiOptions, Vivnest.Core/Options), model
// parameters on the *executing* agent's own document
// (ObjectDetectionModelOptions, inside AiClassificationOptions.Devices[]).
// This projector produces both halves from one set of admin-typed
// Settings; a single Warnings list gates them together deliberately - a
// device with ROI configured but no model (or vice versa) is genuinely
// broken at runtime either way, so a half-configured capability should
// never quietly publish as "working" on just one side.
public sealed class ObjectDetectionRuntimeProjector : ICapabilityRuntimeProjector
{
    public string CapabilityName => "Object Detection";

    private const string RoiLeftKey = "RoiLeft";
    private const string RoiTopKey = "RoiTop";
    private const string RoiRightKey = "RoiRight";
    private const string RoiBottomKey = "RoiBottom";
    private const string ModelPathKey = "ModelPath";
    private const string ConfidenceThresholdKey = "ConfidenceThreshold";
    // Optional - comma-separated, matches the real
    // ObjectDetectionModelOptions.ExpectedClasses's own empty default
    // ("no restriction" is a legitimate state, not a missing value).
    private const string ExpectedClassesKey = "ExpectedClasses";

    private static readonly string[] RequiredIntKeys = [RoiLeftKey, RoiTopKey, RoiRightKey, RoiBottomKey];

    public CapabilityProjectionResult Project(
        DeviceCapabilityEntity assignment,
        DeviceRegistryEntity device,
        string? executingRuntimeAgentId)
    {
        var warnings = new List<string>();
        var assignedSettings = ParseSettings(assignment.Settings);

        var roiValues = new Dictionary<string, string>();

        foreach (var key in RequiredIntKeys)
        {
            if (!assignedSettings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                warnings.Add($"Object Detection: \"{key}\" is not set.");
                continue;
            }

            if (!int.TryParse(value, out _))
            {
                warnings.Add($"Object Detection: \"{key}\" value \"{value}\" is not a whole number.");
                continue;
            }

            roiValues[key] = value;
        }

        string? modelPath = null;

        if (!assignedSettings.TryGetValue(ModelPathKey, out modelPath) || string.IsNullOrWhiteSpace(modelPath))
        {
            warnings.Add("Object Detection: \"ModelPath\" is not set.");
            modelPath = null;
        }

        string? confidenceThreshold = null;

        if (!assignedSettings.TryGetValue(ConfidenceThresholdKey, out confidenceThreshold)
            || string.IsNullOrWhiteSpace(confidenceThreshold))
        {
            warnings.Add("Object Detection: \"ConfidenceThreshold\" is not set.");
            confidenceThreshold = null;
        }
        else if (!double.TryParse(confidenceThreshold, out _))
        {
            warnings.Add($"Object Detection: \"ConfidenceThreshold\" value \"{confidenceThreshold}\" is not a number.");
            confidenceThreshold = null;
        }

        if (assignment.Enabled && string.IsNullOrWhiteSpace(executingRuntimeAgentId))
        {
            warnings.Add(
                "Object Detection: ExecutingAgentId doesn't resolve to a RuntimeAgentId - model parameters can't be routed anywhere.");
        }

        if (warnings.Count > 0)
            return new CapabilityProjectionResult(null, null, warnings);

        var deviceEntry = new CapabilityDocumentEntryDto(
            assignment.CapabilityId,
            CapabilityName,
            assignment.Enabled,
            executingRuntimeAgentId,
            roiValues);

        var agentSettings = new Dictionary<string, string>
        {
            [ModelPathKey] = modelPath!,
            [ConfidenceThresholdKey] = confidenceThreshold!
        };

        if (assignedSettings.TryGetValue(ExpectedClassesKey, out var expectedClasses)
            && !string.IsNullOrWhiteSpace(expectedClasses))
        {
            agentSettings[ExpectedClassesKey] = expectedClasses;
        }

        var agentEntry = new AgentCapabilityContribution(
            executingRuntimeAgentId!,
            device.RuntimeDeviceId ?? "",
            CapabilityName,
            agentSettings);

        return new CapabilityProjectionResult(deviceEntry, agentEntry, warnings);
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
