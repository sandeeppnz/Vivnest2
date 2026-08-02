using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

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
}
