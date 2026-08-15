using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

// --installtoken/--registrationurl (decision-log.md ADR-072): self-
// registration, the real answer to "how does a fresh Machine get an
// AgentId without an admin hand-typing it." Runs before
// ApplySettingsOverridesFromArgs below, which it deliberately shares
// updater.settings.json with - registration writes Agent:AgentId/
// Messaging:ConnectionString first (from Cloud's response), then
// ApplySettingsOverridesFromArgs can still layer --container/--acr*
// on top from the same command line, same file, no clobbering (it only
// ever touches keys for flags actually present).
var registration = await TryRegisterFromInstallTokenAsync(args);

// --agent/--container/--connectionstring/--acrusername/--acrpassword:
// writes updater.settings.json from the command line instead of requiring
// it to be hand-edited first - see decision-log.md ADR-035's follow-up
// (the first three flags) and ADR-039 (the ACR credential pair). Applied
// before Host.CreateApplicationBuilder reads the file, so the same run
// picks up the values too, not just future ones.
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

// Decision-log.md ADR-072 - deploys immediately rather than waiting for
// DeployPollingWorker's own poll tick to pick up the queue message
// RegisterAsync already enqueued (which still happens too, redundantly
// but harmlessly - AgentDeployer's pull/stop/rm/run is idempotent). Then
// reports back so Cloud can mark the installation Installed without
// waiting for the first real heartbeat - best-effort, a failure here
// just means the installation catches up to Active on that heartbeat
// instead (HealthMonitorService's own hook covers this identically).
//
// Wrapped in try/catch deliberately - unlike --install below (an
// attended, run-once operator gesture where a hard failure is a useful,
// visible signal), this runs as part of an unattended bootstrap. If
// Docker isn't up yet or a pull transiently fails, crashing the whole
// Updater process here would be strictly worse than letting it fall
// through to normal queue-polling, which already has the exact same
// deploy command queued and will retry it on its own next tick.
if (registration != null)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var deployer = app.Services.GetRequiredService<AgentDeployer>();

    logger.LogInformation("Registered via install token; running immediate deploy before starting the update service.");

    try
    {
        await deployer.DeployAsync(CancellationToken.None);
        logger.LogInformation("Registration deploy complete.");

        await TryReportDeployCompleteAsync(registration, logger);
    }
    catch (Exception ex)
    {
        logger.LogWarning(
            ex,
            "Immediate post-registration deploy failed; the queued deploy command will retry it on the next poll tick.");
    }
}

await app.RunAsync();

// Decision-log.md ADR-072 - the self-registration handshake. Returns null
// (and leaves every existing config file untouched) whenever --installtoken
// wasn't passed, so this is a no-op for every deployment that doesn't use
// it - same "additive, opt-in" convention every other flag here follows.
// A plain HttpClient, not the DI-managed HttpClientFactory pattern - this
// runs before the host is even built, deliberately outside DI, same as
// ApplySettingsOverridesFromArgs's own direct file I/O below.
static async Task<RegistrationBootstrap?> TryRegisterFromInstallTokenAsync(string[] args)
{
    var installToken = GetArgValue(args, "--installtoken");

    if (installToken is null)
        return null;

    var registrationUrl = GetArgValue(args, "--registrationurl");

    if (registrationUrl is null)
    {
        Console.WriteLine("[Startup] --installtoken was passed without --registrationurl; skipping self-registration.");
        return null;
    }

    using var http = new HttpClient();

    RegisterInstallationResponse? response;

    try
    {
        var httpResponse = await http.PostAsJsonAsync(
            $"{registrationUrl.TrimEnd('/')}/api/agent-installations-admin/register",
            new RegisterInstallationRequestBody(installToken));

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"[Startup] Registration failed ({(int)httpResponse.StatusCode}): {body}");
            return null;
        }

        response = await httpResponse.Content.ReadFromJsonAsync<RegisterInstallationResponse>();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Registration request failed: {ex.Message}");
        return null;
    }

    if (response is null)
    {
        Console.WriteLine("[Startup] Registration response was empty; skipping self-registration.");
        return null;
    }

    // The Updater's own filter (DeployPollingWorker matching
    // command.AgentId against Agent:AgentId) and the Agent container's own
    // identity both need the freshly assigned RuntimeAgentId - two
    // separate files, same value, same reasoning
    // ApplySettingsOverridesFromArgs's own comment already gives for why
    // they're separate files at all.
    WriteUpdaterSettingsFromRegistration(response);
    WriteAgentAppSettingsFromRegistration(response);

    Console.WriteLine(
        $"[Startup] Registered as RuntimeAgentId {response.RuntimeAgentId} (installation {response.InstallationId}).");

    return new RegistrationBootstrap(
        response.InstallationId, response.TenantId, response.SiteId, registrationUrl);
}

static void WriteUpdaterSettingsFromRegistration(RegisterInstallationResponse response)
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "updater.settings.json");

    var readOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    var root = File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path), documentOptions: readOptions) as JsonObject ?? new JsonObject()
        : new JsonObject
        {
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

    var agent = root["Agent"] as JsonObject ?? new JsonObject();
    agent["AgentId"] = response.RuntimeAgentId;
    root["Agent"] = agent;

    var messaging = root["Messaging"] as JsonObject ?? new JsonObject();
    messaging["ConnectionString"] = response.StorageConnectionString;
    messaging["DeployCommandQueue"] = "agent-deploy-commands";
    root["Messaging"] = messaging;

    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

// The local appsettings.json AgentDeployer mounts into the Agent
// container itself (distinct from this process's own updater.settings.json) -
// the real Vivnest.Agent process reads Agent:TenantId/SiteId/AgentId and
// Storage:ConnectionString from exactly this file today (hand-typed until
// now). Same read-or-default-then-patch shape as
// WriteUpdaterSettingsFromRegistration, deliberately not shared code - the
// two files' default shapes are different enough (Storage vs Messaging
// section, no Deploy section here at all) that forcing one helper to
// handle both would need more branching than just having two.
static void WriteAgentAppSettingsFromRegistration(RegisterInstallationResponse response)
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

    var readOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    var root = File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path), documentOptions: readOptions) as JsonObject ?? new JsonObject()
        : new JsonObject
        {
            ["LoadLocalSettings"] = false,
            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject
                {
                    ["Default"] = "Information",
                    ["Microsoft.Hosting.Lifetime"] = "Information",
                },
            },
        };

    var agent = root["Agent"] as JsonObject ?? new JsonObject();
    agent["TenantId"] = response.TenantId;
    agent["SiteId"] = response.SiteId;
    agent["AgentId"] = response.RuntimeAgentId;
    root["Agent"] = agent;

    var storage = root["Storage"] as JsonObject ?? new JsonObject();
    storage["ConnectionString"] = response.StorageConnectionString;
    root["Storage"] = storage;

    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

// Best-effort, matching TryEnqueueRestartAsync's own convention on the
// Cloud side (decision-log.md ADR-068) - a callback failure here must
// never be treated as the deploy itself having failed.
static async Task TryReportDeployCompleteAsync(RegistrationBootstrap registration, ILogger logger)
{
    try
    {
        using var http = new HttpClient();

        var httpResponse = await http.PostAsJsonAsync(
            $"{registration.RegistrationUrl.TrimEnd('/')}/api/agent-installations-admin/{registration.InstallationId}/deploy-complete",
            new ReportDeployCompleteBody(registration.TenantId, registration.SiteId));

        if (!httpResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "deploy-complete callback returned {StatusCode}.", (int)httpResponse.StatusCode);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to report deploy-complete.");
    }
}

// Patches (or creates) updater.settings.json's Agent:AgentId,
// Deploy:ContainerName, Messaging:ConnectionString, and
// Deploy:AcrUsername/AcrPassword from
// --agent/--container/--connectionstring/--acrusername/--acrpassword,
// leaving every other setting (PollInterval, DeployCommandQueue, Logging)
// untouched if the file already exists. A no-op if none of the flags are
// present, so this is safe to call unconditionally regardless of
// --install.
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
    var acrUsername = GetArgValue(args, "--acrusername");
    var acrPassword = GetArgValue(args, "--acrpassword");

    if (agentId is null && containerName is null && connectionString is null &&
        acrUsername is null && acrPassword is null)
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

    if (acrUsername is not null)
    {
        var deploy = root["Deploy"] as JsonObject ?? new JsonObject();
        deploy["AcrUsername"] = acrUsername;
        root["Deploy"] = deploy;
    }

    if (acrPassword is not null)
    {
        var deploy = root["Deploy"] as JsonObject ?? new JsonObject();
        deploy["AcrPassword"] = acrPassword;
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

// Mirrors Vivnest.Cloud.Api.Dtos.RegisterInstallationRequest/
// AgentRegistrationResult/ReportDeployCompleteRequest exactly (this
// project doesn't reference Vivnest.Cloud, so these are deliberately
// duplicated wire-shape records rather than a shared assembly reference
// pulled in just for four field names). Type declarations must trail
// every top-level statement/local function in this file (CS8803), hence
// living all the way down here rather than near the functions that use
// them.
internal sealed record RegisterInstallationRequestBody(string InstallToken);

internal sealed record RegisterInstallationResponse(
    string RuntimeAgentId,
    string InstallationId,
    string TenantId,
    string SiteId,
    string? ImageVersion,
    string StorageConnectionString);

internal sealed record ReportDeployCompleteBody(string TenantId, string SiteId);

internal sealed record RegistrationBootstrap(
    string InstallationId, string TenantId, string SiteId, string RegistrationUrl);
