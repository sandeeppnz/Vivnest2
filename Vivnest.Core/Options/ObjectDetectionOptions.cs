namespace Vivnest.Core.Options;

/// <summary>
/// The full set of inputs <see cref="Vivnest.Capabilities.AiClassification.Inference.IObjectDetector"/>
/// needs to detect on one capture - see ADR-034's follow-up. Since
/// ADR-035's follow-up, nothing configures this shape directly: it's
/// assembled at detect time by merging <see cref="ObjectDetectionRoiOptions"/>
/// (camera-specific, arrives over the classify-request message) with
/// <see cref="ObjectDetectionModelOptions"/> (High-type-agent-specific, looked up
/// locally by DeviceId). A detected class inside the ROI that isn't in
/// <see cref="ExpectedClasses"/> is flagged as a new/unusual object.
/// <para>
/// A "person" detection inside the ROI used to gate
/// <see cref="SinkCleanlinessOptions"/> classification - someone at the
/// sink means "in use", not a fair clean/dirty read. ADR-036 dropped that
/// coupling rather than rebuilding it: the two capabilities now route
/// independently, possibly to different High-type agents, so neither can
/// depend on the other having run first. A person inside the ROI is still
/// recorded, as LastPersonSeenUtc, but it decides nothing.
/// </para>
/// </summary>
public sealed class ObjectDetectionOptions
{
    public bool Enabled { get; init; }

    /// <summary>
    /// Region of interest within the raw capture - the sink/counter area,
    /// not the whole frame. Pixel coordinates in the source image's own
    /// resolution. Independent of SinkCleanlinessOptions's own ROI fields
    /// even though they're typically the same region for a given camera -
    /// kept separate since either capability can be enabled without the
    /// other.
    /// </summary>
    public int RoiLeft { get; init; }
    public int RoiTop { get; init; }
    public int RoiRight { get; init; }
    public int RoiBottom { get; init; }

    /// <summary>
    /// Path to the .onnx model file, relative to the Agent's base
    /// directory (same convention as SinkCleanlinessOptions.ModelPath).
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
    /// outside this list becomes an UnusualObjectDetected DeviceEvent.
    /// Case-insensitive. "person" never needs to be listed here - it is
    /// treated separately and never itself flagged as unusual.
    /// </summary>
    public string[] ExpectedClasses { get; init; } = [];
}
