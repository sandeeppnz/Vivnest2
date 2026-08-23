using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// The Cloud half of an ROI classification capability, shared by Object
// Detection and Sink Cleanliness (ADR-108). The Agent half is
// RoiCapabilityRuntimeAdapter in Vivnest.Core.
//
// The cross-agent case ADR-064's worked example (§1a) designed and ADR-066
// implemented: unlike Image Capture (ADR-065, purely device-local), an ROI
// capability's runtime shape is split across two documents - ROI on the
// device's own entry, model parameters on the *executing* agent's
// document, inside AiClassificationOptions.Devices[].
//
// One Warnings list gates both halves deliberately. A device with ROI
// configured but no model - or the reverse - is genuinely broken at
// runtime either way, so a half-configured capability must never quietly
// publish as working on just one side.
public abstract class RoiCapabilityRuntimeProjector : ICapabilityRuntimeProjector
{
    protected const string RoiLeftKey = "RoiLeft";
    protected const string RoiTopKey = "RoiTop";
    protected const string RoiRightKey = "RoiRight";
    protected const string RoiBottomKey = "RoiBottom";
    protected const string ModelPathKey = "ModelPath";
    protected const string ConfidenceThresholdKey = "ConfidenceThreshold";

    private static readonly string[] RequiredIntKeys =
        [RoiLeftKey, RoiTopKey, RoiRightKey, RoiBottomKey];

    public abstract string CapabilityName { get; }

    // Settings beyond ModelPath/ConfidenceThreshold that belong on the
    // executing agent's document. Object Detection adds ExpectedClasses
    // here; Sink Cleanliness has none. A hook rather than a shared
    // optional-key list, because "optional" differs per capability and a
    // list would make every capability carry every other one's keys.
    protected virtual void AddAgentSettings(
        IReadOnlyDictionary<string, string> assignedSettings,
        IDictionary<string, string> agentSettings)
    {
    }

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
                warnings.Add($"{CapabilityName}: \"{key}\" is not set.");
                continue;
            }

            if (!int.TryParse(value, out _))
            {
                warnings.Add($"{CapabilityName}: \"{key}\" value \"{value}\" is not a whole number.");
                continue;
            }

            roiValues[key] = value;
        }

        string? modelPath = null;

        if (!assignedSettings.TryGetValue(ModelPathKey, out modelPath) || string.IsNullOrWhiteSpace(modelPath))
        {
            warnings.Add($"{CapabilityName}: \"ModelPath\" is not set.");
            modelPath = null;
        }

        string? confidenceThreshold = null;

        if (!assignedSettings.TryGetValue(ConfidenceThresholdKey, out confidenceThreshold)
            || string.IsNullOrWhiteSpace(confidenceThreshold))
        {
            warnings.Add($"{CapabilityName}: \"ConfidenceThreshold\" is not set.");
            confidenceThreshold = null;
        }
        else if (!double.TryParse(confidenceThreshold, out _))
        {
            warnings.Add($"{CapabilityName}: \"ConfidenceThreshold\" value \"{confidenceThreshold}\" is not a number.");
            confidenceThreshold = null;
        }

        if (assignment.Enabled && string.IsNullOrWhiteSpace(executingRuntimeAgentId))
        {
            warnings.Add(
                $"{CapabilityName}: ExecutingAgentId doesn't resolve to a RuntimeAgentId - model parameters can't be routed anywhere.");
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

        AddAgentSettings(assignedSettings, agentSettings);

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
