using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface ITenantStore
{
    Task<IReadOnlyList<TenantEntity>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<TenantEntity?> GetAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        TenantEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TenantEntity entity,
        CancellationToken cancellationToken = default);
}
