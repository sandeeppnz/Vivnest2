namespace Vivnest.Core.Models;

public abstract class BaseIdentity
{
    public required string TenantId { get; init; }

    public required string SiteId { get; init; }

    public required string AgentId { get; init; }
}
