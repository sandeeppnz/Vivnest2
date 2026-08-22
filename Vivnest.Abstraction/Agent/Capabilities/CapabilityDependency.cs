namespace Vivnest.Abstraction.Agent.Capabilities;

public sealed record CapabilityDependency(
    string CapabilityId,
    string MinimumVersion);