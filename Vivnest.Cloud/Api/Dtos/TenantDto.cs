namespace Vivnest.Cloud.Api.Dtos;

// TenantId is a generated Guid - same convention as AgentRegistryDto.AgentId
// - not accepted on create, only ever server-assigned.
public sealed record TenantDto(
    Guid TenantId,
    string Name,
    string? Description,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateTenantRequest(
    string Name,
    string? Description);

public sealed record UpdateTenantRequest(
    string Name,
    string? Description,
    string Status);
