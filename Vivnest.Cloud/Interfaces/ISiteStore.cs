using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface ISiteStore
{
    Task<SiteEntity?> GetAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SiteEntity>> GetByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        SiteEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        SiteEntity entity,
        CancellationToken cancellationToken = default);
}
