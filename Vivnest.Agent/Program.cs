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
using System.Text.Json;
using System.Text.Json.Nodes;
using Vivnest.Abstractions;
using Vivnest.Abstractions.Commands;
using Vivnest.Abstractions.Constants;
using Vivnest.Abstractions.Enums;
using Vivnest.Abstractions.Events;
using Vivnest.Abstractions.Models.Triggers;
using Vivnest.Agent.Capabilities.Bridges.HomeAssistant;
using Vivnest.Agent.Capabilities.Bridges.TapoHub;
using Vivnest.Agent.Capabilities.Camera;
using Vivnest.Agent.Capabilities.DeviceHealth;
using Vivnest.Agent.Capabilities.MotionSensor;
using Vivnest.Agent.Capabilities.SmartPlug;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Agent.Runtime.Commands;
using Vivnest.Agent.Runtime.Shell;
using Vivnest.Capabilities.Camera.Events;
using Vivnest.Capabilities.Camera.Executors;
using Vivnest.Capabilities.Camera.Handlers;
using Vivnest.Capabilities.Camera.Services;
using Vivnest.Capabilities.Camera.Workers;
using Vivnest.Core.Configuration;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Options;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DependencyInjection;
using Vivnest.Runtime.Events;


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
// decision-log.md ADR-086 - read straight off builder.Configuration, same
// tier as Agent:AgentId/Storage:ConnectionString above: appsettings.json
// (and env vars) are already loaded by Host.CreateApplicationBuilder
// before any of this file's code runs, so the key is available before
// the agent-config blob download below with no extra loading step and no
// dependency on load order. Superseded ADR-085's original design (key in
// the local-only common-config.secrets.json, requiring
// TryLoadLocalSharedSecrets to be reordered ahead of the fetch below) -
// see ADR-086 for why.
var credentialEncryptionKey = TryParseCredentialEncryptionKey(builder.Configuration);

if (builder.Configuration.GetValue<bool>("LoadLocalSettings"))
{
    TryLoadLocalSharedConfig(builder.Configuration, credentialEncryptionKey);
    TryLoadLocalConfig(builder.Configuration, credentialEncryptionKey);
}
else
{
    await TryLoadRemoteSharedConfigAsync(builder.Configuration, credentialEncryptionKey);
    await TryLoadRemoteConfigAsync(builder.Configuration, credentialEncryptionKey);
}

// Secrets are local-only regardless of LoadLocalSettings (ADR-038) -
// credentials never travel through git (the public files above are
// git-tracked) or Azure Blob Storage (there is no remote secrets blob at
// all). Loaded unconditionally, right after the block above, since there's
// no key overlap with the public files above - each *.secrets.json supplies
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
        builder.Configuration["Agent:AgentId"] ?? "",
        credentialEncryptionKey);
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

// Root-bound, not section-bound (decision-log.md ADR-065) - matches
// where IAgentRuntimeConfigurationPublisher actually writes
// "ConfigurationPublishedUtc": a top-level key on agent-config/{agentId}.json,
// sibling to "AiClassification", not nested under any section.
builder.Services.Configure<AgentConfigMetadataOptions>(
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
// get created as soon as the host starts composing), and PlatformLogShippingWorker
// must read from exactly what the provider wrote to.
var agentLogBuffer = new AgentLogBuffer(logShippingOptions.MaxBufferedLines);
builder.Services.AddSingleton<IAgentLogBuffer>(agentLogBuffer);

// Sprint 8 - same construct-before-the-host reasoning as the buffer above.
// Independent of LogShipping:Enabled on purpose: shipping logs for a human
// to read and raising errors for something to react to are different
// features, and wanting the second without the first is reasonable.
var agentErrorSignalBuffer = new AgentErrorSignalBuffer(maxSignals: 200);
builder.Services.AddSingleton<IAgentErrorSignalBuffer>(agentErrorSignalBuffer);

if (logShippingOptions.Enabled)
{
    builder.Logging.AddProvider(
        new AgentLogBufferLoggerProvider(
            agentLogBuffer, logShippingOptions.MinimumLevel, agentErrorSignalBuffer));
}
else
{
    // A no-op sink for the log buffer, so Error-level calls are still
    // observed for alerting even with log shipping switched off.
    builder.Logging.AddProvider(
        new AgentLogBufferLoggerProvider(
            new AgentLogBuffer(maxLines: 1), LogLevel.Error, agentErrorSignalBuffer));
}


builder.Services.AddInfrastructure();

builder.Services.AddSingleton<IEventDispatcher, EventDispatcher>();

// Shared by both types (ADR-035) - generic agent lifecycle/observability,
// not tied to any one capability. PlatformDeviceHeartbeatWorker safely no-ops on
// a High-type agent's empty Devices list (confirmed: it's a plain foreach
// over IDeviceRuntimeStore.GetDevices()).
builder.Services.AddSingleton<IEventHandler<AgentHeartbeatGeneratedEvent>, AgentHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<DeviceHeartbeatGeneratedEvent>, DeviceHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<AgentMetricsSampledEvent>, AgentMetricsHandler>();
builder.Services.AddSingleton<IDeviceRuntimeStateStore, DeviceRuntimeStateStore>();
builder.Services.AddSingleton<IOfflineDetection, OfflineDetection>();

// Decision-log.md ADR-080 - shared by both types (Phase 9 Pass 2), same
// reasoning as the shared IEventHandlers above: RefreshConfiguration/
// ApplyConfiguration apply to any Agent's own configuration regardless of
// role.
builder.Services.AddSingleton<ICommandHandler, RefreshConfigurationCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ApplyConfigurationCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ExecuteCapabilityCommandHandler>();

// PlatformAgentHeartbeatWorker (shared, both types) depends on this to populate
// HomeAssistatLastConnectedUtc - a trivial, dependency-free state holder
// (a locked nullable DateTime), so it's cheap and harmless to register
// unconditionally too, even though only HomeAssistantWorker (Low-only)
// ever calls MarkConnected() on it. On a High-type agent nothing ever
// marks it connected, so LastConnectedUtc correctly stays null forever -
// exactly right for an agent with no Home Assistant integration. Found
// live: this was Low-only at first, which crashed PlatformAgentHeartbeatWorker
// on startup for every High-type agent (DI couldn't resolve the dependency).
builder.Services.AddSingleton<IHomeAssistantConnectionTracker, HomeAssistantConnectionTracker>();

// Same reasoning as IHomeAssistantConnectionTracker just above -
// PlatformAgentMetricsWorker (shared) depends on this; a trivial
// Interlocked-backed counter with no dependencies of its own, so cheap
// and harmless to register unconditionally even though only
// Low-type upload paths ever call AddBytesUploaded(). A High-type
// agent doesn't upload photos, so TakeBytesUploaded() correctly reports
// 0 - not a missing feature, an honest reading. Also found live, same
// startup-crash pattern as the HomeAssistant one above.
builder.Services.AddSingleton<INetworkUsageTracker, NetworkUsageTracker>();

builder.Services.AddHostedService<PlatformAgentHeartbeatWorker>();
builder.Services.AddHostedService<PlatformDeviceHeartbeatWorker>();
builder.Services.AddHostedService<PlatformAgentMetricsWorker>();
builder.Services.AddHostedService<PlatformCommandPollingWorker>();
builder.Services.AddHostedService<PlatformAgentCommandPollingWorker>();
builder.Services.AddHostedService<PlatformLogShippingWorker>();

// Sprint 8 - drains the error buffer into AgentEvent rows + the
// agent-events queue. Harmless when Messaging:AgentEventQueue is unset:
// it logs once and returns.
builder.Services.AddHostedService<PlatformErrorEventWorker>();

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
static async Task TryLoadRemoteSharedConfigAsync(ConfigurationManager configuration, byte[]? credentialEncryptionKey)
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

        configBytes = DecryptConfigBytes(configBytes, credentialEncryptionKey);

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

static async Task TryLoadRemoteConfigAsync(ConfigurationManager configuration, byte[]? credentialEncryptionKey)
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

        // Decision-log.md ADR-069 - try the versioned manifest path first;
        // a 404 (never published through the versioned pipeline) falls
        // through to the flat blob. "Run alongside," never a special case.
        //
        // The second candidate here used to be the unscoped name, for
        // agents that predated tenant/site scoping. Both those blobs and
        // those agents are gone (ADR-091), so a 404 now means what it says.
        var key = new ConfigBlobKey(
            configuration["Agent:TenantId"] ?? "", configuration["Agent:SiteId"] ?? "", agentId);

        var configBytes = await TryLoadViaManifestAsync(
            blobClient, AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName(key));

        var source = "the versioned manifest";

        if (configBytes == null)
        {
            // A 404 here propagates to the handler below unchanged: it is
            // the genuine "no config for this agent" case.
            configBytes = await blobClient.DownloadAsync(
                AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(key));

            source = "the flat blob";
        }

        configBytes = DecryptConfigBytes(configBytes, credentialEncryptionKey);

        InsertConfigSourceBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource { Stream = new ReusableMemoryStream(configBytes) });

        Console.WriteLine($"[Startup] Loaded remote config for agent {agentId} (via {source}).");
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

// decision-log.md ADR-086 - reads CredentialEncryption:Key straight from
// appsettings.json/env vars (same bootstrap tier as Storage:ConnectionString -
// both are needed before any remote fetch can happen, so both live
// local-only, outside git's protection but consistent with this file's
// existing precedent). A missing or unparseable key is never fatal
// (matches every other "additive, never required" convention in this
// file) - it just means "enc:v1:"-prefixed values in downloaded config
// stay encrypted and unusable until the key is fixed.
static byte[]? TryParseCredentialEncryptionKey(ConfigurationManager configuration)
{
    var rawKey = configuration["CredentialEncryption:Key"];

    if (string.IsNullOrWhiteSpace(rawKey))
    {
        Console.WriteLine("[Startup] CredentialEncryption:Key is not set; encrypted config fields will not be decrypted.");
        return null;
    }

    try
    {
        return CredentialCipher.ParseKey(rawKey);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] CredentialEncryption:Key is invalid, encrypted config fields will not be decrypted: {ex.Message}");
        return null;
    }
}

// decision-log.md ADR-085 - decrypts any "enc:v1:"-prefixed string
// anywhere in a downloaded config blob before it's added as a config
// source (agent-config only - device-config blobs are decrypted
// separately in TryProcessDeviceBlob, before DeviceConfigRuntimeAdapter.Adapt
// runs). A null key or a decrypt failure just returns the bytes
// unchanged - never fails startup.
static byte[] DecryptConfigBytes(byte[] configBytes, byte[]? credentialEncryptionKey)
{
    if (credentialEncryptionKey == null)
        return configBytes;

    // A UTF-8 BOM makes JsonNode.Parse throw on the very first byte, which
    // used to send this straight to the catch below and hand back the
    // still-encrypted bytes. That failure was silent in the worst possible
    // way: the config "loaded", and the agent then died several layers
    // later on `QueueServiceClient("enc:v1:...")` complaining about
    // account information - a message that names neither the BOM nor the
    // config. Any editor that saves JSON as UTF-8-with-BOM can reintroduce
    // this, so tolerate it rather than merely documenting it.
    var json = StripUtf8Bom(configBytes);

    try
    {
        var node = JsonNode.Parse(json);
        CredentialCipher.DecryptInPlace(node, credentialEncryptionKey);
        return JsonSerializer.SerializeToUtf8Bytes(node);
    }
    catch (Exception ex)
    {
        // Deliberately still non-fatal, but no longer quiet about what it
        // implies: if the document contained enc:v1: values, returning it
        // as-is means the agent is about to use ciphertext as if it were a
        // real setting, and the resulting error will point somewhere else
        // entirely.
        Console.WriteLine($"[Startup] Failed to decrypt downloaded config, using it as-is: {ex.Message}");

        if (ContainsEncryptedValues(json))
        {
            Console.WriteLine(
                "[Startup] WARNING: that config contains enc:v1: values which are now being used "
                + "UNDECRYPTED. Expect failures that look unrelated (e.g. an invalid storage "
                + "connection string). Check the blob is valid UTF-8 JSON without a BOM.");
        }

        return json;
    }
}

static byte[] StripUtf8Bom(byte[] bytes) =>
    bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
        ? bytes[3..]
        : bytes;

static bool ContainsEncryptedValues(byte[] jsonBytes)
{
    try
    {
        return System.Text.Encoding.UTF8.GetString(jsonBytes).Contains("enc:v1:", StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

// Decision-log.md ADR-069 - downloads the manifest at manifestBlobName,
// then the version blob it points to, returning null (not throwing) on a
// 404 for the manifest specifically - that's the expected "never
// published through the new pipeline" case every caller falls back from.
// A 404 on the *version* blob it points to (manifest exists but its
// target doesn't - shouldn't happen, but not impossible under a rare
// race) is NOT swallowed here, since that's a real inconsistency worth
// surfacing to the caller's own outer catch rather than silently
// pretending the manifest didn't exist.
static async Task<byte[]?> TryLoadViaManifestAsync(
    AzureBlobStorageClient blobClient, string containerName, string manifestBlobName)
{
    byte[] manifestBytes;

    try
    {
        manifestBytes = await blobClient.DownloadAsync(containerName, manifestBlobName);
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return null;
    }

    var manifest = JsonSerializer.Deserialize<ConfigurationManifest>(manifestBytes);

    if (manifest == null)
        return null;

    return await blobClient.DownloadAsync(containerName, manifest.ConfigurationUri);
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
    string agentId,
    byte[]? credentialEncryptionKey)
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
        var allBlobNames = await blobClient.ListBlobNamesAsync(DeviceConfigBlob.ContainerName);
        var devices = new JsonArray();

        // This used to enumerate and DOWNLOAD the entire container, then
        // discard whatever this agent did not own - meaning every agent
        // routinely pulled every other tenant's device names, locations,
        // brands and RTSP URLs across the wire (the credential fields are
        // ciphertext since ADR-085; the rest never was).
        //
        // Now it reads only its own tenant/site prefix, and falls back to
        // the flat listing only when that prefix yields nothing, which is
        // the un-migrated case. The per-blob OwningAgentId check further
        // down stays regardless: in the legacy layout the name carries no
        // tenant, so the prefix cannot be the only thing standing between
        // this agent and another tenant's device.
        var prefix = DeviceConfigBlob.Prefix(
            configuration["Agent:TenantId"] ?? "", configuration["Agent:SiteId"] ?? "");

        var scopedNames = prefix.Length == 0
            ? []
            : allBlobNames
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .Select(n => n[prefix.Length..])
                .ToList();

        // Falling back to the whole container is what keeps an agent
        // working the first time it starts on this build, before anything
        // has been republished into its prefix.
        var usingScopedLayout = scopedNames.Count > 0;

        var blobNames = usingScopedLayout
            ? scopedNames
            : allBlobNames.ToList();

        var blobPrefix = usingScopedLayout ? prefix : "";

        Console.WriteLine(
            usingScopedLayout
                ? $"[Startup] Reading device configs from the scoped layout ({prefix}), {blobNames.Count} blob(s)."
                : $"[Startup] No device configs under the scoped prefix; falling back to the flat container listing ({blobNames.Count} blob(s)).");

        // Decision-log.md ADR-068 - reported on this Agent's own heartbeat
        // via AgentConfigMetadataOptions.ConfigurationLoadErrors, so an
        // admin investigating a stuck device is pointed at the right
        // agent. Coarse (Agent-level, not per-device) by design - see
        // AgentHeartbeat.ConfigurationLoadError.
        var loadErrors = new List<string>();

        // Decision-log.md ADR-069 - Blob Storage has no real directories,
        // "/" is just a name convention, so the new versioned layout
        // (device-config/{id}/current.json, .../versions/{n}.json) shows
        // up in this SAME flat listing alongside legacy {id}.json entries -
        // partitioned here purely by string shape. versions/{n}.json
        // entries are deliberately never acted on directly; only ever
        // read by URI from a manifest.
        const string ManifestSuffix = "/current.json";

        // Depth matters now, not just shape. When falling back to the flat
        // listing, scoped entries ({tenant}/{site}/{id}/current.json) are
        // also present and must not be mistaken for manifests of a device
        // literally named "{tenant}/{site}/{id}" - so a manifest is
        // required to have exactly one slash, and a flat blob none.
        var manifestBlobNames = blobNames
            .Where(n => n.EndsWith(ManifestSuffix, StringComparison.Ordinal)
                && n.Count(c => c == '/') == 1)
            .ToList();

        var legacyBlobNames = blobNames
            .Where(n => !n.Contains('/'))
            .ToList();

        // Manifest-driven devices processed first, so a device republished
        // through the new pipeline always wins over its own stale legacy
        // flat blob (still written on every publish, ADR-069's "run
        // alongside" dual-write) rather than the two racing on
        // enumeration order.
        var processedDeviceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var manifestBlobName in manifestBlobNames)
        {
            var deviceId = manifestBlobName[..^ManifestSuffix.Length];

            byte[] manifestBytes;

            try
            {
                manifestBytes = await blobClient.DownloadAsync(
                    DeviceConfigBlob.ContainerName, blobPrefix + manifestBlobName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Startup] Failed to download device manifest {manifestBlobName}, skipping: {ex.Message}");
                continue;
            }

            ConfigurationManifest? manifest;

            try
            {
                manifest = JsonSerializer.Deserialize<ConfigurationManifest>(manifestBytes);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Startup] Device manifest {manifestBlobName} is not valid JSON, skipping: {ex.Message}");
                continue;
            }

            if (manifest == null)
            {
                Console.WriteLine($"[Startup] Device manifest {manifestBlobName} deserialized to null, skipping.");
                continue;
            }

            byte[] versionBytes;

            try
            {
                versionBytes = await blobClient.DownloadAsync(DeviceConfigBlob.ContainerName, manifest.ConfigurationUri);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Startup] Failed to download device version blob {manifest.ConfigurationUri} (from manifest {manifestBlobName}), skipping: {ex.Message}");
                continue;
            }

            if (TryProcessDeviceBlob(versionBytes, manifestBlobName, agentId, deviceId, loadErrors, credentialEncryptionKey, out var deviceObject))
            {
                devices.Add(deviceObject);
                processedDeviceIds.Add(deviceId);
            }
        }

        foreach (var blobName in legacyBlobNames)
        {
            var deviceId = blobName[..^".json".Length];

            if (processedDeviceIds.Contains(deviceId))
                continue; // superseded by a manifest-driven load above

            byte[] deviceBytes;

            try
            {
                deviceBytes = await blobClient.DownloadAsync(
                    DeviceConfigBlob.ContainerName, blobPrefix + blobName);
            }
            catch (Exception ex)
            {
                // One bad/unreachable device blob must never take every
                // other device down with it.
                Console.WriteLine($"[Startup] Failed to download device config blob {blobName}, skipping: {ex.Message}");
                continue;
            }

            if (TryProcessDeviceBlob(deviceBytes, blobName, agentId, deviceId, loadErrors, credentialEncryptionKey, out var deviceObject))
                devices.Add(deviceObject);
        }

        var root = new JsonObject { ["Devices"] = devices };

        if (loadErrors.Count > 0)
        {
            root["ConfigurationLoadErrors"] = new JsonArray(
                loadErrors.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray());
        }

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

// Decision-log.md ADR-069 - the parse/adapt/filter/secrets-merge pipeline
// every device blob goes through regardless of which path (manifest or
// legacy flat) supplied its bytes - factored out so both loops above
// share identical behavior rather than two copies drifting apart.
// Translates the new capabilities[]-shaped document (decision-log.md
// ADR-064) into the legacy flat DeviceOptions shape everything downstream
// already expects - a no-op for any blob still in the legacy shape. Its
// own try/catch (ADR-066) - Adapt throws UnsupportedConfigurationSchemaException
// on an unrecognized SchemaVersion, caught here so one bad device never
// takes every other device down with it.
static bool TryProcessDeviceBlob(
    byte[] deviceBytes,
    string blobNameForLogging,
    string agentId,
    string deviceId,
    List<string> loadErrors,
    byte[]? credentialEncryptionKey,
    out JsonObject? deviceObject)
{
    deviceObject = null;

    JsonNode? deviceNode;

    try
    {
        deviceNode = JsonNode.Parse(deviceBytes);
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"[Startup] Device config blob {blobNameForLogging} is not valid JSON, skipping: {ex.Message}");
        return false;
    }

    if (deviceNode is not JsonObject deviceObjectRaw)
    {
        Console.WriteLine($"[Startup] Device config blob {blobNameForLogging} is not a JSON object, skipping.");
        return false;
    }

    // Ownership is checked FIRST, on the raw document, before anything is
    // decrypted. device-config is a flat, un-scoped container and this
    // Agent lists all of it (see TryLoadRemoteDeviceConfigsAsync), so it
    // downloads every other site's device blobs too. Decrypting before
    // this check - as this function used to - meant every Agent briefly
    // held every other tenant's device credentials in plaintext. It still
    // downloads them, but they now stay ciphertext and are discarded
    // unread.
    //
    // Safe on both document shapes: OwningAgentId is a top-level field on
    // the legacy flat shape and on the capabilities[] wire document alike
    // (verified against the real cached documents), and it is never one of
    // the credential-shaped keys CredentialCipher touches, so it reads
    // correctly pre-decrypt. The UnsupportedConfigurationSchemaException
    // branch below already relied on exactly this.
    if (!string.Equals(
            deviceObjectRaw["OwningAgentId"]?.GetValue<string>(), agentId, StringComparison.Ordinal))
    {
        return false;
    }

    // decision-log.md ADR-085 - decrypted in place before Adapt() runs, so
    // ImageCaptureRuntimeAdapter's verbatim Connection->Settings copy (and
    // any other capability adapter) sees plaintext. Also before the
    // last-known-good cache write below, so a future fallback load never
    // needs to decrypt again.
    if (credentialEncryptionKey != null)
        CredentialCipher.DecryptInPlace(deviceObjectRaw, credentialEncryptionKey);

    JsonObject flattened;

    try
    {
        flattened = DeviceConfigRuntimeAdapter.Adapt(deviceObjectRaw);
    }
    catch (UnsupportedConfigurationSchemaException ex)
    {
        // Decision-log.md ADR-070 - the published version failed schema
        // validation. Before dropping the device entirely, try the last
        // version that actually loaded successfully here - never replace
        // a known-good running config with a broken one (spec section
        // 14/28's own framing). No ownership re-check needed any more:
        // the guard above already established this device is ours before
        // Adapt was ever called, which is also why the cache lookup is
        // meaningful (the cache is only ever written for owned devices).
        var fallback = TryLoadCachedDeviceConfig(deviceId);

        if (fallback != null)
        {
            Console.WriteLine(
                $"[Startup] Device config blob {blobNameForLogging}: {ex.Message} Continuing on cached last-known-good config.");
            loadErrors.Add($"{ex.Message} Continuing on cached last-known-good config for device {deviceId}.");

            TryMergeLocalDeviceSecrets(fallback);

            deviceObject = fallback;
            return true;
        }

        loadErrors.Add(ex.Message);

        Console.WriteLine($"[Startup] Device config blob {blobNameForLogging}: {ex.Message} Skipping.");
        return false;
    }

    // No second ownership check here: the raw-document guard above already
    // ran, and DeviceConfigRuntimeAdapter.Adapt copies OwningAgentId
    // through verbatim (a legacy-shape document passes through untouched),
    // so re-reading it post-adapt could only ever produce the same answer.
    TryMergeLocalDeviceSecrets(flattened);

    // Decision-log.md ADR-070 - cache the raw (pre-adapt, pre-secrets-merge)
    // document as this device's new last-known-good, so a future broken
    // publish has something to fall back to. Cached pre-merge since local
    // secrets don't change with published versions and are re-merged fresh
    // on every load anyway (see TryMergeLocalDeviceSecrets above).
    TryWriteDeviceConfigCache(deviceId, deviceObjectRaw);

    deviceObject = flattened;
    return true;
}

// Decision-log.md ADR-070 - keyed by deviceId (not RuntimeDeviceId
// specifically - they're the same value for any device that's ever
// published through this pipeline, which is the only kind that can reach
// here at all), overwritten on every successful load. Lives in the publish
// output directory, same as common-config.json and the secrets-sibling
// files - survives a restart-command-triggered `docker restart` (same
// container, see PlatformCommandPollingWorker) but not a full redeploy through the
// Updater (a new container), which is no worse than today's behavior on a
// cold box.
static string DeviceConfigCachePath(string deviceId) =>
    Path.Combine(AppContext.BaseDirectory, "config-cache", "devices", $"{deviceId}.json");

static void TryWriteDeviceConfigCache(string deviceId, JsonObject rawDeviceDocument)
{
    try
    {
        var cachePath = DeviceConfigCachePath(deviceId);

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllBytes(cachePath, JsonSerializer.SerializeToUtf8Bytes(rawDeviceDocument));
    }
    catch (Exception ex)
    {
        // Best-effort - a cache write failure must never fail the device
        // load that's succeeding right now.
        Console.WriteLine($"[Startup] Device {deviceId}: failed to write last-known-good config cache: {ex.Message}");
    }
}

// Returns null (never throws) on any failure - cache missing, unreadable,
// corrupted, or itself no longer schema-supported (e.g. this Agent build
// was downgraded since the cache was written) - every case just means
// "nothing to fall back to," handled identically by the caller.
static JsonObject? TryLoadCachedDeviceConfig(string deviceId)
{
    var cachePath = DeviceConfigCachePath(deviceId);

    if (!File.Exists(cachePath))
        return null;

    byte[] cachedBytes;

    try
    {
        cachedBytes = File.ReadAllBytes(cachePath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Device {deviceId}: cached fallback config unreadable, dropping device: {ex.Message}");
        return null;
    }

    JsonNode? cachedNode;

    try
    {
        cachedNode = JsonNode.Parse(cachedBytes);
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"[Startup] Device {deviceId}: cached fallback config is not valid JSON, dropping device: {ex.Message}");
        return null;
    }

    if (cachedNode is not JsonObject cachedRaw)
        return null;

    try
    {
        return DeviceConfigRuntimeAdapter.Adapt(cachedRaw);
    }
    catch (UnsupportedConfigurationSchemaException ex)
    {
        Console.WriteLine($"[Startup] Device {deviceId}: cached fallback config also unsupported ({ex.Message}), dropping device.");
        return null;
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
static void TryLoadLocalSharedConfig(ConfigurationManager configuration, byte[]? credentialEncryptionKey)
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
        configBytes = DecryptConfigBytes(configBytes, credentialEncryptionKey);

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

// Dev convenience: reads the exact same file CONTENT a remote config blob
// holds, from disk next to the executable instead of Blob Storage - so a
// file can be dropped in this folder and used directly with no Azure round
// trip, without maintaining a second config format. Only takes effect when
// LoadLocalSettings is explicitly true; off (remote blob) by default.
//
// The name is deliberately flat "{agentId}.json" rather than
// AgentConfigBlob.BlobName, which since tenant/site scoping (ADR-091)
// would require a nested tenant/site directory next to the executable to
// hold one dev file.
static void TryLoadLocalConfig(ConfigurationManager configuration, byte[]? credentialEncryptionKey)
{
    var agentId = configuration["Agent:AgentId"];

    if (string.IsNullOrWhiteSpace(agentId))
    {
        Console.WriteLine("[Startup] LoadLocalSettings is true but Agent:AgentId is not set; skipping local config load.");
        return;
    }

    var path = Path.Combine(AppContext.BaseDirectory, $"{agentId}.json");

    if (!File.Exists(path))
    {
        Console.WriteLine($"[Startup] LoadLocalSettings is true but no local config file found at {path}; using local appsettings only.");
        return;
    }

    try
    {
        var configBytes = File.ReadAllBytes(path);
        configBytes = DecryptConfigBytes(configBytes, credentialEncryptionKey);

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