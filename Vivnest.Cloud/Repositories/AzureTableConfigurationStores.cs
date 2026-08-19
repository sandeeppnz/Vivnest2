using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

// Thin wrappers over the same AzureTableStore<T> the publishers used to
// construct inline - the behaviour is identical, it just now sits behind an
// interface so the publishers can be tested. Both follow the shape every
// other AzureTable*Store in this folder already uses; the body is shared
// here because the only thing that differs between the two is which
// TablesOptions name the table comes from.
public abstract class AzureTableConfigurationStore<TEntity> : IConfigurationStateStore<TEntity>
    where TEntity : class, ITableEntity, IConfigurationStateEntity
{
    private readonly AzureTableStore<TEntity> _store;

    protected AzureTableConfigurationStore(TableServiceClient tableServiceClient, string tableName) =>
        _store = new AzureTableStore<TEntity>(tableServiceClient, tableName);

    public Task<TEntity?> GetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        _store.GetAsync(partitionKey, rowKey, cancellationToken);

    public Task UpsertAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);

    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(entity, cancellationToken);
}

public sealed class AzureTableAgentConfigurationStore
    : AzureTableConfigurationStore<AgentConfigurationEntity>, IAgentConfigurationStore
{
    public AzureTableAgentConfigurationStore(
        TableServiceClient tableServiceClient, IOptions<TablesOptions> tablesOptions)
        : base(tableServiceClient, tablesOptions.Value.AgentConfiguration)
    {
    }
}

public sealed class AzureTableDeviceConfigurationStore
    : AzureTableConfigurationStore<DeviceConfigurationEntity>, IDeviceConfigurationStore
{
    public AzureTableDeviceConfigurationStore(
        TableServiceClient tableServiceClient, IOptions<TablesOptions> tablesOptions)
        : base(tableServiceClient, tablesOptions.Value.DeviceConfiguration)
    {
    }
}
