using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin;

public interface ITenantManagementService
{
    Task<IReadOnlyList<TenantDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<TenantDto?> GetAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    // Returns null if tenantId already exists - a genuine conflict, not a
    // silent overwrite, since TenantId here is a caller-chosen id used
    // directly as the row key (unlike Capability/DeviceType/etc., whose
    // ids are always a fresh Guid and can't collide).
    Task<TenantDto?> CreateAsync(
        string tenantId,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    Task<TenantDto?> UpdateAsync(
        string tenantId,
        string name,
        string? description,
        string status,
        CancellationToken cancellationToken = default);

    // Soft delete - flips Status to Inactive, leaves everything else
    // untouched. No hard delete exists (see class-level comment on
    // TenantManagementService for why).
    Task<TenantDto?> DeactivateAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
