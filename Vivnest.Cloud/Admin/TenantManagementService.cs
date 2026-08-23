using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Tenants;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Tenant domain model (Vivnest.Core.Domain)
// to/from TenantEntity for storage - Tenant itself never touches
// ITableEntity/Azure.Data.Tables. No hard delete here (unlike Capability/
// AgentRegistry/DeviceRegistry): a Tenant is the top-level ownership
// boundary everything else scopes under, not disposable reference data -
// deactivate via UpdateAsync(status: Inactive) instead.
public sealed class TenantManagementService : ITenantManagementService
{
    private readonly ITenantStore _tenants;

    public TenantManagementService(ITenantStore tenants)
    {
        _tenants = tenants;
    }

    public async Task<IReadOnlyList<TenantDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _tenants.ListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<TenantDto?> GetAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _tenants.GetAsync(tenantId, cancellationToken);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<TenantDto> CreateAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var tenant = new Tenant(name, description);
        var entity = ToEntity(tenant);

        await _tenants.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<TenantDto?> UpdateAsync(
        string tenantId,
        string name,
        string? description,
        string status,
        CancellationToken cancellationToken = default)
    {
        var entity = await _tenants.GetAsync(tenantId, cancellationToken);

        if (entity == null)
            return null;

        var tenant = ToDomain(entity);
        tenant.Update(name, description);
        tenant.SetStatus(Enum.Parse<TenantStatus>(status));

        var updated = ToEntity(tenant);
        updated.ETag = entity.ETag;

        await _tenants.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    public async Task<TenantDto?> DeactivateAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _tenants.GetAsync(tenantId, cancellationToken);

        if (entity == null)
            return null;

        var tenant = ToDomain(entity);
        tenant.SetStatus(TenantStatus.Inactive);

        var updated = ToEntity(tenant);
        updated.ETag = entity.ETag;

        await _tenants.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    private static Tenant ToDomain(TenantEntity entity)
    {
        return Tenant.Rehydrate(
            entity.RowKey,
            entity.Name,
            entity.Description,
            Enum.Parse<TenantStatus>(entity.Status),
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }

    private static TenantEntity ToEntity(Tenant tenant)
    {
        return new TenantEntity
        {
            RowKey = tenant.TenantId,
            Name = tenant.Name,
            Description = tenant.Description,
            Status = tenant.Status.ToString(),
            CreatedUtc = tenant.CreatedUtc,
            UpdatedUtc = tenant.UpdatedUtc
        };
    }

    private static TenantDto ToDto(TenantEntity entity)
    {
        return new TenantDto(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.Description,
            entity.Status,
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }
}
