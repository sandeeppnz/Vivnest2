namespace Vivnest.Core.Options;

/// <summary>
/// Opt-in ML sink-cleanliness classification for one camera - the
/// camera-specific half only (see ADR-032, and decision-log.md ADR-035's
/// follow-up for the split). Region-of-interest pixel coordinates only
/// mean anything in the context of this exact camera's framing/mounting,
/// so they stay here; how the classifier itself behaves (model, confidence
/// threshold) is High-type agent config instead - see
/// <see cref="SinkCleanlinessModelOptions"/>. Null on
/// <see cref="DeviceOptions.SinkCleanliness"/> means disabled; every
/// camera not doing this stays exactly as it is today.
/// </summary>
public sealed class SinkCleanlinessRoiOptions
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
    /// Which High-type agent executes this capability's classification for
    /// this camera (ADR-036) - replaces the old single, whole-agent
    /// AgentOptions.AiAgentId, since SinkCleanliness and ObjectDetection on
    /// the same camera can now route to different High-type agents. Empty means
    /// nowhere to route to, same as Enabled being false.
    /// </summary>
    public string ExecutingAgentId { get; init; } = "";
}
