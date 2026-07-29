using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using TapoSharp.Models;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models.Camera;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Agent.Workers;

public sealed class DeviceHeartbeatWorker : BackgroundService
{
    private readonly ICaptureStatusStore _statusStore;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IDeviceHeartbeatPublisher _gateway;
    private readonly AgentOptions _agent;
    private readonly DeviceHeartbeatOptions _options;
    private readonly ILogger<DeviceHeartbeatWorker> _logger;

    public DeviceHeartbeatWorker(
        CaptureStatusStore statusStore,
        IDeviceRegistry deviceRegistry,
        IDeviceHeartbeatPublisher gateway,
        IOptions<AgentOptions> agentOptions,
        IOptions<DeviceHeartbeatOptions> options,
        ILogger<DeviceHeartbeatWorker> logger)
    {
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _gateway = gateway;
        _agent = agentOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
           "DeviceHeartbeatWorker for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next heartbeat: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
           _options.HeartbeatInterval,
           DateTime.Now,
           DateTime.UtcNow,
           DateTime.Now.Add(_options.HeartbeatInterval),
           DateTime.UtcNow.Add(_options.HeartbeatInterval));


        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var device in _deviceRegistry.GetCameras())
            {
                await PublishHeartbeatAsync(
                    device,
                    stoppingToken);
            }

            await Task.Delay(
                _options.HeartbeatInterval,
                stoppingToken);
        }
    }

    private async Task PublishHeartbeatAsync(
        DeviceOptions device,
        CancellationToken cancellationToken)
    {
        DeviceRuntimeState? runtime = null;

        if (!_statusStore.TryGet(
                device.DeviceId,
                out runtime))
        {
            _logger.LogDebug(
                "No runtime state yet for {DeviceId}",
                device.DeviceId);
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
                LastActivityUtc = runtime.LastCaptureUtc,

                // This is the configured expectation for this device.

                ExpectedActivityInterval = device.ActivityInterval,
                ExpectedHeartbeatInterval = _options.HeartbeatInterval,

                Error = runtime?.LastError,

                Status = DetermineStatus(
                    runtime,
                    device.ActivityInterval)
            };

        await _gateway.PublishAsync(
            heartbeat,
            cancellationToken);

        _logger.LogDebug(
            "Heartbeat published for {DeviceId}",
            device.DeviceId);
    }

    private static DeviceHeartbeatStatus DetermineStatus(
        DeviceRuntimeState? runtime,
        TimeSpan expectedInterval)
    {
        if (runtime is null)
            return DeviceHeartbeatStatus.Unknown;

        if (!string.IsNullOrWhiteSpace(runtime.LastError))
            return DeviceHeartbeatStatus.Error;

        if (runtime.LastCaptureUtc == default)
            return DeviceHeartbeatStatus.Unknown;

        var elapsed =
            DateTime.UtcNow - runtime.LastCaptureUtc;

        if (elapsed > expectedInterval + expectedInterval)
            return DeviceHeartbeatStatus.Warning;

        return DeviceHeartbeatStatus.Online;
    }
}