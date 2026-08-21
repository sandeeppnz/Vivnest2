namespace Vivnest.Abstractions.Enums;

// Which capabilities this agent process registers - see decision-log.md
// ADR-035/ADR-044. Defaults to Low everywhere it isn't explicitly set (the
// existing single-agent deployment never needs to know this enum exists).
// Low = camera/smart-plug/motion-sensor capture (originally "Capture");
// High = ONNX inference (originally "Ai") - the enum itself (AgentRole)
// and its values were renamed in ADR-044, same two behaviors throughout.
public enum AgentType
{
    Low,
    High
}
