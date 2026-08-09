using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Capabilities.Camera;
using Vivnest.Agent.Capabilities.DeviceHealth;
using Vivnest.Agent.Capabilities.Bridges.HomeAssistant;
using Vivnest.Agent.Capabilities.Bridges.TapoHub;
using Vivnest.Agent.Capabilities.MotionSensor;
using Vivnest.Agent.Capabilities.SmartPlug;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Agent.Runtime.Shell;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DependencyInjection;


var builder = Host.CreateApplicationBuilder(args);

// Agent/device config: layer either local files or remote blobs on top of
// local appsettings.json before the rest of the host builds, in precedence
// order shared -> per-agent -> (below, Capture-only) devices -> env vars.
// Shared (shared-config/config.json, ADR-037) holds values genuinely
// identical across every agent (Tables, common Messaging queues,
// heartbeat/metrics toggles) - loaded first so the per-agent blob
// (agent-config/{agentId}.json, written by the dashboard's config editor -
// see decision-log.md) can still override a shared value if ever needed,
// though nothing does today. Read bootstrap-only, before either source is
// added: AgentId, Storage:ConnectionString, and LoadLocalSettings must
// come from local config/env vars alone, since they're what's needed to
// find the local files or reach the remote blobs in the first place.
// Best-effort and additive, not required - if a file/blob doesn't exist,
// or a load fails for any reason, the agent proceeds on whatever's already
// loaded + each Options class's own code-level defaults, exactly as it
// always has.
if (builder.Configuration.GetValue<bool>("LoadLocalSettings"))
{
    TryLoadLocalSharedConfig(builder.Configuration);
    TryLoadLocalConfig(builder.Configuration);
}
else
{
    await TryLoadRemoteSharedConfigAsync(builder.Configuration);
    await TryLoadRemoteConfigAsync(builder.Configuration);
}

// Read raw, same as Agent:AgentId above - decides which capability
// registrations follow, before any typed IOptions<AgentOptions> is
// resolvable. Defaults to Capture so every existing agent config (which
// has no Agent:Role key at all) behaves exactly as before - see
// decision-log.md ADR-035.
var role = Enum.TryParse<AgentRole>(builder.Configuration["Agent:Role"], out var parsedRole)
    ? parsedRole
    : AgentRole.Capture;

// Capture-role only (ADR-036): Devices[] no longer lives embedded in this
// agent's own config blob - it's assembled from individual blobs in the
// device-config container, filtered to the ones this agent owns. Ai-role
// agents never consumed Devices at all, so this is skipped entirely for
// them rather than making a pointless container-listing round trip.
if (role == AgentRole.Capture)
{
    await TryLoadRemoteDeviceConfigsAsync(
        builder.Configuration,
        builder.Configuration["Agent:AgentId"] ?? "");
}

builder.Services.Configure<MessagingOptions>(
    builder.Configuration.GetSection("Messaging"));

builder.Services.AddSingleton(sp =>
{
    var options = sp
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value;

    return new QueueServiceClient(options.ConnectionString);
});

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

builder.Services.Configure<DevicesOptions>(
    builder.Configuration);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("Agent"));

// Ai-role only in practice (ADR-035's follow-up) - harmless to bind
// unconditionally like every other Configure<T> call here, since nothing
// on a Capture-role agent reads it.
builder.Services.Configure<AiClassificationOptions>(
    builder.Configuration.GetSection("AiClassification"));

builder.Services.Configure<AgentHeartbeatOptions>(
    builder.Configuration.GetSection("AgentHeartbeat"));

builder.Services.Configure<TablesOptions>(
    builder.Configuration.GetSection("Tables"));


builder.Services.Configure<DeviceEventOptions>(
    builder.Configuration.GetSection("DeviceEvents"));

builder.Services.Configure<AgentEventOptions>(
    builder.Configuration.GetSection("AgentEvents"));

builder.Services.Configure<AgentMetricsOptions>(
    builder.Configuration.GetSection("AgentMetrics"));

builder.Services.Configure<DeviceHeartbeatOptions>(
    builder.Configuration.GetSection("DeviceHeartbeat"));

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));

var logShippingOptions = new AgentLogShippingOptions();
builder.Configuration.GetSection("AgentLogShipping").Bind(logShippingOptions);
builder.Services.Configure<AgentLogShippingOptions>(
    builder.Configuration.GetSection("AgentLogShipping"));

// Constructed before the host builds, then registered as the same
// singleton instance - the logger provider needs it immediately (loggers
// get created as soon as the host starts composing), and LogShippingWorker
// must read from exactly what the provider wrote to.
var agentLogBuffer = new AgentLogBuffer(logShippingOptions.MaxBufferedLines);
builder.Services.AddSingleton<IAgentLogBuffer>(agentLogBuffer);

if (logShippingOptions.Enabled)
{
    builder.Logging.AddProvider(
        new AgentLogBufferLoggerProvider(agentLogBuffer, logShippingOptions.MinimumLevel));
}


builder.Services.AddInfrastructure();

builder.Services.AddSingleton<IEventDispatcher, EventDispatcher>();

// Shared by both roles (ADR-035) - generic agent lifecycle/observability,
// not tied to any one capability. DeviceHeartbeatWorker safely no-ops on
// an Ai-role agent's empty Devices list (confirmed: it's a plain foreach
// over IDeviceRuntimeStore.GetDevices()).
builder.Services.AddSingleton<IEventHandler<AgentHeartbeatGeneratedEvent>, AgentHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<DeviceHeartbeatGeneratedEvent>, DeviceHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<AgentMetricsSampledEvent>, AgentMetricsHandler>();
builder.Services.AddSingleton<ICaptureStatusStore, CaptureStatusStore>();
builder.Services.AddSingleton<IOfflineDetection, OfflineDetection>();

// AgentHeartbeatWorker (shared, both roles) depends on this to populate
// HomeAssistatLastConnectedUtc - a trivial, dependency-free state holder
// (a locked nullable DateTime), so it's cheap and harmless to register
// unconditionally too, even though only HomeAssistantWorker (Capture-only)
// ever calls MarkConnected() on it. On an Ai-role agent nothing ever
// marks it connected, so LastConnectedUtc correctly stays null forever -
// exactly right for an agent with no Home Assistant integration. Found
// live: this was Capture-only at first, which crashed AgentHeartbeatWorker
// on startup for every Ai-role agent (DI couldn't resolve the dependency).
builder.Services.AddSingleton<IHomeAssistantConnectionTracker, HomeAssistantConnectionTracker>();

// Same reasoning as IHomeAssistantConnectionTracker just above -
// AgentMetricsWorker (shared) depends on this; a trivial
// Interlocked-backed counter with no dependencies of its own, so cheap
// and harmless to register unconditionally even though only
// Capture-role upload paths ever call AddBytesUploaded(). An Ai-role
// agent doesn't upload photos, so TakeBytesUploaded() correctly reports
// 0 - not a missing feature, an honest reading. Also found live, same
// startup-crash pattern as the HomeAssistant one above.
builder.Services.AddSingleton<INetworkUsageTracker, NetworkUsageTracker>();

builder.Services.AddHostedService<AgentHeartbeatWorker>();
builder.Services.AddHostedService<DeviceHeartbeatWorker>();
builder.Services.AddHostedService<AgentMetricsWorker>();
builder.Services.AddHostedService<CommandPollingWorker>();
builder.Services.AddHostedService<LogShippingWorker>();

if (role == AgentRole.Capture)
{
    builder.Services.AddSingleton<IEventHandler<CameraCaptureCompletedEvent>, CameraCaptureHandler>();
    builder.Services.AddSingleton<IEventHandler<CameraCaptureCompletedEvent>, SinkCleanlinessHandler>();
    builder.Services.AddSingleton<IEventHandler<CameraCaptureFailedEvent>, CameraCaptureFailedHandler>();
    builder.Services.AddSingleton<IEventHandler<SmartPlugReadingCompletedEvent>, SmartPlugReadingHandler>();
    builder.Services.AddSingleton<IEventHandler<SmartPlugReadingFailedEvent>, SmartPlugReadingFailedHandler>();
    builder.Services.AddSingleton<IEventHandler<SmartPlugPowerStateChangedEvent>, SmartPlugPowerStateChangedHandler>();
    builder.Services.AddSingleton<IEventHandler<HomeAssistantStateChangedEvent>, HomeAssistantStateChangedHandler>();
    builder.Services.AddSingleton<IEventHandler<MotionSensorStateChangedEvent>, MotionSensorStateChangedHandler>();
    builder.Services.AddSingleton<IEventHandler<MotionSensorStateChangedEvent>, MotionTriggerResolverHandler>();
    builder.Services.AddSingleton<IEventHandler<MotionSensorReadingFailedEvent>, MotionSensorReadingFailedHandler>();
    builder.Services.AddSingleton<IEventHandler<MotionSensorBatteryReportedEvent>, MotionSensorBatteryHandler>();
    builder.Services.AddSingleton<IEventHandler<DeviceTriggeredEvent>, CaptureOnTriggerHandler>();

    builder.Services.AddSingleton<ICameraCaptureService, CameraCaptureService>();
    builder.Services.AddSingleton<ICameraCaptureExecutor, CameraCaptureExecutor>();
    builder.Services.AddSingleton<ISmartPlugMonitorService, SmartPlugMonitorService>();
    builder.Services.AddSingleton<IMotionSensorMonitorService, MotionSensorMonitorService>();

    builder.Services.AddHttpClient<IHomeAssistantCommandSender, HomeAssistantCommandSender>();
    builder.Services.AddSingleton<IHomeAssistantLivenessTracker, HomeAssistantLivenessTracker>();

    builder.Services.AddSingleton<ITapoHubReachabilityChecker, TapoHubReachabilityChecker>();

    builder.Services.AddHostedService<CameraCaptureWorker>();
    builder.Services.AddHostedService<SmartPlugMonitorWorker>();
    builder.Services.AddHostedService<MotionSensorMonitorWorker>();
    builder.Services.AddHostedService<HomeAssistantWorker>();
    builder.Services.AddHostedService<TapoHubLivenessWorker>();
}

if (role == AgentRole.Ai)
{
    // The whole point of the split (ADR-035): only an Ai-role agent
    // loads either ONNX model. SinkCleanlinessWorker itself now polls
    // MessagingOptions.ClassifyCommandQueue directly instead of draining
    // an in-process Channel<T> - no channel registration here anymore.
    builder.Services.AddSingleton<ISinkCleanlinessClassifier, SinkCleanlinessClassifier>();
    builder.Services.AddSingleton<IObjectDetector, ObjectDetector>();

    builder.Services.AddHostedService<SinkCleanlinessWorker>();
}

var app = builder.Build();

await app.RunAsync();

// Loaded for both roles (ADR-037), unlike the per-agent/device blobs -
// these values (Tables, the common Messaging queues, heartbeat/metrics
// toggles) are genuinely identical across every agent regardless of role,
// so there's exactly one blob to edit instead of one per agent kept in
// sync by hand. Same shape/error-handling as TryLoadRemoteConfigAsync
// below, minus the per-agent id.
static async Task TryLoadRemoteSharedConfigAsync(ConfigurationManager configuration)
{
    var storageConnectionString = configuration["Storage:ConnectionString"];

    if (string.IsNullOrWhiteSpace(storageConnectionString))
    {
        Console.WriteLine("[Startup] Storage:ConnectionString not set; skipping shared config fetch.");
        return;
    }

    try
    {
        var blobClient = new AzureBlobStorageClient(new BlobServiceClient(storageConnectionString));

        var configBytes = await blobClient.DownloadAsync(
            SharedConfigBlob.ContainerName,
            SharedConfigBlob.BlobName);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(configBytes) });

        Console.WriteLine("[Startup] Loaded remote shared config.");
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        Console.WriteLine("[Startup] No remote shared config blob found; using local/per-agent config only.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load remote shared config, continuing without it: {ex.Message}");
    }
}

static async Task TryLoadRemoteConfigAsync(ConfigurationManager configuration)
{
    var agentId = configuration["Agent:AgentId"];
    var storageConnectionString = configuration["Storage:ConnectionString"];

    if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(storageConnectionString))
    {
        Console.WriteLine("[Startup] Agent:AgentId or Storage:ConnectionString not set; skipping remote config fetch.");
        return;
    }

    try
    {
        var blobClient = new AzureBlobStorageClient(new BlobServiceClient(storageConnectionString));

        var configBytes = await blobClient.DownloadAsync(
            AgentConfigBlob.ContainerName,
            AgentConfigBlob.BlobName(agentId));

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(configBytes) });

        Console.WriteLine($"[Startup] Loaded remote config for agent {agentId}.");
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Startup] No remote config blob found for agent {agentId}; using local config only.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load remote config for agent {agentId}, continuing with local config only: {ex.Message}");
    }
}

// Capture-role only (ADR-036). Lists every blob in device-config, keeps
// only the ones whose CaptureAgentId matches this agent, and merges the
// survivors into IConfiguration under the same root "Devices" key the
// existing Configure<DevicesOptions>(builder.Configuration) call already
// binds - so every downstream consumer (all going through
// IDeviceRuntimeStore) needs zero changes. There's no server-side filter on
// the listing call, so every agent downloads every device blob and
// discards what isn't theirs - fine at this project's scale, and the same
// "additive, never required" convention as TryLoadRemoteConfigAsync: a
// missing container means zero devices, not a startup failure, and one bad
// blob is skipped rather than aborting the rest.
static async Task TryLoadRemoteDeviceConfigsAsync(
    ConfigurationManager configuration,
    string agentId)
{
    var storageConnectionString = configuration["Storage:ConnectionString"];

    if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(storageConnectionString))
    {
        Console.WriteLine("[Startup] Agent:AgentId or Storage:ConnectionString not set; skipping device config fetch.");
        return;
    }

    try
    {
        var blobClient = new AzureBlobStorageClient(new BlobServiceClient(storageConnectionString));
        var blobNames = await blobClient.ListBlobNamesAsync(DeviceConfigBlob.ContainerName);
        var devices = new JsonArray();

        foreach (var blobName in blobNames)
        {
            byte[] deviceBytes;

            try
            {
                deviceBytes = await blobClient.DownloadAsync(DeviceConfigBlob.ContainerName, blobName);
            }
            catch (Exception ex)
            {
                // One bad/unreachable device blob must never take every
                // other device down with it.
                Console.WriteLine($"[Startup] Failed to download device config blob {blobName}, skipping: {ex.Message}");
                continue;
            }

            JsonNode? deviceNode;

            try
            {
                deviceNode = JsonNode.Parse(deviceBytes);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Startup] Device config blob {blobName} is not valid JSON, skipping: {ex.Message}");
                continue;
            }

            if (deviceNode is not JsonObject deviceObject)
            {
                Console.WriteLine($"[Startup] Device config blob {blobName} is not a JSON object, skipping.");
                continue;
            }

            var owningAgentId = deviceObject["CaptureAgentId"]?.GetValue<string>();

            if (!string.Equals(owningAgentId, agentId, StringComparison.Ordinal))
                continue;

            devices.Add(deviceObject);
        }

        var root = new JsonObject { ["Devices"] = devices };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(root);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(jsonBytes) });

        Console.WriteLine($"[Startup] Loaded {devices.Count} device config(s) for agent {agentId}.");
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        Console.WriteLine("[Startup] No device-config container/blobs found; agent has zero devices.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load device configs, continuing with zero devices: {ex.Message}");
    }
}

// Dev convenience mirroring TryLoadRemoteSharedConfigAsync - same file
// shape/name (SharedConfigBlob.BlobName) a remote fetch would use, just
// from disk. Not optional dressing: TablesOptions' table-name fields and
// AgentHeartbeatOptions/AgentMetricsOptions/etc.'s Enabled all default to
// empty/false, so without this, trimming the per-agent local files down
// (now that Tables/heartbeats/metrics live here instead) would silently
// disable them in local dev - the exact bug class ADR-037 exists to fix,
// just relocated from the remote blobs to here.
static void TryLoadLocalSharedConfig(ConfigurationManager configuration)
{
    var path = Path.Combine(AppContext.BaseDirectory, SharedConfigBlob.BlobName);

    if (!File.Exists(path))
    {
        Console.WriteLine($"[Startup] LoadLocalSettings is true but no local shared config file found at {path}; skipping.");
        return;
    }

    try
    {
        var configBytes = File.ReadAllBytes(path);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(configBytes) });

        Console.WriteLine($"[Startup] Loaded local shared config from {path}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load local shared config from {path}, continuing without it: {ex.Message}");
    }
}

// Dev convenience: reads the exact same file shape/name a remote config
// blob would use (AgentConfigBlob.BlobName), just from disk next to the
// executable instead of Blob Storage - so a file can be dropped in this
// folder and used directly with no Azure round-trip, without maintaining a
// second config format. Only takes effect when LoadLocalSettings is
// explicitly true; off (remote blob, as before) by default.
static void TryLoadLocalConfig(ConfigurationManager configuration)
{
    var agentId = configuration["Agent:AgentId"];

    if (string.IsNullOrWhiteSpace(agentId))
    {
        Console.WriteLine("[Startup] LoadLocalSettings is true but Agent:AgentId is not set; skipping local config load.");
        return;
    }

    var path = Path.Combine(AppContext.BaseDirectory, AgentConfigBlob.BlobName(agentId));

    if (!File.Exists(path))
    {
        Console.WriteLine($"[Startup] LoadLocalSettings is true but no local config file found at {path}; using local appsettings only.");
        return;
    }

    try
    {
        var configBytes = File.ReadAllBytes(path);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(configBytes) });

        Console.WriteLine($"[Startup] Loaded local config for agent {agentId} from {path}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load local config from {path}, continuing with local appsettings only: {ex.Message}");
    }
}

// Shared by both TryLoadRemoteConfigAsync and TryLoadLocalConfig - insert
// before env vars, not just appended, so env var overrides (e.g. docker
// run -e HomeAssistant__BaseUrl=...) still win over whatever the
// file/blob says, same as they already win over local appsettings.json.
static void InsertConfigSourceBeforeEnvVars(
    ConfigurationManager configuration,
    IConfigurationSource source)
{
    var sources = configuration.Sources;
    var envVarsSourceIndex = -1;

    for (var i = 0; i < sources.Count; i++)
    {
        if (sources[i] is EnvironmentVariablesConfigurationSource)
        {
            envVarsSourceIndex = i;
            break;
        }
    }

    if (envVarsSourceIndex >= 0)
        sources.Insert(envVarsSourceIndex, source);
    else
        sources.Add(source);
}

// ConfigurationManager (unlike a plain ConfigurationBuilder) eagerly
// rebuilds every registered source into a fresh provider on each Sources
// mutation, disposing the providers being replaced - which for an
// ordinary MemoryStream means its second successful insert corrupts the
// first stream-backed source (observed directly: chaining a shared-config
// insert then a per-agent insert threw "Stream was not readable" on the
// second one, ADR-037). Resetting instead of actually closing on Dispose
// makes each stream survive being rebuilt any number of times before
// Build() finally settles.
sealed class ReusableMemoryStream : MemoryStream
{
    public ReusableMemoryStream(byte[] buffer) : base(buffer) { }

    protected override void Dispose(bool disposing)
    {
        Position = 0;
    }
}