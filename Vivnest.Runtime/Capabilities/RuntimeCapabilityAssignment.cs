namespace Vivnest.Runtime.Capabilities;

public sealed record RuntimeCapabilityAssignment
{
    public required string CapabilityId { get; init; }

    public required string CapabilityName { get; init; }

    public bool Enabled { get; init; }

    public string? ExecutingAgentId { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; }
        = new Dictionary<string, string>();
}
