namespace Vivnest.Abstraction.Agent.Capabilities;

public interface ICapabilityContext
{
    string AgentId { get; }

    string? TenantId { get; }

    string? SiteId { get; }

    IServiceProvider Services { get; }
}