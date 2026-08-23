using Vivnest.Domain.Shared;

namespace Vivnest.Core.DataStores.Entities;

public abstract class BaseEntity : ISiteScoped
{
    public required string TenantId { get; init; }

    public required string SiteId { get; init; }


}
