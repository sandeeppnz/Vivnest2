using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Options;
using Vivnest.Domain.Sites;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public sealed class AzureTablePushDeviceStore : IPushDeviceStore
{
    private readonly AzureTableStore<PushDeviceEntity> _store;

    public AzureTablePushDeviceStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<PushDeviceEntity>(
            tableServiceClient,
            tablesOptions.Value.PushDevices);
    }

    public Task<IReadOnlyList<PushDeviceEntity>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        _store.QueryAsync(null, cancellationToken);

    public Task<IReadOnlyList<PushDeviceEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(x => x.PartitionKey == partitionKey, cancellationToken);
    }

    public Task<PushDeviceEntity?> GetAsync(
        string tenantId,
        string siteId,
        string pushDeviceId,
        CancellationToken cancellationToken = default) =>
        _store.GetAsync(new SiteScope(tenantId, siteId).PartitionKey, pushDeviceId, cancellationToken);

    public Task UpsertAsync(
        PushDeviceEntity entity,
        CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);

    public Task DeleteAsync(
        string tenantId,
        string siteId,
        string pushDeviceId,
        CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(new SiteScope(tenantId, siteId).PartitionKey, pushDeviceId, cancellationToken);
}
