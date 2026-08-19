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
// other AzureTable*Store in this folder already uses.
public sealed class AzureTableAgentConfigurationStore : IAgentConfigurationStore
{
    private readonly AzureTableStore<AgentConfigurationEntity> _store;

    public AzureTableAgentConfigurationStore(
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions)
    {
        _store = new AzureTableStore<AgentConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.AgentConfiguration);
    }

    public Task<AgentConfigurationEntity?> GetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        _store.GetAsync(partitionKey, rowKey, cancellationToken);

    public Task UpsertAsync(
        AgentConfigurationEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);

    public Task UpdateAsync(
        AgentConfigurationEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(entity, cancellationToken);
}

public sealed class AzureTableDeviceConfigurationStore : IDeviceConfigurationStore
{
    private readonly AzureTableStore<DeviceConfigurationEntity> _store;

    public AzureTableDeviceConfigurationStore(
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions)
    {
        _store = new AzureTableStore<DeviceConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.DeviceConfiguration);
    }

    public Task<DeviceConfigurationEntity?> GetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        _store.GetAsync(partitionKey, rowKey, cancellationToken);

    public Task UpsertAsync(
        DeviceConfigurationEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);

    public Task UpdateAsync(
        DeviceConfigurationEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(entity, cancellationToken);
}
