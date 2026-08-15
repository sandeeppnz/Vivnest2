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
using Vivnest.Agent.Runtime.Configuration;
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
// Shared (shared-config/common-config.json, ADR-037) holds values
// genuinely identical across every agent (Tables, common Messaging
// queues, heartbeat/metrics toggles) - loaded first so the per-agent blob
// (agent-config/{agentId}.json - see decision-log.md) can still override
// a shared value if ever needed, though nothing does today. Read
// bootstrap-only, before either source is added: AgentId,
// Storage:ConnectionString, and LoadLocalSettings must come from local
// config/env vars alone, since they're what's needed to find the local
// files or reach the remote blobs in the first place. Best-effort and
// additive, not required - if a file/blob doesn't exist, or a load fails
// for any reason, the agent proceeds on whatever's already loaded + each
// Options class's own code-level defaults, exactly as it always has.
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

// Secrets are local-only regardless of LoadLocalSettings (ADR-038) -
// credentials never travel through git (the public files above are
// git-tracked) or Azure Blob Storage (there is no remote secrets blob at
// all). Loaded unconditionally, right after the block above, since there's
// no key overlap with the public files - each *.secrets.json supplies
// only the sensitive leaves its public sibling no longer carries.
TryLoadLocalSharedSecrets(builder.Configuration);
TryLoadLocalAgentSecrets(builder.Configuration, builder.Configuration["Agent:AgentId"] ?? "");

// Read raw, same as Agent:AgentId above - decides which capability
// registrations follow, before any typed IOptions<AgentOptions> is
// resolvable. Defaults to Low so every existing agent config (which
// has no Agent:Type key at all) behaves exactly as before - see
// decision-log.md ADR-035/ADR-044.
var agentType = Enum.TryParse<AgentType>(builder.Configuration["Agent:Type"], out var parsedType)
    ? parsedType
    : AgentType.Low;

// Low-type only (ADR-036): Devices[] no longer lives embedded in this
// agent's own config blob - it's assembled from individual blobs in the
// device-config container, filtered to the ones this agent owns. High-type
// agents never consumed Devices at all, so this is skipped entirely for
// them rather than making a pointless container-listing round trip.
if (agentType == AgentType.Low)
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

// High-type only in practice (ADR-035's follow-up) - harmless to bind
// unconditionally like every other Configure<T> call here, since nothing
// on a Low-type agent reads it.
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

// Shared by both types (ADR-035) - generic agent lifecycle/observability,
// not tied to any one capability. DeviceHeartbeatWorker safely no-ops on
// a High-type agent's empty Devices list (confirmed: it's a plain foreach
// over IDeviceRuntimeStore.GetDevices()).
builder.Services.AddSingleton<IEventHandler<AgentHeartbeatGeneratedEvent>, AgentHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<DeviceHeartbeatGeneratedEvent>, DeviceHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<AgentMetricsSampledEvent>, AgentMetricsHandler>();
builder.Services.AddSingleton<ICaptureStatusStore, CaptureStatusStore>();
builder.Services.AddSingleton<IOfflineDetection, OfflineDetection>();

// AgentHeartbeatWorker (shared, both types) depends on this to populate
// HomeAssistatLastConnectedUtc - a trivial, dependency-free state holder
// (a locked nullable DateTime), so it's cheap and harmless to register
// unconditionally too, even though only HomeAssistantWorker (Low-only)
// ever calls MarkConnected() on it. On a High-type agent nothing ever
// marks it connected, so LastConnectedUtc correctly stays null forever -
// exactly right for an agent with no Home Assistant integration. Found
// live: this was Low-only at first, which crashed AgentHeartbeatWorker
// on startup for every High-type agent (DI couldn't resolve the dependency).
builder.Services.AddSingleton<IHomeAssistantConnectionTracker, HomeAssistantConnectionTracker>();

// Same reasoning as IHomeAssistantConnectionTracker just above -
// AgentMetricsWorker (shared) depends on this; a trivial
// Interlocked-backed counter with no dependencies of its own, so cheap
// and harmless to register unconditionally even though only
// Low-type upload paths ever call AddBytesUploaded(). A High-type
// agent doesn't upload photos, so TakeBytesUploaded() correctly reports
// 0 - not a missing feature, an honest reading. Also found live, same
// startup-crash pattern as the HomeAssistant one above.
builder.Services.AddSingleton<INetworkUsageTracker, NetworkUsageTracker>();

builder.Services.AddHostedService<AgentHeartbeatWorker>();
builder.Services.AddHostedService<DeviceHeartbeatWorker>();
builder.Services.AddHostedService<AgentMetricsWorker>();
builder.Services.AddHostedService<CommandPollingWorker>();
builder.Services.AddHostedService<LogShippingWorker>();

if (agentType == AgentType.Low)
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

if (agentType == AgentType.High)
{
    // The whole point of the split (ADR-035): only a High-type agent
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

// Low-type only (ADR-036). Lists every blob in device-config, keeps
// only the ones whose OwningAgentId matches this agent, and merges the
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

            if (deviceNode is not JsonObject deviceObjectRaw)
            {
                Console.WriteLine($"[Startup] Device config blob {blobName} is not a JSON object, skipping.");
                continue;
            }

            // Translates the new capabilities[]-shaped document
            // (decision-log.md ADR-064) into the legacy flat DeviceOptions
            // shape everything below already expects - a no-op for any
            // blob still in the legacy shape.
            var deviceObject = DeviceConfigRuntimeAdapter.Adapt(deviceObjectRaw);

            var owningAgentId = deviceObject["OwningAgentId"]?.GetValue<string>();

            if (!string.Equals(owningAgentId, agentId, StringComparison.Ordinal))
                continue;

            TryMergeLocalDeviceSecrets(deviceObject);

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

// Device secrets are local-only, unlike everything else in
// TryLoadRemoteDeviceConfigsAsync (ADR-038) - never uploaded to
// device-config, never fetched remotely. Merged into the device object
// assembled from the remote blob before it's added to the Devices array,
// since .NET's cross-provider array merge is by-index, not by-key (the
// reason this array is built in code in the first place, ADR-036) - a
// separately-inserted config source can't target "Settings.Password on
// the third element of Devices" the way it can target
// "HomeAssistant:Password". Same "additive, never required" convention as
// everywhere else here: a missing per-device secrets file just means this
// device keeps whatever Settings the public blob already had (e.g. the
// smart plug, which has no Password field at all and needs no secrets
// file) - it aborts neither this device nor the function.
static void TryMergeLocalDeviceSecrets(JsonObject deviceObject)
{
    var deviceId = deviceObject["DeviceId"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(deviceId))
    {
        Console.WriteLine("[Startup] Device config object has no DeviceId; skipping local secrets merge.");
        return;
    }

    var path = Path.Combine(AppContext.BaseDirectory, "device-config", $"{deviceId}.secrets.json");

    if (!File.Exists(path))
    {
        Console.WriteLine($"[Startup] No local secrets file found for device {deviceId} at {path}; continuing without them.");
        return;
    }

    try
    {
        var secretsBytes = File.ReadAllBytes(path);
        var secretsNode = JsonNode.Parse(secretsBytes);

        if (secretsNode is not JsonObject secretsObject)
        {
            Console.WriteLine($"[Startup] Device secrets file {path} is not a JSON object, skipping.");
            return;
        }

        MergeJsonInto(deviceObject, secretsObject);

        Console.WriteLine($"[Startup] Merged local secrets for device {deviceId} from {path}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load local secrets for device {deviceId} from {path}, continuing without them: {ex.Message}");
    }
}

// Recursively merges source's keys into target, in place - used only for
// device secrets (ADR-038), where a separate config source can't reach
// into an array element. Recurses when both sides already have a
// JsonObject at the same key (so a secrets {"Settings":{"Password":...}}
// adds Password alongside target's existing Settings.Host/Username rather
// than replacing the whole Settings object); otherwise sets the value
// directly. Values are deep-cloned - a JsonNode can only be attached to
// one parent at a time, so assigning sourceValue itself (already attached
// to source) would throw once source goes out of scope expecting sole
// ownership.
static void MergeJsonInto(JsonObject target, JsonObject source)
{
    foreach (var (key, sourceValue) in source)
    {
        if (sourceValue is JsonObject sourceObject && target[key] is JsonObject targetObject)
        {
            MergeJsonInto(targetObject, sourceObject);
        }
        else
        {
            target[key] = sourceValue?.DeepClone();
        }
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

// Local-only, unconditional (ADR-038) - mirrors TryLoadLocalSharedConfig's
// shape exactly, but for the handful of sensitive leaves stripped out of
// common-config.json (currently just Messaging.ConnectionString). No key
// overlap with the public shared config, so this can safely be a separate
// inserted source rather than needing to merge into anything in code.
static void TryLoadLocalSharedSecrets(ConfigurationManager configuration)
{
    var path = Path.Combine(AppContext.BaseDirectory, "common-config.secrets.json");

    if (!File.Exists(path))
    {
        Console.WriteLine($"[Startup] No local shared secrets file found at {path}; continuing without it.");
        return;
    }

    try
    {
        var secretsBytes = File.ReadAllBytes(path);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(secretsBytes) });

        Console.WriteLine($"[Startup] Loaded local shared secrets from {path}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load local shared secrets from {path}, continuing without them: {ex.Message}");
    }
}

// Local-only, unconditional (ADR-038) - the sensitive leaves stripped out
// of this agent's own {agentId}.json (currently HomeAssistant.Password/
// AccessToken on the Low-type agent; the High-type agent has none today, so
// this just no-ops for it). Same reasoning as TryLoadLocalSharedSecrets: no key
// overlap with the public per-agent config, safe as a separate source.
static void TryLoadLocalAgentSecrets(ConfigurationManager configuration, string agentId)
{
    if (string.IsNullOrWhiteSpace(agentId))
    {
        Console.WriteLine("[Startup] Agent:AgentId is not set; skipping local agent secrets load.");
        return;
    }

    var path = Path.Combine(AppContext.BaseDirectory, $"{agentId}.secrets.json");

    if (!File.Exists(path))
    {
        Console.WriteLine($"[Startup] No local secrets file found for agent {agentId} at {path}; continuing without them.");
        return;
    }

    try
    {
        var secretsBytes = File.ReadAllBytes(path);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(secretsBytes) });

        Console.WriteLine($"[Startup] Loaded local secrets for agent {agentId} from {path}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to load local secrets for agent {agentId} from {path}, continuing without them: {ex.Message}");
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