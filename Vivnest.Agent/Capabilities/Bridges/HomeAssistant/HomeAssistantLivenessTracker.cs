using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;
using Vivnest.Agent.Capabilities.DeviceHealth;
using Vivnest.Agent.Runtime.Shell;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Bridges.HomeAssistant;

// The only liveness signal for HA-sourced devices - there's no native poll
// loop for them, so this is called both from live state_changed events and
// from a one-off REST sync on (re)connect, so a stale status doesn't survive
// an agent restart with no subsequent HA event. Deliberately does not touch
// DeviceEvent/notifications - that's HomeAssistantStateChangedHandler's job,
// and re-running it on every reconnect would spam duplicate notifications.
public sealed class HomeAssistantLivenessTracker : IHomeAssistantLivenessTracker
{
    private readonly ILogger<HomeAssistantLivenessTracker> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly IEventHandler<DeviceHeartbeatGeneratedEvent> _heartbeatHandler;

    public HomeAssistantLivenessTracker(
        ILogger<HomeAssistantLivenessTracker> logger,
        IOptions<AgentOptions> agentOptions,
        IDeviceRuntimeStateStore statusStore,
        IEventHandler<DeviceHeartbeatGeneratedEvent> heartbeatHandler)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _statusStore = statusStore;
        _heartbeatHandler = heartbeatHandler;
    }

    public async Task ReportAsync(
        string deviceId,
        DeviceType deviceType,
        string entityId,
        string? state,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isUnavailable = string.Equals(
                state,
                "unavailable",
                StringComparison.OrdinalIgnoreCase);

            var status = isUnavailable
                ? DeviceHeartbeatStatus.Offline
                : DeviceHeartbeatStatus.Healthy;

            var runtime = _statusStore.GetOrAdd(deviceId);
            var previousStatus = runtime.LastReportedStatus;

            runtime.LastActivityUtc = observedAtUtc;
            runtime.LastError = isUnavailable
                ? $"Home Assistant reports {entityId} as unavailable."
                : null;
            runtime.LastReportedStatus = status;

            // First observation since this process started, and it's
            // healthy - don't treat "we finally have a status" as a
            // transition. runtime is in-memory only, so previousStatus is
            // always null right after a restart; without this, every
            // restart would look like a fresh recovery the instant HA
            // reports the entity available, regardless of whether anything
            // actually changed. A first observation of Offline is still
            // reported - a device that's already down when the agent
            // starts shouldn't go unreported just because nothing
            // "changed" locally.
            if (previousStatus is null && status == DeviceHeartbeatStatus.Healthy)
            {
                return;
            }

            if (previousStatus == status)
            {
                return;
            }

            var heartbeat = new DeviceHeartbeat
            {
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,

                DeviceId = deviceId,
                DeviceType = deviceType,

                LastHeartbeatUtc = DateTime.UtcNow,
                LastActivityUtc = runtime.LastActivityUtc,

                // Push-based (HA notifies us), not polled - there's no
                // configured interval to report here, unlike native devices.
                ExpectedLivenessInterval = TimeSpan.Zero,
                ExpectedHeartbeatInterval = TimeSpan.Zero,

                Error = runtime.LastError,

                Status = status,
                Source = DeviceHeartbeatSource.HomeAssistant,

                // The container's own OS timezone - see Dockerfile.
                Timezone = LocalTimeZone.IanaId
            };

            await _heartbeatHandler.HandleAsync(
                new DeviceHeartbeatGeneratedEvent(heartbeat),
                cancellationToken);

            _logger.LogInformation(
                "Home Assistant liveness for {DeviceId}: status changed {Previous} -> {New}",
                deviceId,
                previousStatus,
                status);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish Home Assistant liveness heartbeat for {DeviceId} (entity {EntityId})",
                deviceId,
                entityId);
        }
    }
}
