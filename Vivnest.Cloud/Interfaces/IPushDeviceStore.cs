using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IPushDeviceStore
{
    // Every registered phone in every tenant. For the push channel only:
    // Notification carries no tenant, so an alert goes to all of them, the
    // same way Telegram's single chat receives everything (ADR-129).
    Task<IReadOnlyList<PushDeviceEntity>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushDeviceEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<PushDeviceEntity?> GetAsync(
        string tenantId,
        string siteId,
        string pushDeviceId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        PushDeviceEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string tenantId,
        string siteId,
        string pushDeviceId,
        CancellationToken cancellationToken = default);
}
