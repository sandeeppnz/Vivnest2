using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentHeartbeatReader
{
    Task<IReadOnlyList<AgentHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<AgentHeartbeatEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentHeartbeatEntity>> GetByTenantAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task UpdateNotificationStateAsync(
        AgentHeartbeatEntity entity,
        DeviceNotificationState notificationState,
        DateTime? lastOfflineNotificationUtc,
        DateTime? lastRecoveredUtc,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-077 - a separate update, not folded into
    // UpdateNotificationStateAsync above: ConfigurationApplyFailed is an
    // independent transition from Online/Offline (an agent can go offline
    // and have a config error at the same time, or recover while the
    // error persists), so it needs its own gate rather than overloading
    // NotificationState.
    Task UpdateLastNotifiedConfigurationLoadErrorAsync(
        AgentHeartbeatEntity entity,
        string? lastNotifiedConfigurationLoadError,
        CancellationToken cancellationToken = default);
}
