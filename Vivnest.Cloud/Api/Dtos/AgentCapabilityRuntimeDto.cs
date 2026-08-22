namespace Vivnest.Cloud.Api.Dtos;

public sealed record AgentCapabilityRuntimeDto(
    string CapabilityId,
    string Name,
    bool Enabled);
