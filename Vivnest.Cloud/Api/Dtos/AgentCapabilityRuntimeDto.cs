namespace Vivnest.Cloud.Api.Dtos;

public sealed record AgentCapabilityRuntimeDto(
    string CapabilityId,
    string CapabilityKey,
    string Name,
    bool Enabled);
