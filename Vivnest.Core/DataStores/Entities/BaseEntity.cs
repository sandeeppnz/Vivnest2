namespace Vivnest.Core.DataStores.Entities;

public abstract class BaseEntity
{
    public required string TenantId { get; init; }

    public required string SiteId { get; init; }


}
