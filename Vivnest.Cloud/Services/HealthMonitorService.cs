using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Cloud.Services;

public sealed class HealthMonitorService : IHealthMonitorService
{
    private readonly IDeviceHeartbeatRepository _deviceHeartbeats;
    private readonly IAgentHeartbeatRepository _agentHeartbeats;
    private readonly IOfflineDetectionRule _offlineRule;
    private readonly IRecoveryDetectionRule _recoveryRule;
    private readonly INotificationDispatcher _notifications;
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<HealthMonitorService> _logger;

    public HealthMonitorService(
        IDeviceHeartbeatRepository deviceHeartbeats,
        IAgentHeartbeatRepository agentHeartbeats,
        IOfflineDetectionRule offlineRule,
        IRecoveryDetectionRule recoveryRule,
        INotificationDispatcher notifications,
        IOptions<HealthMonitorOptions> options,
        ILogger<HealthMonitorService> logger)
    {
        _deviceHeartbeats = deviceHeartbeats;
        _agentHeartbeats = agentHeartbeats;
        _offlineRule = offlineRule;
        _recoveryRule = recoveryRule;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Health monitor disabled.");
            return;
        }

        var devices = await _deviceHeartbeats.GetAllAsync(cancellationToken);
        var agents = await _agentHeartbeats.GetAllAsync(cancellationToken);

        var agentsByKey = agents.ToDictionary(
            a => (a.TenantId, a.SiteId, a.AgentId));

        foreach (var device in devices)
        {
            try
            {
                await ProcessDeviceAsync(device, agentsByKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process health check for device {DeviceId}.",
                    device.RowKey);
            }
        }
    }

    private async Task ProcessDeviceAsync(
        DeviceHeartbeatEntity device,
        IReadOnlyDictionary<(string TenantId, string SiteId, string AgentId), AgentHeartbeatEntity> agentsByKey,
        CancellationToken cancellationToken)
    {
        agentsByKey.TryGetValue(
            (device.TenantId, device.SiteId, device.AgentId),
            out var agent);

        var finalStatus = DetermineFinalStatus(device, agent);

        var currentNotificationState =
            Enum.TryParse<DeviceNotificationState>(device.NotificationState, out var parsed)
                ? parsed
                : DeviceNotificationState.None;

        if (_offlineRule.ShouldNotifyOffline(finalStatus, currentNotificationState))
        {
            await _notifications.DispatchAsync(
                new Notification
                {
                    Type = NotificationTypes.DeviceOffline,
                    Title = $"⚠️ Device {device.RowKey} is offline",
                    Message =
                        $"Last heartbeat: {device.LastHeartbeatUtc:u}\n" +
                        $"Status: {finalStatus}" +
                        (string.IsNullOrWhiteSpace(device.Error) ? "" : $"\nError: {device.Error}"),
                    Priority = NotificationPriority.Urgent
                },
                cancellationToken);

            await _deviceHeartbeats.UpdateNotificationStateAsync(
                device,
                DeviceNotificationState.OfflineNotified,
                lastOfflineNotificationUtc: DateTime.UtcNow,
                lastRecoveredUtc: device.LastRecoveredUtc,
                cancellationToken);

            _logger.LogInformation(
                "Offline alert sent for device {DeviceId}.",
                device.RowKey);
        }
        else if (_recoveryRule.ShouldNotifyRecovery(finalStatus, currentNotificationState))
        {
            await _notifications.DispatchAsync(
                new Notification
                {
                    Type = NotificationTypes.DeviceRecovered,
                    Title = $"✅ Device {device.RowKey} is back online",
                    Message = $"Recovered at {DateTime.UtcNow:u}",
                    Priority = NotificationPriority.Normal
                },
                cancellationToken);

            await _deviceHeartbeats.UpdateNotificationStateAsync(
                device,
                DeviceNotificationState.None,
                lastOfflineNotificationUtc: device.LastOfflineNotificationUtc,
                lastRecoveredUtc: DateTime.UtcNow,
                cancellationToken);

            _logger.LogInformation(
                "Recovery alert sent for device {DeviceId}.",
                device.RowKey);
        }
    }

    private DeviceHeartbeatStatus DetermineFinalStatus(
        DeviceHeartbeatEntity device,
        AgentHeartbeatEntity? agent)
    {
        if (agent is null)
            return DeviceHeartbeatStatus.Unknown;

        var staleAfter = agent.HeartbeatInterval > TimeSpan.Zero
            ? agent.HeartbeatInterval * _options.AgentStaleMultiplier
            : TimeSpan.FromMinutes(5);

        var agentElapsed = DateTime.UtcNow - agent.LastHeartbeatUtc;

        if (agentElapsed > staleAfter)
            return DeviceHeartbeatStatus.Offline;

        // Agent is alive, so trust the device-level status it last reported.
        return Enum.TryParse<DeviceHeartbeatStatus>(device.Status, out var status)
            ? status
            : DeviceHeartbeatStatus.Unknown;
    }
}
