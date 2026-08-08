namespace Vivnest.Core.Options;

/// <summary>
/// Ai-role agent config, section "AiClassification" (decision-log.md
/// ADR-035's follow-up) - a flat list of per-device model behavior, since
/// the Ai-agent has no <see cref="DevicesOptions"/> of its own (it owns no
/// physical devices) and needs some way to know how to classify captures
/// for devices it's never configured with directly.
/// </summary>
public sealed class AiClassificationOptions
{
    public List<AiDeviceClassification> Devices { get; set; } = [];
}

/// <summary>
/// One entry per device the Ai-agent is willing to classify captures for.
/// Both blocks are optional independently, same as
/// <see cref="DeviceOptions.SinkCleanliness"/>/<see cref="DeviceOptions.ObjectDetection"/>
/// being independently nullable - a device can use one capability without
/// the other.
/// </summary>
public sealed class AiDeviceClassification
{
    public required string DeviceId { get; init; }
    public SinkCleanlinessModelOptions? SinkCleanliness { get; init; }
    public ObjectDetectionModelOptions? ObjectDetection { get; init; }
}
