using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Updater;
using Vivnest.Core.Options;

// Deliberately a separate, standalone process from Vivnest.Agent - never
// runs inside the Agent's container. It needs Docker access to pull and
// recreate the Agent container, which the Agent container itself is
// deliberately refused (ADR-020) - so this runs directly on the host,
// deployed into the same folder as the Agent's own appsettings.json
// (ADR-028).
//
// Its own config file is named updater.settings.json, not
// appsettings.json - both executables land in the same host folder
// (C:\vivnest-agent), and Host.CreateApplicationBuilder's default
// "appsettings.json" convention would otherwise collide with (and risk
// overwriting) the Agent's real, secret-bearing config file whenever this
// project's publish output is copied in.
var builder = Host.CreateApplicationBuilder(args);

// Inserted before the environment-variables source, not just appended -
// same convention TryLoadRemoteConfigAsync uses on the Agent side, so an
// env var override (e.g. a Scheduled Task setting Messaging__ConnectionString)
// still wins over this file, same as it already wins over appsettings.json
// on every other host in this codebase.
{
    var sources = builder.Configuration.Sources;
    var envVarsSourceIndex = -1;

    for (var i = 0; i < sources.Count; i++)
    {
        if (sources[i] is EnvironmentVariablesConfigurationSource)
        {
            envVarsSourceIndex = i;
            break;
        }
    }

    var updaterSettingsSource = new JsonConfigurationSource
    {
        Path = "updater.settings.json",
        Optional = true,
        ReloadOnChange = false,
    };

    if (envVarsSourceIndex >= 0)
        sources.Insert(envVarsSourceIndex, updaterSettingsSource);
    else
        sources.Add(updaterSettingsSource);
}

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

builder.Services.Configure<MessagingOptions>(
    builder.Configuration.GetSection("Messaging"));

builder.Services.Configure<DeployOptions>(
    builder.Configuration.GetSection("Deploy"));

builder.Services.AddSingleton(sp =>
{
    var options = sp
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value;

    return new QueueServiceClient(options.ConnectionString);
});

builder.Services.AddSingleton<AgentDeployer>();
builder.Services.AddHostedService<DeployPollingWorker>();

var app = builder.Build();

// --install: a one-time, locally-triggered deploy before falling through
// to the normal queue-polling service - covers the gap the queue-driven
// path can't (AgentsFunction's DeployAgent endpoint 404s for an agent
// Cloud has never seen a heartbeat from, so a brand-new agent can't be
// bootstrapped that way). No AgentId filtering needed here, unlike a
// queue message - running this flag locally on a host already implies
// "this Updater instance's own agent," by construction.
if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var deployer = app.Services.GetRequiredService<AgentDeployer>();

    logger.LogInformation("--install: running one-time deploy before starting the update service.");
    await deployer.DeployAsync(CancellationToken.None);
    logger.LogInformation("--install complete.");
}

await app.RunAsync();
