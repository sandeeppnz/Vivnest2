using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Site domain model (Vivnest.Core.Domain) to/
// from SiteEntity for storage. Depends on ITenantStore only to enforce "a
// Site must never exist without a Tenant" at create time - it never reads
// or writes Tenant data otherwise. No hard delete, same reasoning as
// TenantManagementService.
public sealed class SiteManagementService : ISiteManagementService
{
    private readonly ISiteStore _sites;
    private readonly ITenantStore _tenants;

    public SiteManagementService(ISiteStore sites, ITenantStore tenants)
    {
        _sites = sites;
        _tenants = tenants;
    }

    public async Task<IReadOnlyList<SiteDto>> GetByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _sites.GetByTenantAsync(tenantId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<SiteDto?> GetAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _sites.GetAsync(tenantId, siteId, cancellationToken);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<SiteDto?> CreateAsync(
        string tenantId,
        string siteId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenants.GetAsync(tenantId, cancellationToken);

        if (tenant == null)
            return null;

        var existing = await _sites.GetAsync(tenantId, siteId, cancellationToken);

        if (existing != null)
            return null;

        var site = new Site(tenantId, siteId, name, description);
        var entity = ToEntity(site);

        await _sites.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<SiteDto?> UpdateAsync(
        string tenantId,
        string siteId,
        string name,
        string? description,
        string status,
        CancellationToken cancellationToken = default)
    {
        var entity = await _sites.GetAsync(tenantId, siteId, cancellationToken);

        if (entity == null)
            return null;

        var site = ToDomain(entity);
        site.Update(name, description);
        site.SetStatus(Enum.Parse<SiteStatus>(status));

        var updated = ToEntity(site);
        updated.ETag = entity.ETag;

        await _sites.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    private static Site ToDomain(SiteEntity entity)
    {
        return Site.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.Name,
            entity.Description,
            Enum.Parse<SiteStatus>(entity.Status),
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }

    private static SiteEntity ToEntity(Site site)
    {
        return new SiteEntity
        {
            PartitionKey = site.TenantId,
            RowKey = site.SiteId,
            TenantId = site.TenantId,
            SiteId = site.SiteId,
            Name = site.Name,
            Description = site.Description,
            Status = site.Status.ToString(),
            CreatedUtc = site.CreatedUtc,
            UpdatedUtc = site.UpdatedUtc
        };
    }

    private static SiteDto ToDto(SiteEntity entity)
    {
        return new SiteDto(
            entity.TenantId,
            entity.SiteId,
            entity.Name,
            entity.Description,
            entity.Status,
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }
}
