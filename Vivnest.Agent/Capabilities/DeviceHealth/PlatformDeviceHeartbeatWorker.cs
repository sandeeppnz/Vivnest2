using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Shell;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.DeviceHealth;

public sealed class PlatformDeviceHeartbeatWorker : BackgroundService
{
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly IDeviceRuntimeStore _runtimeStateStore;
    private readonly IOfflineDetection _offlineDetection;
    private readonly IEventHandler<DeviceHeartbeatGeneratedEvent> _handler;
    private readonly AgentOptions _agent;
    private readonly DeviceHeartbeatOptions _options;
    private readonly ILogger<PlatformDeviceHeartbeatWorker> _logger;

    public PlatformDeviceHeartbeatWorker(
        IDeviceRuntimeStateStore statusStore,
        IDeviceRuntimeStore deviceRegistry,
        IOfflineDetection offlineDetection,
        IEventHandler<DeviceHeartbeatGeneratedEvent> handler,
        IOptions<AgentOptions> agentOptions,
        IOptions<DeviceHeartbeatOptions> options,
        ILogger<PlatformDeviceHeartbeatWorker> logger)
    {
        _statusStore = statusStore;
        _runtimeStateStore = deviceRegistry;
        _offlineDetection = offlineDetection;
        _handler = handler;
        _agent = agentOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Device heartbeat disabled.");
            return;
        }

        _logger.LogInformation(
           "PlatformDeviceHeartbeatWorker for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next heartbeat: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
           _options.HeartbeatInterval,
           DateTime.Now,
           DateTime.UtcNow,
           DateTime.Now.Add(_options.HeartbeatInterval),
           DateTime.UtcNow.Add(_options.HeartbeatInterval));


        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var device in _runtimeStateStore.GetDevices())
            {
                try
                {
                    await ProcessDeviceHeartbeat(
                        device,
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to process device heartbeat for {DeviceId}.",
                        device.DeviceId);
                }
            }

            await Task.Delay(
                _options.HeartbeatInterval,
                stoppingToken);
        }
    }

    private async Task ProcessDeviceHeartbeat(
        DeviceOptions device,
        CancellationToken cancellationToken)
    {
        var runtime = _statusStore.GetOrAdd(device.DeviceId);

        var status = _offlineDetection.Evaluate(
            runtime,
            device.LivenessInterval,
            device.WarningMultiplier);

        var previousStatus = runtime.LastReportedStatus;

        runtime.LastReportedStatus = status;

        if (previousStatus == status)
        {
            _logger.LogDebug(
                "No status change for {DeviceId} ({Status}); skipping heartbeat.",
                device.DeviceId,
                status);

            return;
        }

        var heartbeat =
            new DeviceHeartbeat
            {
                AgentId = _agent.AgentId,
                TenantId = _agent.TenantId,
                SiteId = _agent.SiteId,

                DeviceId = device.DeviceId,
                Name = device.Name,
                DeviceType = device.Type,

                LastHeartbeatUtc = DateTime.UtcNow,
                LastActivityUtc = runtime.LastActivityUtc,

                // This is the configured expectation for this device.

                ExpectedLivenessInterval = device.LivenessInterval,
                ExpectedHeartbeatInterval = _options.HeartbeatInterval,

                Error = runtime.LastError,

                Status = status,
                Source = DeviceHeartbeatSource.Native,
                ParentDeviceId = string.IsNullOrWhiteSpace(device.ParentDeviceId)
                    ? null
                    : device.ParentDeviceId,

                // The container's own OS timezone (Dockerfile's TZ, not app
                // config) - see Dockerfile for why this isn't Agent:Timezone.
                Timezone = LocalTimeZone.IanaId,

                Location = device.Location,
                Brand = device.Brand,
                Model = device.Model,
                Firmware = device.Firmware,

                SinkCleanlinessEnabled = device.SinkCleanliness?.Enabled ?? false,
                ObjectDetectionEnabled = device.ObjectDetection?.Enabled ?? false,

                // .ToUniversalTime(), not DateTime.SpecifyKind(..., Utc) -
                // IConfiguration's default DateTime binder parses a
                // "Z"-suffixed JSON string by converting it to the
                // container's local timezone with Kind=Local (the value
                // itself is correctly shifted, only the Kind tag is
                // wrong), not by preserving Kind=Utc. SpecifyKind would
                // silently relabel an already-shifted local value as UTC,
                // corrupting it by the timezone offset; ToUniversalTime()
                // converts it back to the true instant regardless of
                // which Kind the binder assigned. Azure Table SDK rejects
                // anything but Kind=Utc outright.
                ConfigurationPublishedUtc = device.ConfigurationPublishedUtc?.ToUniversalTime(),
                // Decision-log.md ADR-069 - null on any device loaded via
                // the legacy flat blob (never republished through the new
                // manifest path) - no Kind conversion needed, these aren't
                // DateTime values.
                ConfigurationVersion = device.ConfigurationVersion,
                ConfigurationHash = device.ConfigurationHash
            };

        await _handler.HandleAsync(
            new DeviceHeartbeatGeneratedEvent(heartbeat),
            cancellationToken);

        _logger.LogInformation(
            "Heartbeat published for {DeviceId}: status changed {Previous} -> {New}",
            device.DeviceId,
            previousStatus,
            status);
    }
}