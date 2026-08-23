using Microsoft.Extensions.DependencyInjection;
using Vivnest.Core.Commands;
using Vivnest.Core.Events;
using Vivnest.Core.Runtime;
using Vivnest.Capabilities.Bridges.HomeAssistant;
using Vivnest.Capabilities.DeviceHealth;
using Vivnest.Agent.Commands;
using Vivnest.Agent.Shell;
using Vivnest.Runtime.State;
using Vivnest.Runtime.Commands;

namespace Vivnest.Agent.Bootstrap;

public static class AgentPlatformRegistration
{
    public static IServiceCollection AddAgentPlatform(
        this IServiceCollection services)
    {
        // Agent lifecycle / observability

        services.AddSingleton<
            IEventHandler<AgentHeartbeatGeneratedEvent>,
            AgentHeartbeatHandler>();

        services.AddSingleton<
            IEventHandler<DeviceHeartbeatGeneratedEvent>,
            DeviceHeartbeatHandler>();

        services.AddSingleton<
            IEventHandler<AgentMetricsSampledEvent>,
            AgentMetricsHandler>();

        services.AddSingleton<
            IDeviceRuntimeStateStore,
            DeviceRuntimeStateStore>();

        services.AddSingleton<
            IOfflineDetection,
            OfflineDetection>();


        // Agent commands

        services.AddSingleton<
            ICommandHandler,
            RefreshConfigurationCommandHandler>();

        services.AddSingleton<
            ICommandHandler,
            ApplyConfigurationCommandHandler>();

        services.AddSingleton<
            ICommandHandler,
            ExecuteCapabilityCommandHandler>();


        // Agent-level state

        services.AddSingleton<
            IHomeAssistantConnectionTracker,
            HomeAssistantConnectionTracker>();

        services.AddSingleton<
            INetworkUsageTracker,
            NetworkUsageTracker>();


        // Agent platform workers

        services.AddHostedService<
            PlatformAgentHeartbeatWorker>();

        services.AddHostedService<
            PlatformDeviceHeartbeatWorker>();

        services.AddHostedService<
            PlatformAgentMetricsWorker>();

        services.AddHostedService<
            PlatformCommandPollingWorker>();

        services.AddHostedService<
            PlatformAgentCommandPollingWorker>();

        services.AddHostedService<
            PlatformLogShippingWorker>();

        services.AddHostedService<
            PlatformErrorEventWorker>();

        return services;
    }
}
