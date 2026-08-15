using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// Mirrors ObjectDetectionRuntimeProjector exactly (decision-log.md
// ADR-066) - same cross-agent ROI/model-param split, minus
// ExpectedClasses (SinkCleanlinessModelOptions has no such field).
public sealed class SinkCleanlinessRuntimeProjector : ICapabilityRuntimeProjector
{
    public string CapabilityName => "Sink Cleanliness";

    private const string RoiLeftKey = "RoiLeft";
    private const string RoiTopKey = "RoiTop";
    private const string RoiRightKey = "RoiRight";
    private const string RoiBottomKey = "RoiBottom";
    private const string ModelPathKey = "ModelPath";
    private const string ConfidenceThresholdKey = "ConfidenceThreshold";

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
                warnings.Add($"Sink Cleanliness: \"{key}\" is not set.");
                continue;
            }

            if (!int.TryParse(value, out _))
            {
                warnings.Add($"Sink Cleanliness: \"{key}\" value \"{value}\" is not a whole number.");
                continue;
            }

            roiValues[key] = value;
        }

        string? modelPath = null;

        if (!assignedSettings.TryGetValue(ModelPathKey, out modelPath) || string.IsNullOrWhiteSpace(modelPath))
        {
            warnings.Add("Sink Cleanliness: \"ModelPath\" is not set.");
            modelPath = null;
        }

        string? confidenceThreshold = null;

        if (!assignedSettings.TryGetValue(ConfidenceThresholdKey, out confidenceThreshold)
            || string.IsNullOrWhiteSpace(confidenceThreshold))
        {
            warnings.Add("Sink Cleanliness: \"ConfidenceThreshold\" is not set.");
            confidenceThreshold = null;
        }
        else if (!double.TryParse(confidenceThreshold, out _))
        {
            warnings.Add($"Sink Cleanliness: \"ConfidenceThreshold\" value \"{confidenceThreshold}\" is not a number.");
            confidenceThreshold = null;
        }

        if (assignment.Enabled && string.IsNullOrWhiteSpace(executingRuntimeAgentId))
        {
            warnings.Add(
                "Sink Cleanliness: ExecutingAgentId doesn't resolve to a RuntimeAgentId - model parameters can't be routed anywhere.");
        }

        if (warnings.Count > 0)
            return new CapabilityProjectionResult(null, null, warnings);

        var deviceEntry = new CapabilityDocumentEntryDto(
            assignment.CapabilityId,
            CapabilityName,
            assignment.Enabled,
            executingRuntimeAgentId,
            roiValues);

        var agentEntry = new AgentCapabilityContribution(
            executingRuntimeAgentId!,
            device.RuntimeDeviceId ?? "",
            CapabilityName,
            new Dictionary<string, string>
            {
                [ModelPathKey] = modelPath!,
                [ConfidenceThresholdKey] = confidenceThreshold!
            });

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
