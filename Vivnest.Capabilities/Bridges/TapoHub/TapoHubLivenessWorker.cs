using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.Hubs;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Capabilities.Bridges.TapoHub;

// Not a device-owning capability the way Camera/SmartPlug/MotionSensor are
// - a hub has no ability of its own, it's what a motion sensor's
// DeviceOptions.ParentDeviceId cascade (see DeviceStatusResolver) depends
// on having real data. Only job here is populating LastActivityUtc so the
// already-generic PlatformDeviceHeartbeatWorker can report Online/Offline for it
// like any other device - no separate heartbeat-publishing logic needed,
// unlike HomeAssistantLivenessTracker (which bypasses PlatformDeviceHeartbeatWorker
// entirely because HA is push-based, not polled).
public sealed class TapoHubLivenessWorker : BackgroundService
{
    private readonly ITapoHubReachabilityChecker _reachabilityChecker;
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ILogger<TapoHubLivenessWorker> _logger;

    public TapoHubLivenessWorker(
        ITapoHubReachabilityChecker reachabilityChecker,
        IDeviceRuntimeStateStore statusStore,
        IDeviceRuntimeStore deviceRegistry,
        ILogger<TapoHubLivenessWorker> logger)
    {
        _reachabilityChecker = reachabilityChecker;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tapo Hub Liveness Worker started.");

        var hubs = _deviceRegistry.GetDevices()
            .Where(d => d.Type == DeviceType.Hub)
            .ToList();

        if (hubs.Count == 0)
        {
            _logger.LogInformation("No Hub devices configured. Tapo Hub Liveness Worker has nothing to do.");
            return;
        }

        var tasks = hubs.Select(hub => RunLoopAsync(hub, stoppingToken));

        await Task.WhenAll(tasks);
    }

    private async Task RunLoopAsync(
        DeviceOptions hub,
        CancellationToken stoppingToken)
    {
        var runtime = _statusStore.GetOrAdd(hub.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeAsync(hub, runtime, stoppingToken);

            var delay = hub.LivenessInterval > TimeSpan.Zero
                ? hub.LivenessInterval
                : TimeSpan.FromMinutes(1);

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ProbeAsync(
        DeviceOptions hub,
        DeviceRuntimeState runtime,
        CancellationToken stoppingToken)
    {
        try
        {
            var reachable = await _reachabilityChecker.CheckReachabilityAsync(
                hub,
                stoppingToken);

            if (reachable)
            {
                runtime.LastActivityUtc = DateTime.UtcNow;

                _logger.LogDebug(
                    "Liveness probe succeeded for {DeviceId}.",
                    hub.DeviceId);
            }
            else
            {
                _logger.LogWarning(
                    "Liveness probe failed for {DeviceId}: hub unreachable.",
                    hub.DeviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Liveness probe errored for {DeviceId}.",
                hub.DeviceId);
        }
    }
}
