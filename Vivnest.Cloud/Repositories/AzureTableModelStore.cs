using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public class AzureTableModelStore : IModelStore
{
    private readonly AzureTableStore<ModelEntity> _store;

    public AzureTableModelStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<ModelEntity>(
            tableServiceClient,
            tablesOptions.Value.Models);
    }

    private static string PartitionKey(string tenantId, string siteId) => $"{tenantId}|{siteId}";

    public Task<IReadOnlyList<ModelEntity>> ListAsync(
        string tenantId, string siteId, CancellationToken cancellationToken = default)
    {
        var partitionKey = PartitionKey(tenantId, siteId);

        return _store.QueryAsync(x => x.PartitionKey == partitionKey, cancellationToken);
    }

    public Task<ModelEntity?> GetAsync(
        string tenantId, string siteId, string modelId, CancellationToken cancellationToken = default) =>
        _store.GetAsync(PartitionKey(tenantId, siteId), modelId, cancellationToken);

    public Task CreateAsync(ModelEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);

    public Task UpdateAsync(ModelEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(entity, cancellationToken);
}

public class AzureTableModelVersionStore : IModelVersionStore
{
    private readonly AzureTableStore<ModelVersionEntity> _store;

    public AzureTableModelVersionStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<ModelVersionEntity>(
            tableServiceClient,
            tablesOptions.Value.ModelVersions);
    }

    private static string PartitionKey(string tenantId, string siteId, string modelId) =>
        $"{tenantId}|{siteId}|{modelId}";

    // Zero-padded so lexical RowKey order is version order (ADR-069's D6
    // convention).
    private static string RowKey(int version) => version.ToString("D6");

    public Task<IReadOnlyList<ModelVersionEntity>> ListAsync(
        string tenantId, string siteId, string modelId, CancellationToken cancellationToken = default)
    {
        var partitionKey = PartitionKey(tenantId, siteId, modelId);

        return _store.QueryAsync(x => x.PartitionKey == partitionKey, cancellationToken);
    }

    public Task<ModelVersionEntity?> GetAsync(
        string tenantId, string siteId, string modelId, int version,
        CancellationToken cancellationToken = default) =>
        _store.GetAsync(PartitionKey(tenantId, siteId, modelId), RowKey(version), cancellationToken);

    public Task CreateAsync(ModelVersionEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);

    public Task UpdateAsync(ModelVersionEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(entity, cancellationToken);
}
