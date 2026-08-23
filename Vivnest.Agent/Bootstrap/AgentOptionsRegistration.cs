using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Agent.Configuration;

namespace Vivnest.Agent.Bootstrap;

public static class AgentOptionsRegistration
{
    public static IServiceCollection AddAgentOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MessagingOptions>(
            configuration.GetSection("Messaging"));

        services.AddSingleton(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<MessagingOptions>>()
                .Value;

            return new QueueServiceClient(
                options.ConnectionString);
        });

        services.Configure<StorageOptions>(
            configuration.GetSection("Storage"));

        services.Configure<DevicesOptions>(
            configuration);

        services.Configure<AgentConfigMetadataOptions>(
            configuration);

        services.Configure<AgentOptions>(
            configuration.GetSection("Agent"));

        services.Configure<AiClassificationOptions>(
            configuration.GetSection("AiClassification"));

        services.Configure<AgentHeartbeatOptions>(
            configuration.GetSection("AgentHeartbeat"));

        services.Configure<TablesOptions>(
            configuration.GetSection("Tables"));

        services.Configure<DeviceEventOptions>(
            configuration.GetSection("DeviceEvents"));

        services.Configure<AgentEventOptions>(
            configuration.GetSection("AgentEvents"));

        services.Configure<AgentMetricsOptions>(
            configuration.GetSection("AgentMetrics"));

        services.Configure<DeviceHeartbeatOptions>(
            configuration.GetSection("DeviceHeartbeat"));

        services.Configure<HomeAssistantOptions>(
            configuration.GetSection("HomeAssistant"));

        return services;
    }
}
