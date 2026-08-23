namespace Vivnest.Core.Options;

/// <summary>
/// The full set of inputs <see cref="Vivnest.Capabilities.Camera.ISinkCleanlinessClassifier"/>
/// needs to classify one capture - see ADR-032. Since ADR-035's follow-up,
/// nothing configures this shape directly: it's assembled at classify time
/// by merging <see cref="SinkCleanlinessRoiOptions"/> (camera-specific,
/// arrives over the classify-request message) with
/// <see cref="SinkCleanlinessModelOptions"/> (High-type-agent-specific, looked up
/// locally by DeviceId).
/// </summary>
public sealed class SinkCleanlinessOptions
{
    public bool Enabled { get; init; }

    /// <summary>
    /// Region of interest within the raw capture to crop before classifying
    /// - the sink/counter area, not the whole frame. Pixel coordinates in
    /// the source image's own resolution.
    /// </summary>
    public int RoiLeft { get; init; }
    public int RoiTop { get; init; }
    public int RoiRight { get; init; }
    public int RoiBottom { get; init; }

    /// <summary>
    /// Path to the .onnx model file, relative to the Agent's base
    /// directory (same convention as the bundled ffmpeg.exe path).
    /// </summary>
    public string ModelPath { get; init; } = "";

    /// <summary>
    /// Minimum classifier confidence to trust a "dirty" prediction enough to
    /// alert on it. Below this, treated as clean for transition purposes -
    /// an unconfident "maybe dirty" shouldn't page anyone.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.6;
}
