using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vivnest.Agent.Shell;
using Vivnest.Core.Options;
using Vivnest.Core.Runtime;
using Vivnest.Agent.Configuration;

namespace Vivnest.Agent.Bootstrap;

public static class AgentLoggingRegistration
{
    public static IServiceCollection AddAgentLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging)
    {
        var options =
            new AgentLogShippingOptions();

        configuration
            .GetSection("AgentLogShipping")
            .Bind(options);

        services.Configure<AgentLogShippingOptions>(
            configuration.GetSection(
                "AgentLogShipping"));

        var agentLogBuffer =
            new AgentLogBuffer(
                options.MaxBufferedLines);

        services.AddSingleton<IAgentLogBuffer>(
            agentLogBuffer);

        var agentErrorSignalBuffer =
            new AgentErrorSignalBuffer(
                maxSignals: 200);

        services.AddSingleton<IAgentErrorSignalBuffer>(
            agentErrorSignalBuffer);

        if (options.Enabled)
        {
            logging.AddProvider(
                new AgentLogBufferLoggerProvider(
                    agentLogBuffer,
                    options.MinimumLevel,
                    agentErrorSignalBuffer));
        }
        else
        {
            logging.AddProvider(
                new AgentLogBufferLoggerProvider(
                    new AgentLogBuffer(maxLines: 1),
                    LogLevel.Error,
                    agentErrorSignalBuffer));
        }

        return services;
    }
}
