using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Cloud.Services;

public sealed class HealthMonitorService : IHealthMonitorService
{
    private readonly IDeviceHeartbeatReader _deviceHeartbeats;
    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly IOfflineDetectionRule _offlineRule;
    private readonly IRecoveryDetectionRule _recoveryRule;
    private readonly IDeviceStatusResolver _statusResolver;
    private readonly IAgentStatusResolver _agentStatusResolver;
    private readonly INotificationDispatcher _notifications;
    private readonly IAgentInstallationManagementService _agentInstallations;
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<HealthMonitorService> _logger;

    public HealthMonitorService(
        IDeviceHeartbeatReader deviceHeartbeats,
        IAgentHeartbeatReader agentHeartbeats,
        IOfflineDetectionRule offlineRule,
        IRecoveryDetectionRule recoveryRule,
        IDeviceStatusResolver statusResolver,
        IAgentStatusResolver agentStatusResolver,
        INotificationDispatcher notifications,
        IAgentInstallationManagementService agentInstallations,
        IOptions<HealthMonitorOptions> options,
        ILogger<HealthMonitorService> logger)
    {
        _deviceHeartbeats = deviceHeartbeats;
        _agentHeartbeats = agentHeartbeats;
        _offlineRule = offlineRule;
        _recoveryRule = recoveryRule;
        _statusResolver = statusResolver;
        _agentStatusResolver = agentStatusResolver;
        _notifications = notifications;
        _agentInstallations = agentInstallations;
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

        // Same PartitionKey ("{TenantId}|{SiteId}|{AgentId}") for a device
        // and its parent - they're always on the same agent - so a lookup
        // by (PartitionKey, ParentDeviceId) is enough, no cross-agent
        // scenario to handle here.
        var devicesByKey = devices.ToDictionary(
            d => (d.PartitionKey, d.RowKey));

        foreach (var device in devices)
        {
            try
            {
                agentsByKey.TryGetValue(
                    (device.TenantId, device.SiteId, device.AgentId),
                    out var agent);

                DeviceHeartbeatEntity? parentDevice = null;

                if (!string.IsNullOrWhiteSpace(device.ParentDeviceId))
                {
                    devicesByKey.TryGetValue(
                        (device.PartitionKey, device.ParentDeviceId),
                        out parentDevice);
                }

                await EvaluateAndNotifyAsync(device, agent, parentDevice, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process health check for device {DeviceId}.",
                    device.RowKey);
            }
        }

        foreach (var agent in agents)
        {
            try
            {
                await EvaluateAgentAndNotifyAsync(agent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process health check for agent {AgentId}.",
                    agent.RowKey);
            }
        }
    }

    public async Task ProcessDeviceAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Health monitor disabled.");
            return;
        }

        var device = await _deviceHeartbeats.GetAsync(partitionKey, rowKey, cancellationToken);

        if (device is null)
        {
            _logger.LogWarning(
                "DeviceHeartbeat not found {PartitionKey}/{RowKey}.",
                partitionKey,
                rowKey);

            return;
        }

        var agent = await _agentHeartbeats.GetAsync(
            new SiteScope(device.TenantId, device.SiteId).PartitionKey,
            device.AgentId,
            cancellationToken);

        DeviceHeartbeatEntity? parentDevice = null;

        if (!string.IsNullOrWhiteSpace(device.ParentDeviceId))
        {
            parentDevice = await _deviceHeartbeats.GetAsync(
                device.PartitionKey,
                device.ParentDeviceId,
                cancellationToken);
        }

        await EvaluateAndNotifyAsync(device, agent, parentDevice, cancellationToken);
    }

    public async Task ProcessAgentAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Health monitor disabled.");
            return;
        }

        var agent = await _agentHeartbeats.GetAsync(partitionKey, rowKey, cancellationToken);

        if (agent is null)
        {
            _logger.LogWarning(
                "AgentHeartbeat not found {PartitionKey}/{RowKey}.",
                partitionKey,
                rowKey);

            return;
        }

        await EvaluateAgentAndNotifyAsync(agent, cancellationToken);
    }

    private async Task EvaluateAndNotifyAsync(
        DeviceHeartbeatEntity device,
        AgentHeartbeatEntity? agent,
        DeviceHeartbeatEntity? parentDevice,
        CancellationToken cancellationToken)
    {
        var (finalStatus, agentCascade, homeAssistantCascade, parentDeviceCascade, _) =
            _statusResolver.Determine(device, agent, parentDevice);

        // The single AgentOffline/AgentRecovered notification already
        // covers every device on a down agent - firing a DeviceOffline for
        // each one too is redundant noise. Leave NotificationState
        // untouched here so it stays whatever it was before the agent went
        // down, and this device's own report (once it arrives) resolves it
        // correctly on a later pass, independent of the agent's own state.
        // Same reasoning for homeAssistantCascade/parentDeviceCascade, one
        // level down each: this device's badness is explained by its HA
        // connection or its parent device (e.g. a Tapo hub), not itself.
        if (agentCascade || homeAssistantCascade || parentDeviceCascade)
        {
            return;
        }

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

    private async Task EvaluateAgentAndNotifyAsync(
        AgentHeartbeatEntity agent,
        CancellationToken cancellationToken)
    {
        // Decision-log.md ADR-072 - best-effort, deliberately outside the
        // Online/Offline notification logic below (a heartbeat proves the
        // installation is running regardless of whether it's stale enough
        // to also count as "offline" by this method's own threshold).
        // Never allowed to break real notification processing - a failure
        // here is logged and swallowed, not rethrown.
        try
        {
            await _agentInstallations.NoteAgentHeartbeatAsync(
                agent.TenantId, agent.SiteId, agent.RowKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to update installation lifecycle for agent {AgentId}.", agent.RowKey);
        }

        var status = _agentStatusResolver.Determine(agent).Status;

        var currentNotificationState =
            Enum.TryParse<DeviceNotificationState>(agent.NotificationState, out var parsed)
                ? parsed
                : DeviceNotificationState.None;

        if (_offlineRule.ShouldNotifyOffline(status, currentNotificationState))
        {
            await _notifications.DispatchAsync(
                new Notification
                {
                    Type = NotificationTypes.AgentOffline,
                    Title = $"⚠️ Agent {agent.RowKey} is offline",
                    Message =
                        $"Last heartbeat: {agent.LastHeartbeatUtc:u}\n" +
                        $"Host: {agent.HostName}" +
                        (string.IsNullOrWhiteSpace(agent.Error) ? "" : $"\nError: {agent.Error}"),
                    Priority = NotificationPriority.Urgent
                },
                cancellationToken);

            await _agentHeartbeats.UpdateNotificationStateAsync(
                agent,
                DeviceNotificationState.OfflineNotified,
                lastOfflineNotificationUtc: DateTime.UtcNow,
                lastRecoveredUtc: agent.LastRecoveredUtc,
                cancellationToken);

            _logger.LogInformation(
                "Offline alert sent for agent {AgentId}.",
                agent.RowKey);
        }
        else if (_recoveryRule.ShouldNotifyRecovery(status, currentNotificationState))
        {
            await _notifications.DispatchAsync(
                new Notification
                {
                    Type = NotificationTypes.AgentRecovered,
                    Title = $"✅ Agent {agent.RowKey} is back online",
                    Message = $"Recovered at {DateTime.UtcNow:u}",
                    Priority = NotificationPriority.Normal
                },
                cancellationToken);

            await _agentHeartbeats.UpdateNotificationStateAsync(
                agent,
                DeviceNotificationState.None,
                lastOfflineNotificationUtc: agent.LastOfflineNotificationUtc,
                lastRecoveredUtc: DateTime.UtcNow,
                cancellationToken);

            _logger.LogInformation(
                "Recovery alert sent for agent {AgentId}.",
                agent.RowKey);
        }
    }
}
