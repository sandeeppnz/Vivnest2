namespace Vivnest.Cloud.Admin.CapabilityProjection;

// Object Detection's Cloud-side projection. The ROI/model translation
// lives in RoiCapabilityRuntimeProjector (ADR-108), shared with Sink
// Cleanliness. Only the optional ExpectedClasses setting is specific here.
public sealed class ObjectDetectionRuntimeProjector : RoiCapabilityRuntimeProjector
{
    // Optional - comma-separated, matching the real
    // ObjectDetectionModelOptions.ExpectedClasses's own empty default
    // ("no restriction" is a legitimate state, not a missing value), which
    // is why it is added here rather than being a required key.
    private const string ExpectedClassesKey = "ExpectedClasses";

    public override string CapabilityName => "Object Detection";

    protected override void AddAgentSettings(
        IReadOnlyDictionary<string, string> assignedSettings,
        IDictionary<string, string> agentSettings)
    {
        if (assignedSettings.TryGetValue(ExpectedClassesKey, out var expectedClasses)
            && !string.IsNullOrWhiteSpace(expectedClasses))
        {
            agentSettings[ExpectedClassesKey] = expectedClasses;
        }
    }
}
