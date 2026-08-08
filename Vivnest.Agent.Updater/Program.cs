using System.Text.Json;
using System.Text.Json.Nodes;
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

// --agent/--container/--connectionstring: writes updater.settings.json
// from the command line instead of requiring it to be hand-edited first -
// see decision-log.md ADR-035's follow-up. Applied before
// Host.CreateApplicationBuilder reads the file, so the same run picks up
// the values too, not just future ones.
ApplySettingsOverridesFromArgs(args);

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

// Patches (or creates) updater.settings.json's Agent:AgentId,
// Deploy:ContainerName, and Messaging:ConnectionString from
// --agent/--container/--connectionstring, leaving every other setting
// (PollInterval, DeployCommandQueue, Logging) untouched if the file
// already exists. A no-op if none of the three flags are present, so
// this is safe to call unconditionally regardless of --install.
//
// Trade-off worth knowing: this reads/writes via System.Text.Json.Nodes,
// which has no concept of comments - a hand-commented
// updater.settings.json (like the checked-in template, with both
// agents' values present but commented out) loses those comments the
// first time this runs. Accepted deliberately: the whole point of these
// flags is not needing to hand-edit the file at all going forward.
static void ApplySettingsOverridesFromArgs(string[] args)
{
    var agentId = GetArgValue(args, "--agent");
    var containerName = GetArgValue(args, "--container");
    var connectionString = GetArgValue(args, "--connectionstring");

    if (agentId is null && containerName is null && connectionString is null)
        return;

    var path = Path.Combine(Directory.GetCurrentDirectory(), "updater.settings.json");

    // CommentHandling/AllowTrailingCommas match how
    // Microsoft.Extensions.Configuration.Json itself reads this file -
    // without this, parsing the checked-in commented template throws.
    var readOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    JsonObject root;

    if (File.Exists(path))
    {
        root = JsonNode.Parse(File.ReadAllText(path), documentOptions: readOptions) as JsonObject
            ?? new JsonObject();
    }
    else
    {
        // Same defaults as the checked-in template, so a from-scratch
        // --install --agent ... --container ... --connectionstring ...
        // on a brand-new host produces a complete, working file, not
        // just the three overridden fields.
        root = new JsonObject
        {
            ["Messaging"] = new JsonObject { ["DeployCommandQueue"] = "agent-deploy-commands" },
            ["Deploy"] = new JsonObject { ["PollInterval"] = "00:00:30" },
            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject
                {
                    ["Default"] = "Information",
                    ["Microsoft.Hosting.Lifetime"] = "Information",
                },
            },
        };
    }

    if (agentId is not null)
    {
        var agent = root["Agent"] as JsonObject ?? new JsonObject();
        agent["AgentId"] = agentId;
        root["Agent"] = agent;
    }

    if (containerName is not null)
    {
        var deploy = root["Deploy"] as JsonObject ?? new JsonObject();
        deploy["ContainerName"] = containerName;
        root["Deploy"] = deploy;
    }

    if (connectionString is not null)
    {
        var messaging = root["Messaging"] as JsonObject ?? new JsonObject();
        messaging["ConnectionString"] = connectionString;
        root["Messaging"] = messaging;
    }

    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"[Startup] Updated {path} from command-line arguments.");
}

static string? GetArgValue(string[] args, string flag)
{
    var index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
