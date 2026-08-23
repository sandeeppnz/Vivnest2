using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.Commands;
using Vivnest.Core.Events;
using Vivnest.Core.Runtime;
using Vivnest.Capabilities.Bridges.HomeAssistant;
using Vivnest.Agent.Shell.DeviceHealth;
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


        // Cloud API client, shared by both command-polling workers.
        services.AddHttpClient(
            CloudApiHttpClient.Name,
            (sp, http) =>
            {
                var agent = sp
                    .GetRequiredService<IOptions<AgentOptions>>()
                    .Value;

                if (!string.IsNullOrWhiteSpace(agent.ApiKey))
                    http.DefaultRequestHeaders.Add("x-api-key", agent.ApiKey);
            });


        // Agent platform workers

        services.AddHostedService<
            AgentHeartbeatWorker>();

        services.AddHostedService<
            DeviceHeartbeatWorker>();

        services.AddHostedService<
            AgentMetricsWorker>();

        services.AddHostedService<
            CommandPollingWorker>();

        services.AddHostedService<
            AgentCommandPollingWorker>();

        services.AddHostedService<
            LogShippingWorker>();

        services.AddHostedService<
            ErrorEventWorker>();

        return services;
    }
}
