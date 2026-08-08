namespace Vivnest.Core.Options;

/// <summary>
/// How the sink-cleanliness classifier itself behaves - Ai-role agent
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
    /// Minimum classifier confidence to trust a "dirty" prediction enough to
    /// alert on it. Below this, treated as clean for transition purposes -
    /// an unconfident "maybe dirty" shouldn't page anyone.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.6;
}
