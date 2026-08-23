namespace Vivnest.Core.Options;

/// <summary>
/// How the sink-cleanliness classifier itself behaves - High-type agent
/// config only (decision-log.md ADR-035's follow-up), independent of any
/// one camera's framing. Looked up by DeviceId from
/// <see cref="AiClassificationOptions"/> and merged with the capturing
/// agent's <see cref="SinkCleanlinessRoiOptions"/> (received over the
/// classify-request message) into a full <see cref="SinkCleanlinessOptions"/>
/// right before classifying.
/// </summary>
public sealed class SinkCleanlinessModelOptions
{
    /// <summary>
    /// Path to the .onnx model file, relative to the Agent's base
    /// directory (same convention as the bundled ffmpeg.exe path).
    /// </summary>
    public string ModelPath { get; init; } = "";

    /// <summary>
    /// Minimum classifier confidence for a prediction to count as an
    /// observation at all. Below this the capture is SKIPPED - no
    /// DeviceEvent, no notification, and the device's last known state is
    /// left alone.
    /// <para>
    /// Until 2026-08-24 this gated only the "dirty" direction and a
    /// low-confidence "dirty" was recorded as a positive clean reading,
    /// which could itself fire a dirty-to-clean transition. The intent was
    /// always that an unconfident "maybe dirty" shouldn't page anyone;
    /// skipping serves that better, since it claims nothing in either
    /// direction.
    /// </para>
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.6;
}
