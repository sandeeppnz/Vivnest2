using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Runtime.Workers;

public sealed class DeviceHeartbeatWorker : BackgroundService
{
    private readonly ICaptureStatusStore _statusStore;
    private readonly IDeviceRuntimeStore _runtimeStateStore;
    private readonly IOfflineDetection _offlineDetection;
    private readonly IEventHandler<DeviceHeartbeatGeneratedEvent> _handler;
    private readonly AgentOptions _agent;
    private readonly DeviceHeartbeatOptions _options;
    private readonly ILogger<DeviceHeartbeatWorker> _logger;

    public DeviceHeartbeatWorker(
        ICaptureStatusStore statusStore,
        IDeviceRuntimeStore deviceRegistry,
        IOfflineDetection offlineDetection,
        IEventHandler<DeviceHeartbeatGeneratedEvent> handler,
        IOptions<AgentOptions> agentOptions,
        IOptions<DeviceHeartbeatOptions> options,
        ILogger<DeviceHeartbeatWorker> logger)
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
           "DeviceHeartbeatWorker for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next heartbeat: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
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
                DeviceType = device.Type,

                LastHeartbeatUtc = DateTime.UtcNow,
                LastActivityUtc = runtime.LastActivityUtc,

                // This is the configured expectation for this device.

                ExpectedLivenessInterval = device.LivenessInterval,
                ExpectedHeartbeatInterval = _options.HeartbeatInterval,

                Error = runtime.LastError,

                Status = status,
                Source = DeviceHeartbeatSource.Native
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