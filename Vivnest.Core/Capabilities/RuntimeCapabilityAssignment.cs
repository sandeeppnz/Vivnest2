namespace Vivnest.Core.Capabilities;

// Lives beside ICapabilityContext, which carries one (ADR-101), and not
// in Vivnest.Runtime. Runtime references Core, so putting an assignment on
// the context from there would have inverted the dependency direction. A
// contract the shared layer exposes belongs in the shared layer.
//
// That layer was Vivnest.Abstraction until 2026-08-24, when it merged into
// Vivnest.Core (ADR-112). The constraint is unchanged - Core has no
// project references either - only the project name.
public sealed record RuntimeCapabilityAssignment
{
    public required string CapabilityId { get; init; }

    public required string CapabilityName { get; init; }

    public bool Enabled { get; init; }

    public string? ExecutingAgentId { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; }
        = new Dictionary<string, string>();
}
