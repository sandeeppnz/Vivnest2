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
                options.MaxBufferedErrorSignals);

        services.AddSingleton<IAgentErrorSignalBuffer>(
            agentErrorSignalBuffer);

        // A provider is registered either way, because the two things it
        // feeds are configured by one flag but are not the same feature.
        // AgentLogShipping:Enabled turns off SHIPPING - uploading the log
        // blob for a human to download. Operational alerting
        // (ErrorEventWorker turning Error-level calls into AgentEvent rows
        // and notifications) is not log shipping and must survive it being
        // switched off.
        //
        // So when shipping is disabled the provider still runs, but with a
        // one-line throwaway buffer nothing reads and a threshold that
        // admits only what the error path needs. The buffer is a
        // null-object, not a real one; agentLogBuffer above stays
        // registered as the singleton so LogShippingWorker still resolves.
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
