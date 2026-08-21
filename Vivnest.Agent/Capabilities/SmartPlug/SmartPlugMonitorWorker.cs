using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.SmartPlug.Models;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.SmartPlug;

public sealed class SmartPlugMonitorWorker : BackgroundService
{
    private readonly ISmartPlugMonitorService _monitorService;
    private readonly IEventDispatcher _dispatcher;
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<SmartPlugMonitorWorker> _logger;

    public SmartPlugMonitorWorker(
        ISmartPlugMonitorService monitorService,
        IEventDispatcher dispatcher,
        IDeviceRuntimeStateStore statusStore,
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

        // Lives for the lifetime of this device's loop - null until the
        // first successful read, same as DeviceRuntimeState.LastReportedStatus
        // tracks the last DeviceHeartbeat status for change detection.
        bool? lastKnownIsOn = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var dueForReading =
                plugOptions.Schedule.Interval <= TimeSpan.Zero ||
                runtime.LastCaptureUtc is not { } lastReadUtc ||
                DateTime.UtcNow - lastReadUtc >= plugOptions.Schedule.Interval;

            if (dueForReading)
            {
                lastKnownIsOn = await ReadAsync(plugOptions, runtime, lastKnownIsOn, stoppingToken);
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

    private async Task<bool?> ReadAsync(
        DeviceOptions plugOptions,
        DeviceRuntimeState runtime,
        bool? lastKnownIsOn,
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

            var isOn = result.State?.IsOn;

            if (isOn.HasValue && isOn != lastKnownIsOn)
            {
                await PublishPowerStateChangedSafeAsync(
                    plugOptions.DeviceId,
                    isOn.Value,
                    result.ReadAtUtc,
                    stoppingToken);

                return isOn;
            }

            return lastKnownIsOn;
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

            return lastKnownIsOn;
        }
    }

    private async Task PublishPowerStateChangedSafeAsync(
        string deviceId,
        bool isOn,
        DateTime changedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new SmartPlugPowerStateChangedEvent(deviceId, isOn, changedAtUtc),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to report power state change for {DeviceId}.",
                deviceId);
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
