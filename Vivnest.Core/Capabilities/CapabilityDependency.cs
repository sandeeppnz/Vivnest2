namespace Vivnest.Core.Capabilities;

public sealed record CapabilityDependency(
    string CapabilityId,
    string MinimumVersion);