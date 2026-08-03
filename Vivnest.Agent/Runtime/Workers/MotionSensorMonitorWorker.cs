using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.MotionSensor.Models;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Runtime.Workers;

// Unlike SmartPlugMonitorWorker, there's no separate liveness-probe/full-read
// split here - a motion sensor read is already as cheap as a probe (one
// control_child round trip), so every tick does a full read.
public sealed class MotionSensorMonitorWorker : BackgroundService
{
    private readonly IMotionSensorMonitorService _monitorService;
    private readonly IEventDispatcher _dispatcher;
    private readonly ICaptureStatusStore _statusStore;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<MotionSensorMonitorWorker> _logger;

    public MotionSensorMonitorWorker(
        IMotionSensorMonitorService monitorService,
        IEventDispatcher dispatcher,
        ICaptureStatusStore statusStore,
        IDeviceRuntimeStore deviceRegistry,
        IOptions<AgentOptions> agentOptions,
        ILogger<MotionSensorMonitorWorker> logger)
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
        _logger.LogInformation("Motion Sensor Monitor Worker started.");

        var sensors = _deviceRegistry.GetDevices()
            .Where(d => d.Type == DeviceType.MotionSensor)
            .ToList();

        if (sensors.Count == 0)
        {
            _logger.LogWarning(
                "No enabled motion sensors configured. Motion Sensor Monitor Worker has nothing to do.");

            return;
        }

        var tasks = sensors.Select(sensor =>
            RunMonitorLoopAsync(sensor, stoppingToken));

        await Task.WhenAll(tasks);
    }

    private async Task RunMonitorLoopAsync(
        DeviceOptions sensorOptions,
        CancellationToken stoppingToken)
    {
        var runtime = _statusStore.GetOrAdd(sensorOptions.DeviceId);

        // Lives for the lifetime of this device's loop - null until the
        // first successful read, same pattern SmartPlugMonitorWorker uses
        // for lastKnownIsOn.
        bool? lastKnownDetected = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            lastKnownDetected = await ReadAsync(
                sensorOptions,
                runtime,
                lastKnownDetected,
                stoppingToken);

            var delay = sensorOptions.LivenessInterval;

            _logger.LogInformation(
                "Device {DeviceId} sleeping for {Delay}. Current UTC={Now:u}. Next check UTC={Next:u}",
                sensorOptions.DeviceId,
                delay,
                DateTime.UtcNow,
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<bool?> ReadAsync(
        DeviceOptions sensorOptions,
        DeviceRuntimeState runtime,
        bool? lastKnownDetected,
        CancellationToken stoppingToken)
    {
        var result = await _monitorService.ReadAsync(sensorOptions, stoppingToken);

        if (result.Success)
        {
            runtime.LastCaptureUtc = result.ReadAtUtc;
            runtime.LastActivityUtc = result.ReadAtUtc;
            runtime.LastError = null;

            var dueForBatteryReport =
                sensorOptions.BatteryReportInterval <= TimeSpan.Zero ||
                runtime.LastBatteryReportUtc is not { } lastReportUtc ||
                result.ReadAtUtc - lastReportUtc >= sensorOptions.BatteryReportInterval;

            if (dueForBatteryReport)
            {
                runtime.LastBatteryReportUtc = result.ReadAtUtc;

                await PublishBatteryReportedSafeAsync(result, stoppingToken);
            }

            var detected = result.State?.Detected;

            if (detected.HasValue && detected != lastKnownDetected)
            {
                await PublishStateChangedSafeAsync(
                    sensorOptions.DeviceId,
                    detected.Value,
                    result.ReadAtUtc,
                    stoppingToken);

                return detected;
            }

            return lastKnownDetected;
        }
        else
        {
            runtime.LastFailureUtc = DateTime.UtcNow;
            runtime.LastError = result.Error;

            await PublishReadingFailedSafeAsync(
                new MotionSensorReadingFailureData(
                    _agentOptions.AgentId,
                    sensorOptions.DeviceId,
                    DateTime.UtcNow,
                    result.ErrorCode,
                    result.Error),
                stoppingToken);

            _logger.LogWarning(
                "Motion sensor reading failed for {DeviceId}: {Error}",
                sensorOptions.DeviceId,
                result.Error);

            return lastKnownDetected;
        }
    }

    private async Task PublishStateChangedSafeAsync(
        string deviceId,
        bool detected,
        DateTime changedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new MotionSensorStateChangedEvent(deviceId, detected, changedAtUtc),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to report motion state change for {DeviceId}.",
                deviceId);
        }
    }

    private async Task PublishBatteryReportedSafeAsync(
        MotionSensorReadingResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new MotionSensorBatteryReportedEvent(result),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to report battery status for {DeviceId}.",
                result.DeviceId);
        }
    }

    private async Task PublishReadingFailedSafeAsync(
        MotionSensorReadingFailureData failure,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new MotionSensorReadingFailedEvent(failure),
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
