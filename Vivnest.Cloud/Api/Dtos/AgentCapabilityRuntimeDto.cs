namespace Vivnest.Cloud.Api.Dtos;

// Settings is a generic string->string map on purpose: Cloud never parses
// a capability's own configuration, so adding a capability never means
// touching the projector or the wire contract. The capability that owns
// the schema is the thing that understands the values.
public sealed record AgentCapabilityRuntimeDto(
    string CapabilityId,
    string CapabilityKey,
    string Name,
    bool Enabled,
    IReadOnlyDictionary<string, string> Settings);
