using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Core.DataStores;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Shared;
using Vivnest.Domain.Sites;
using Vivnest.Infrastructure.Azure;
using Vivnest.Cloud.Options;

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
    private readonly IAgentCommandManagementService _agentCommands;
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<HealthMonitorService> _logger;

    // Decision-log.md ADR-077 - Cloud-side event persistence goes through
    // a direct AzureTableStore<T>, not IAgentEventWriter/IDeviceEventWriter
    // (those live in Vivnest.Infrastructure, an Agent-side-only project
    // Vivnest.Cloud doesn't reference) - same pattern
    // DeviceRuntimeConfigurationPublisher's own ConfigPublished/ConfigRolledBack
    // audit-trail writes already established for Cloud-originated events.
    private readonly AzureTableStore<DeviceEventEntity> _deviceEvents;
    private readonly AzureTableStore<AgentEventEntity> _agentEvents;

    public HealthMonitorService(
        IDeviceHeartbeatReader deviceHeartbeats,
        IAgentHeartbeatReader agentHeartbeats,
        IOfflineDetectionRule offlineRule,
        IRecoveryDetectionRule recoveryRule,
        IDeviceStatusResolver statusResolver,
        IAgentStatusResolver agentStatusResolver,
        INotificationDispatcher notifications,
        IAgentInstallationManagementService agentInstallations,
        IAgentCommandManagementService agentCommands,
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions,
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
        _agentCommands = agentCommands;
        _deviceEvents = new AzureTableStore<DeviceEventEntity>(tableServiceClient, tablesOptions.Value.DeviceEvents);
        _agentEvents = new AzureTableStore<AgentEventEntity>(tableServiceClient, tablesOptions.Value.AgentEvents);
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

            // Decision-log.md ADR-077 - persisted alongside the Telegram
            // notification above, not instead of it, gated by the exact
            // same NotificationState transition so this fires once per
            // real Offline event, not every health-check tick.
            await PersistDeviceEventAsync(
                device,
                DeviceEventTypes.DeviceOffline,
                EventSeverity.Warning,
                new { device.LastHeartbeatUtc, Status = finalStatus.ToString(), device.Error },
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

            await PersistDeviceEventAsync(
                device,
                DeviceEventTypes.DeviceRecovered,
                EventSeverity.Information,
                new { RecoveredAtUtc = DateTime.UtcNow },
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

        // Decision-log.md ADR-079 - same best-effort convention as
        // NoteAgentHeartbeatAsync above: confirms completion for any
        // RestartAgent command still Dispatched/Received for this Agent,
        // by correlating this fresh heartbeat's StartedUtc against the
        // command's DispatchedUtc. Never allowed to break real
        // notification processing.
        try
        {
            await _agentCommands.EvaluateAgentCommandsAsync(agent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to evaluate agent commands for agent {AgentId}.", agent.RowKey);
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

            await PersistAgentEventAsync(
                agent,
                AgentEventTypes.AgentOffline,
                EventSeverity.Warning,
                new { agent.LastHeartbeatUtc, Status = status.ToString(), agent.Error },
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

            await PersistAgentEventAsync(
                agent,
                AgentEventTypes.AgentRecovered,
                EventSeverity.Information,
                new { RecoveredAtUtc = DateTime.UtcNow },
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

        await EvaluateConfigurationApplyFailedAsync(agent, cancellationToken);
    }

    // Decision-log.md ADR-077 - independent of the Online/Offline logic
    // above (an agent can be Online and still have a config it can't
    // apply), gated by its own LastNotifiedConfigurationLoadError field
    // rather than NotificationState, so this fires once when a *new* or
    // *changed* error first appears and stays quiet on every subsequent
    // tick until the error text actually changes or clears.
    private async Task EvaluateConfigurationApplyFailedAsync(
        AgentHeartbeatEntity agent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agent.ConfigurationLoadError))
        {
            if (agent.LastNotifiedConfigurationLoadError != null)
            {
                await _agentHeartbeats.UpdateLastNotifiedConfigurationLoadErrorAsync(
                    agent, null, cancellationToken);
            }

            return;
        }

        if (string.Equals(
            agent.ConfigurationLoadError, agent.LastNotifiedConfigurationLoadError, StringComparison.Ordinal))
        {
            return;
        }

        await _notifications.DispatchAsync(
            new Notification
            {
                Type = NotificationTypes.ConfigurationApplyFailed,
                Title = $"⚠️ Agent {agent.RowKey} failed to apply its configuration",
                Message = $"Error: {agent.ConfigurationLoadError}",
                Priority = NotificationPriority.Urgent
            },
            cancellationToken);

        await PersistAgentEventAsync(
            agent,
            AgentEventTypes.ConfigurationApplyFailed,
            EventSeverity.Critical,
            new { Error = agent.ConfigurationLoadError },
            cancellationToken);

        await _agentHeartbeats.UpdateLastNotifiedConfigurationLoadErrorAsync(
            agent, agent.ConfigurationLoadError, cancellationToken);

        _logger.LogInformation(
            "Configuration apply failure alert sent for agent {AgentId}.",
            agent.RowKey);
    }

    private Task PersistDeviceEventAsync(
        DeviceHeartbeatEntity device,
        string eventType,
        EventSeverity severity,
        object data,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var entity = new DeviceEventEntity
        {
            PartitionKey = device.RowKey,
            RowKey = EventRowKey.New(now),
            TenantId = device.TenantId,
            SiteId = device.SiteId,
            AgentId = device.AgentId,
            DeviceId = device.RowKey,
            DeviceType = device.DeviceType,
            EventType = eventType,
            Severity = severity.ToString(),
            OccurredAtUtc = now,
            Payload = JsonSerializer.Serialize(data)
        };

        return _deviceEvents.UpsertAsync(entity, cancellationToken);
    }

    private Task PersistAgentEventAsync(
        AgentHeartbeatEntity agent,
        string eventType,
        EventSeverity severity,
        object data,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var entity = new AgentEventEntity
        {
            PartitionKey = agent.RowKey,
            RowKey = EventRowKey.New(now),
            TenantId = agent.TenantId,
            SiteId = agent.SiteId,
            AgentId = agent.RowKey,
            EventType = eventType,
            Severity = severity.ToString(),
            OccurredAtUtc = now,
            Payload = JsonSerializer.Serialize(data)
        };

        return _agentEvents.UpsertAsync(entity, cancellationToken);
    }
}
