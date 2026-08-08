namespace Vivnest.Core.Options;

/// <summary>
/// Opt-in ML object detection for one camera - the camera-specific half
/// only (see ADR-034's follow-up, and decision-log.md ADR-035's follow-up
/// for the split). Region-of-interest pixel coordinates only mean anything
/// in the context of this exact camera's framing/mounting, so they stay
/// here; how the detector itself behaves (model, confidence threshold,
/// expected classes) is Ai-agent config instead - see
/// <see cref="ObjectDetectionModelOptions"/>. Null on
/// <see cref="DeviceOptions.ObjectDetection"/> means disabled; every
/// camera not doing this stays exactly as it is today.
/// </summary>
public sealed class ObjectDetectionRoiOptions
{
    public bool Enabled { get; init; }

    /// <summary>
    /// Region of interest within the raw capture - the sink/counter area,
    /// not the whole frame. Pixel coordinates in the source image's own
    /// resolution. Independent of SinkCleanlinessRoiOptions's own ROI
    /// fields even though they're typically the same region for a given
    /// camera - kept separate since either capability can be enabled
    /// without the other.
    /// </summary>
    public int RoiLeft { get; init; }
    public int RoiTop { get; init; }
    public int RoiRight { get; init; }
    public int RoiBottom { get; init; }
}
