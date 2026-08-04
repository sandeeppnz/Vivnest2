using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Capabilities.Camera;
using Vivnest.Agent.Capabilities.DeviceHealth;
using Vivnest.Agent.Capabilities.HomeAssistant;
using Vivnest.Agent.Capabilities.MotionSensor;
using Vivnest.Agent.Capabilities.SmartPlug;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Agent.Runtime.Shell;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.DependencyInjection;


var builder = Host.CreateApplicationBuilder(args);

// Remote config: layer a per-agent blob (agent-config/{agentId}.json,
// written by the dashboard's config editor - see decision-log.md) on top
// of local appsettings.json before the rest of the host builds. Read
// bootstrap-only, before this source is added: AgentId and
// Storage:ConnectionString must come from local config/env vars alone,
// since they're what's needed to reach the remote blob in the first
// place. Best-effort and additive, not required - if no blob exists yet,
// or the fetch fails for any reason, the agent proceeds on local config +
// each Options class's own code-level defaults alone, exactly as it
// always has. Local dev (`dotnet run`) is unaffected either way.
await TryLoadRemoteConfigAsync(builder.Configuration);

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




builder.Services.AddInfrastructure();

builder.Services.AddSingleton<IEventDispatcher, EventDispatcher>();

builder.Services.AddSingleton<IEventHandler<AgentHeartbeatGeneratedEvent>, AgentHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<DeviceHeartbeatGeneratedEvent>, DeviceHeartbeatHandler>();
builder.Services.AddSingleton<IEventHandler<CameraCaptureCompletedEvent>, CameraCaptureHandler>();
builder.Services.AddSingleton<IEventHandler<CameraCaptureFailedEvent>, CameraCaptureFailedHandler>();
builder.Services.AddSingleton<IEventHandler<SmartPlugReadingCompletedEvent>, SmartPlugReadingHandler>();
builder.Services.AddSingleton<IEventHandler<SmartPlugReadingFailedEvent>, SmartPlugReadingFailedHandler>();
builder.Services.AddSingleton<IEventHandler<SmartPlugPowerStateChangedEvent>, SmartPlugPowerStateChangedHandler>();
builder.Services.AddSingleton<IEventHandler<HomeAssistantStateChangedEvent>, HomeAssistantStateChangedHandler>();
builder.Services.AddSingleton<IEventHandler<MotionSensorStateChangedEvent>, MotionSensorStateChangedHandler>();
builder.Services.AddSingleton<IEventHandler<MotionSensorStateChangedEvent>, MotionTriggerResolverHandler>();
builder.Services.AddSingleton<IEventHandler<MotionSensorReadingFailedEvent>, MotionSensorReadingFailedHandler>();
builder.Services.AddSingleton<IEventHandler<MotionSensorBatteryReportedEvent>, MotionSensorBatteryHandler>();
builder.Services.AddSingleton<IEventHandler<AgentMetricsSampledEvent>, AgentMetricsHandler>();
builder.Services.AddSingleton<IEventHandler<DeviceTriggeredEvent>, CaptureOnTriggerHandler>();


builder.Services.AddSingleton<ICameraCaptureService, CameraCaptureService>();
builder.Services.AddSingleton<ICameraCaptureExecutor, CameraCaptureExecutor>();
builder.Services.AddSingleton<INetworkUsageTracker, NetworkUsageTracker>();
builder.Services.AddSingleton<ISmartPlugMonitorService, SmartPlugMonitorService>();
builder.Services.AddSingleton<IMotionSensorMonitorService, MotionSensorMonitorService>();
builder.Services.AddSingleton<ICaptureStatusStore, CaptureStatusStore>();
builder.Services.AddSingleton<IOfflineDetection, OfflineDetection>();

builder.Services.AddHttpClient<IHomeAssistantCommandSender, HomeAssistantCommandSender>();
builder.Services.AddSingleton<IHomeAssistantLivenessTracker, HomeAssistantLivenessTracker>();
builder.Services.AddSingleton<IHomeAssistantConnectionTracker, HomeAssistantConnectionTracker>();

builder.Services.AddHostedService<CameraCaptureWorker>();
builder.Services.AddHostedService<SmartPlugMonitorWorker>();
builder.Services.AddHostedService<MotionSensorMonitorWorker>();
builder.Services.AddHostedService<AgentHeartbeatWorker>();
builder.Services.AddHostedService<DeviceHeartbeatWorker>();
builder.Services.AddHostedService<HomeAssistantWorker>();
builder.Services.AddHostedService<AgentMetricsWorker>();
builder.Services.AddHostedService<CommandPollingWorker>();

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

        var remoteSource = new JsonStreamConfigurationSource
        {
            Stream = new MemoryStream(configBytes),
        };

        // Insert before env vars, not just appended - env var overrides
        // (e.g. docker run -e HomeAssistant__BaseUrl=...) must still win
        // over whatever the remote blob says, same as they already win
        // over local appsettings.json.
        if (envVarsSourceIndex >= 0)
            sources.Insert(envVarsSourceIndex, remoteSource);
        else
            sources.Add(remoteSource);

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