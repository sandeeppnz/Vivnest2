using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.SmartPlug.Models;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Runtime.Workers;

public sealed class SmartPlugMonitorWorker : BackgroundService
{
    private readonly ISmartPlugMonitorService _monitorService;
    private readonly IEventDispatcher _dispatcher;
    private readonly ICaptureStatusStore _statusStore;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<SmartPlugMonitorWorker> _logger;

    public SmartPlugMonitorWorker(
        ISmartPlugMonitorService monitorService,
        IEventDispatcher dispatcher,
        ICaptureStatusStore statusStore,
        IDeviceRuntimeStore deviceRegistry,
        Microsoft.Extensions.Options.IOptions<AgentOptions> agentOptions,
        ILogger<SmartPlugMonitorWorker> logger)
    {
        _monitorService = monitorService;
        _dispatcher = dispatcher;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Smart Plug Monitor Worker started.");

        var plugs = _deviceRegistry.GetDevices()
            .Where(d => d.Type == DeviceType.SmartPlug)
            .ToList();

        if (plugs.Count == 0)
        {
            _logger.LogWarning(
                "No enabled smart plugs configured. Smart Plug Monitor Worker has nothing to do.");

            return;
        }

        var tasks = plugs.Select(plug =>
            RunMonitorLoopAsync(plug, stoppingToken));

        await Task.WhenAll(tasks);
    }

    private async Task RunMonitorLoopAsync(
        DeviceOptions plugOptions,
        CancellationToken stoppingToken)
    {
        var runtime = _statusStore.GetOrAdd(plugOptions.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var dueForReading =
                plugOptions.SnapshotInterval <= TimeSpan.Zero ||
                runtime.LastCaptureUtc is not { } lastReadUtc ||
                DateTime.UtcNow - lastReadUtc >= plugOptions.SnapshotInterval;

            if (dueForReading)
            {
                await ReadAsync(plugOptions, runtime, stoppingToken);
            }
            else
            {
                await ProbeAsync(plugOptions, runtime, stoppingToken);
            }

            var delay = plugOptions.LivenessInterval;

            _logger.LogInformation(
                "Device {DeviceId} sleeping for {Delay}. Current UTC={Now:u}. Next check UTC={Next:u}",
                plugOptions.DeviceId,
                delay,
                DateTime.UtcNow,
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ReadAsync(
        DeviceOptions plugOptions,
        DeviceRuntimeState runtime,
        CancellationToken stoppingToken)
    {
        var result = await _monitorService.ReadAsync(plugOptions, stoppingToken);

        if (result.Success)
        {
            runtime.LastCaptureUtc = result.ReadAtUtc;
            runtime.LastActivityUtc = result.ReadAtUtc;
            runtime.LastError = null;

            await _dispatcher.PublishAsync(
                new SmartPlugReadingCompletedEvent(result),
                stoppingToken);

            _logger.LogInformation(
                "Power reading reported for {DeviceId}.",
                plugOptions.DeviceId);
        }
        else
        {
            runtime.LastFailureUtc = DateTime.UtcNow;
            runtime.LastError = result.Error;

            await PublishReadingFailedSafeAsync(
                new SmartPlugReadingFailureData(
                    _agentOptions.AgentId,
                    plugOptions.DeviceId,
                    DateTime.UtcNow,
                    result.ErrorCode,
                    result.Error),
                stoppingToken);

            _logger.LogWarning(
                "Power reading failed for {DeviceId}: {Error}",
                plugOptions.DeviceId,
                result.Error);
        }
    }

    private async Task ProbeAsync(
        DeviceOptions plugOptions,
        DeviceRuntimeState runtime,
        CancellationToken stoppingToken)
    {
        try
        {
            var reachable = await _monitorService.CheckReachabilityAsync(
                plugOptions,
                stoppingToken);

            if (reachable)
            {
                runtime.LastActivityUtc = DateTime.UtcNow;

                _logger.LogDebug(
                    "Liveness probe succeeded for {DeviceId}.",
                    plugOptions.DeviceId);
            }
            else
            {
                _logger.LogWarning(
                    "Liveness probe failed for {DeviceId}: plug unreachable.",
                    plugOptions.DeviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Liveness probe errored for {DeviceId}.",
                plugOptions.DeviceId);
        }
    }

    private async Task PublishReadingFailedSafeAsync(
        SmartPlugReadingFailureData failure,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new SmartPlugReadingFailedEvent(failure),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to report reading failure for {DeviceId}.",
                failure.DeviceId);
        }
    }
}
