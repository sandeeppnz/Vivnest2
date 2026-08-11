using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin;

public interface ISiteManagementService
{
    Task<IReadOnlyList<SiteDto>> GetByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<SiteDto?> GetAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    // Returns null if the Tenant doesn't exist (a Site must never exist
    // without a Tenant) or if this SiteId already exists under this
    // Tenant - same collision reasoning as ITenantManagementService.
    Task<SiteDto?> CreateAsync(
        string tenantId,
        string siteId,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    Task<SiteDto?> UpdateAsync(
        string tenantId,
        string siteId,
        string name,
        string? description,
        string status,
        CancellationToken cancellationToken = default);

    // Soft delete - flips Status to Inactive, leaves everything else
    // untouched. No hard delete exists, same reasoning as
    // ITenantManagementService.DeactivateAsync.
    Task<SiteDto?> DeactivateAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);
}
