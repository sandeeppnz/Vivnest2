using Vivnest.Core.Enums;

namespace Vivnest.Core.Options;


public class AgentOptions
{
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;

    // Which capabilities this process registers - see Program.cs and
    // decision-log.md ADR-035. Defaults to Capture so every existing
    // agent config needs zero changes.
    public AgentRole Role { get; set; } = AgentRole.Capture;

    // Capture-role only: which Ai-role agent this one forwards
    // classification work to (ADR-035, design 3). Empty on any agent
    // with no paired Ai-agent - SinkCleanlinessHandler simply has
    // nowhere to route to, same as SinkCleanliness/ObjectDetection not
    // being enabled at all.
    public string AiAgentId { get; set; } = string.Empty;
}
