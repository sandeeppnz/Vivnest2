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

// Agent/device config: layer either a local file or the remote per-agent
// blob (agent-config/{agentId}.json, written by the dashboard's config
// editor - see decision-log.md) on top of local appsettings.json before
// the rest of the host builds. Read bootstrap-only, before either source
// is added: AgentId, Storage:ConnectionString, and LoadLocalSettings must
// come from local config/env vars alone, since they're what's needed to
// find the local file or reach the remote blob in the first place.
// Best-effort and additive, not required - if no file/blob exists, or the
// load fails for any reason, the agent proceeds on local config + each
// Options class's own code-level defaults alone, exactly as it always has.
if (builder.Configuration.GetValue<bool>("LoadLocalSettings"))
{
    TryLoadLocalConfig(builder.Configuration);
}
else
{
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
            new JsonStreamConfigurationSource { Stream = new MemoryStream(configBytes) });

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
            new JsonStreamConfigurationSource { Stream = new MemoryStream(configBytes) });

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