namespace Vivnest.Cloud.Api.Dtos;

public sealed record TenantDto(
    string TenantId,
    string Name,
    string? Description,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateTenantRequest(
    string TenantId,
    string Name,
    string? Description);

public sealed record UpdateTenantRequest(
    string Name,
    string? Description,
    string Status);
