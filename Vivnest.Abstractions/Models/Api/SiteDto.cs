namespace Vivnest.Abstractions.Models.Api;

// TenantId/SiteId are generated Guids - same convention as TenantDto.
public sealed record SiteDto(
    Guid TenantId,
    Guid SiteId,
    string Name,
    string? Description,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CreateSiteRequest(
    string Name,
    string? Description);

public sealed record UpdateSiteRequest(
    string Name,
    string? Description,
    string Status);
