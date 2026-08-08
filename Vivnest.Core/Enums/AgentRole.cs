namespace Vivnest.Core.Enums;

// Which capabilities this agent process registers - see decision-log.md
// ADR-035. Defaults to Capture everywhere it isn't explicitly set (the
// existing single-agent deployment never needs to know this enum exists).
public enum AgentRole
{
    Capture,
    Ai
}
