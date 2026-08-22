namespace Vivnest.Agent.Configuration;

public sealed class AgentCapabilityOptions
{
    public List<AgentCapabilityOption> Capabilities { get; set; } = [];
}

public sealed class AgentCapabilityOption
{
    public string CapabilityId { get; set; } = string.Empty;

    public string CapabilityKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}
