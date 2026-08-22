using Microsoft.Extensions.Hosting;
using Vivnest.Core.Enums;

namespace Vivnest.Agent.Bootstrap;

public static class AgentBootstrap
{
    public static async Task ConfigureAsync(
        HostApplicationBuilder builder)
    {
        await AgentConfigurationLoader.LoadAsync(
            builder.Configuration);

        builder.Services.AddAgentOptions(
            builder.Configuration);

        builder.Services.AddAgentLogging(
          builder.Configuration,
          builder.Logging);

        builder.Services.AddAgentInfrastructure();

        // Platform
        builder.Services.AddAgentPlatform();

        // Capabilities
        var agentType =
            Enum.TryParse<AgentType>(
                builder.Configuration["Agent:Type"],
                out var parsedType)
                    ? parsedType
                    : AgentType.Low;

        builder.Services.AddAgentCapabilities(
              agentType);

    }
}
