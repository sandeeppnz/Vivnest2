using Microsoft.Extensions.Configuration;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Runtime.Capabilities;

public sealed class CapabilityContext
    : ICapabilityContext
{
    public CapabilityContext(
        IConfiguration configuration,
        IServiceProvider services)
    {
        Configuration = configuration;
        Services = services;

        AgentId =
            configuration["Agent:AgentId"]
            ?? throw new InvalidOperationException(
                "Agent:AgentId is not configured.");

        TenantId =
            configuration["Agent:TenantId"];

        SiteId =
            configuration["Agent:SiteId"];
    }

    public string AgentId { get; }

    public string? TenantId { get; }

    public string? SiteId { get; }

    public IServiceProvider Services { get; }

    public IConfiguration Configuration { get; }
}