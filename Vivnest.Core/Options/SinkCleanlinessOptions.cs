namespace Vivnest.Core.Options;

/// <summary>
/// Opt-in ML sink-cleanliness classification for one camera - see ADR-032.
/// Null on <see cref="DeviceOptions.SinkCleanliness"/> means disabled;
/// every camera not doing this stays exactly as it is today.
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
