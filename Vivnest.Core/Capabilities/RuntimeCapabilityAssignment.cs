namespace Vivnest.Core.Capabilities;

// Lives in Vivnest.Abstraction, not Vivnest.Runtime, because
// ICapabilityContext carries one (ADR-101). Abstraction has no project
// references by design; had this stayed in Runtime, putting an assignment
// on the context would have required Abstraction -> Runtime and inverted
// the dependency direction. A contract the abstraction layer exposes
// belongs in the abstraction layer.
public sealed record RuntimeCapabilityAssignment
{
    public required string CapabilityId { get; init; }

    public required string CapabilityName { get; init; }

    public bool Enabled { get; init; }

    public string? ExecutingAgentId { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; }
        = new Dictionary<string, string>();
}
