namespace Vivnest.Agent.Configuration;

// The wrapper class this file used to open with - AgentCapabilityOptions,
// holding a List<AgentCapabilityOption> Capabilities - was deleted on
// 2026-08-24. Nothing referenced it.
//
// It was collateral from the binding bug ADR-097 (5J) found and fixed:
// GetSection("Capabilities").Bind(options) looked for
// Capabilities:Capabilities, matched nothing, and produced an empty list
// with no error. The fix was .Get<List<AgentCapabilityOption>>(), which
// binds the section as the list it actually is - and no longer needs a
// wrapper to bind INTO. The wrapper outlived the mistake that required it.

public sealed class AgentCapabilityOption
{
    public string CapabilityId { get; set; } = string.Empty;

    public string CapabilityKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public Dictionary<string, string> Settings { get; set; } = new();
}
