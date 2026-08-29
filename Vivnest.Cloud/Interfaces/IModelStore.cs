using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IModelStore
{
    Task<IReadOnlyList<ModelEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<ModelEntity?> GetAsync(
        string tenantId,
        string siteId,
        string modelId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        ModelEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ModelEntity entity,
        CancellationToken cancellationToken = default);
}

public interface IModelVersionStore
{
    Task<IReadOnlyList<ModelVersionEntity>> ListAsync(
        string tenantId,
        string siteId,
        string modelId,
        CancellationToken cancellationToken = default);

    Task<ModelVersionEntity?> GetAsync(
        string tenantId,
        string siteId,
        string modelId,
        int version,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        ModelVersionEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ModelVersionEntity entity,
        CancellationToken cancellationToken = default);
}
