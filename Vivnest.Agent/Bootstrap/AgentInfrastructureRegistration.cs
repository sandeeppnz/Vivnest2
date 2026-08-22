using Microsoft.Extensions.DependencyInjection;
using Vivnest.Infrastructure.DependencyInjection;
using Vivnest.Abstraction.Agent.Events;
using Vivnest.Runtime.Events;
using Vivnest.Runtime.Capabilities;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Agent.Bootstrap;

public static class AgentInfrastructureRegistration
{
    public static IServiceCollection AddAgentInfrastructure(
        this IServiceCollection services)
    {
        services.AddInfrastructure();

        services.AddSingleton<
            IEventDispatcher,
            EventDispatcher>();

        services.AddSingleton<
            ICapabilityRegistry,
            CapabilityRegistry>();

        // No ICapabilityContext registration (ADR-101). A context carries
        // one capability's assignment, so it cannot be shared; CapabilityHost
        // constructs one per capability at startup.

        services.AddSingleton<
            CapabilityHost>();

        services.AddHostedService<
            CapabilityHostedService>();

        return services;
    }
}