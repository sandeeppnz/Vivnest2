namespace Vivnest.Core.Capabilities;

public sealed record CapabilityManifest
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public IReadOnlyCollection<CapabilityCommandDescriptor> Commands { get; init; } = [];

    public IReadOnlyCollection<CapabilityEventDescriptor> ProducedEvents { get; init; } = [];

    public IReadOnlyCollection<CapabilityEventDescriptor> ConsumedEvents { get; init; } = [];

    public IReadOnlyCollection<CapabilityDependency> Dependencies { get; init; } = [];
}