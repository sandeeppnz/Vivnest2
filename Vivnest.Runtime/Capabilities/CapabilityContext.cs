using Microsoft.Extensions.Configuration;
using Vivnest.Core.Capabilities;

namespace Vivnest.Runtime.Capabilities;

// Constructed by CapabilityHost, once per capability startup (ADR-101).
// Deliberately NOT a DI singleton any more: a singleton cannot carry
// camera.capture's assignment and motion.sensor's assignment at the same
// time, and the moment two capabilities are enabled a shared context is
// either wrong for one of them or empty for both.
//
// Its lifetime is a capability's StartAsync..StopAsync, which is the
// host's to manage - not the container's.
public sealed class CapabilityContext : ICapabilityContext
{
    public CapabilityContext(
        IConfiguration configuration,
        IServiceProvider services,
        RuntimeCapabilityAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(assignment);

        Configuration = configuration;
        Services = services;
        Assignment = assignment;

        AgentId =
            configuration["Agent:AgentId"]
            ?? throw new InvalidOperationException(
                "Agent:AgentId is not configured.");

        TenantId = configuration["Agent:TenantId"];
        SiteId = configuration["Agent:SiteId"];
    }

    public string AgentId { get; }

    public string? TenantId { get; }

    public string? SiteId { get; }

    public IServiceProvider Services { get; }

    public IConfiguration Configuration { get; }

    public RuntimeCapabilityAssignment Assignment { get; }
}
