using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin.Interfaces;

public interface ITenantManagementService
{
    Task<IReadOnlyList<TenantDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<TenantDto?> GetAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    // TenantId is a generated Guid - no collision possible, so no
    // nullable/conflict return here (same reasoning as
    // IAgentRegistryManagementService.CreateAsync).
    Task<TenantDto> CreateAsync(
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
