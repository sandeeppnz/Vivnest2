namespace Vivnest.Core.Options;

/// <summary>
/// Opt-in ML object detection for one camera - see ADR-034's follow-up.
/// Null on <see cref="DeviceOptions.ObjectDetection"/> means disabled;
/// every camera not doing this stays exactly as it is today. One
/// detection pass feeds two independent uses: a "person" detection whose
/// box falls inside the ROI gates <see cref="SinkCleanlinessOptions"/>
/// classification (someone at the sink means "in use", not a fair
/// clean/dirty read); any other detected class inside the ROI that isn't
/// in <see cref="ExpectedClasses"/> is flagged as a new/unusual object.
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
    /// Case-insensitive. "person" never needs to be listed here - it's
    /// handled separately by the sink-classifier gate above, never itself
    /// flagged as unusual.
    /// </summary>
    public string[] ExpectedClasses { get; init; } = [];
}
