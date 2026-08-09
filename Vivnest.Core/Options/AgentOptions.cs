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
}
