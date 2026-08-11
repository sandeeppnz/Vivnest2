namespace Vivnest.Cloud.Api.Dtos;

public sealed record SiteDto(
    string TenantId,
    string SiteId,
    string Name,
    string? Description,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateSiteRequest(
    string SiteId,
    string Name,
    string? Description);

public sealed record UpdateSiteRequest(
    string Name,
    string? Description,
    string Status);
