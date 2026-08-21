namespace Vivnest.Abstractions.Data;

public abstract class BaseEntity : ISiteScoped
{
    public required string TenantId { get; init; }

    public required string SiteId { get; init; }


}
