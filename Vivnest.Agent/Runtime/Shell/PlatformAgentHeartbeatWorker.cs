using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;
using Vivnest.Agent.Capabilities;
using Vivnest.Agent.Capabilities.Bridges.HomeAssistant;
using Vivnest.Core.Domain;
using Vivnest.Core.Options;
using static Vivnest.Core.Constants.RuntimeConfigurationSchemaVersions;

namespace Vivnest.Agent.Runtime.Shell;

public sealed class PlatformAgentHeartbeatWorker : BackgroundService
{
    private readonly ILogger<PlatformAgentHeartbeatWorker> _logger;
    private readonly IEventHandler<AgentHeartbeatGeneratedEvent> _handler;
    private readonly IHomeAssistantConnectionTracker _homeAssistantConnectionTracker;
    private readonly AgentOptions _agentOptions;
    private readonly AgentHeartbeatOptions _heartbeatOptions;
    private readonly AgentConfigMetadataOptions _configMetadata;

    private readonly DateTime _startedUtc = DateTime.UtcNow;

    // Snapshot facts about this process's own environment - fixed for the
    // process's lifetime, so read once here rather than every tick.
    private readonly string _runtimeVersion = RuntimeInformation.FrameworkDescription;
    private readonly string _osDescription = RuntimeInformation.OSDescription;

    public PlatformAgentHeartbeatWorker(
        IOptions<AgentOptions> agentOptions,
        IOptions<AgentHeartbeatOptions> heartbeatOptions,
        IOptions<AgentConfigMetadataOptions> configMetadata,
        IEventHandler<AgentHeartbeatGeneratedEvent> handler,
        IHomeAssistantConnectionTracker homeAssistantConnectionTracker,
        ILogger<PlatformAgentHeartbeatWorker> logger)
    {
        _agentOptions = agentOptions.Value;
        _heartbeatOptions = heartbeatOptions.Value;
        _configMetadata = configMetadata.Value;
        _logger = logger;
        _handler = handler;
        _homeAssistantConnectionTracker = homeAssistantConnectionTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_heartbeatOptions.Enabled)
        {
            _logger.LogInformation("Agent heartbeat disabled.");
            return;
        }

        _logger.LogInformation(
              "PlatformAgentHeartbeatWorker for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next heartbeat: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
              _heartbeatOptions.HeartbeatInterval,
              DateTime.Now,
              DateTime.UtcNow,
              DateTime.Now.Add(_heartbeatOptions.HeartbeatInterval),
              DateTime.UtcNow.Add(_heartbeatOptions.HeartbeatInterval));

        // decision-log.md ADR-066 - checked once at startup, not per tick
        // (nothing reloads config mid-process, so the value can't change).
        // Unlike DeviceConfigRuntimeAdapter's per-device throw/skip, a bad
        // value here just means AiClassification binding for this Agent
        // may not match what this build expects - lower risk than the
        // Device side's shape-flattening logic, so a loud warning is
        // enough; nothing needs to be skipped.
        if (_configMetadata.ConfigurationSchemaVersion is { } schemaVersion
            && schemaVersion != CurrentAgentSchemaVersion)
        {
            _logger.LogWarning(
                "Agent config declares ConfigurationSchemaVersion {Declared}, but this Agent build only understands {Current}. AiClassification settings may not bind as expected.",
                schemaVersion,
                CurrentAgentSchemaVersion);
        }

        using var timer = new PeriodicTimer(_heartbeatOptions.HeartbeatInterval);

        do
        {
            try
            {
                var heartbeat = new AgentHeartbeat
                {
                    AgentId = _agentOptions.AgentId,
                    TenantId = _agentOptions.TenantId,
                    SiteId = _agentOptions.SiteId,
                    StartedUtc = _startedUtc,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    HostName = Environment.MachineName,
                    // decision-log.md ADR-087 - the Admin registry's own
                    // Name, published as a top-level sibling key on
                    // agent-config (AgentConfigMetadataOptions.Name), not
                    // a locally self-typed value - null until this Agent
                    // has been published through that pipeline at least
                    // once.
                    Name = _configMetadata.Name ?? "",
                    FirmwareVersion = _agentOptions.FirmwareVersion,
                    RuntimeVersion = _runtimeVersion,
                    OsDescription = _osDescription,
                    Error = null,
                    HeartbeatInterval = _heartbeatOptions.HeartbeatInterval,
                    HomeAssistantLastConnectedUtc = _homeAssistantConnectionTracker.LastConnectedUtc,
                    // See PlatformDeviceHeartbeatWorker.ProcessDeviceHeartbeat's
                    // comment - IConfiguration's DateTime binder produces
                    // Kind=Local for a "Z"-suffixed value, not Kind=Utc;
                    // ToUniversalTime() recovers the true instant.
                    ConfigurationPublishedUtc = _configMetadata.ConfigurationPublishedUtc?.ToUniversalTime(),
                    // decision-log.md ADR-068 - fixed for the process's
                    // lifetime, same as _startedUtc/_runtimeVersion above;
                    // set once at startup by TryLoadRemoteDeviceConfigsAsync,
                    // never changes mid-process (no reload path exists).
                    ConfigurationLoadError = _configMetadata.ConfigurationLoadErrors is { Count: > 0 } loadErrors
                        ? string.Join("; ", loadErrors)
                        : null,
                    // Decision-log.md ADR-069 - null unless this Agent's own
                    // config was loaded via the new versioned manifest path.
                    ConfigurationVersion = _configMetadata.ConfigurationVersion,
                    ConfigurationHash = _configMetadata.ConfigurationHash
                };

                await _handler.HandleAsync(
                    new AgentHeartbeatGeneratedEvent(heartbeat),
                    stoppingToken);

                _logger.LogDebug(
                    "Agent heartbeat updated for {AgentId}",
                    heartbeat.AgentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update agent heartbeat.");
            }

        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
