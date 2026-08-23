namespace Vivnest.Core.Options;

/// <summary>
/// How the object detector itself behaves - High-type agent config only
/// (decision-log.md ADR-035's follow-up), independent of any one camera's
/// framing. Looked up by DeviceId from <see cref="AiClassificationOptions"/>
/// and merged with the capturing agent's <see cref="ObjectDetectionRoiOptions"/>
/// (received over the classify-request message) into a full
/// <see cref="ObjectDetectionOptions"/> right before detecting.
/// </summary>
public sealed class ObjectDetectionModelOptions
{
    /// <summary>
    /// Path to the .onnx model file, relative to the Agent's base
    /// directory (same convention as SinkCleanlinessModelOptions.ModelPath).
    /// </summary>
    public string ModelPath { get; init; } = "";

    /// <summary>
    /// Minimum detection confidence to trust a box at all - below this,
    /// discarded before class comparison or NMS.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.5;

    /// <summary>
    /// Class names (see ObjectDetector.CocoClassNames) considered normal
    /// for this camera's counter - anything detected inside the ROI but
    /// outside this list becomes an ObjectsDetected DeviceEvent flagged
    /// Unusual. Case-insensitive. "person" never needs to be listed here -
    /// it is treated separately and never itself flagged as unusual.
    /// </summary>
    public string[] ExpectedClasses { get; init; } = [];
}
