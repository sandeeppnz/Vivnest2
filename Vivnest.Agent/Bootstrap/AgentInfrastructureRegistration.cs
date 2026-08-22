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

        services.AddSingleton<
            ICapabilityContext,
            CapabilityContext>();

        services.AddSingleton<
            CapabilityHost>();

        services.AddHostedService<
            CapabilityHostedService>();

        return services;
    }
}