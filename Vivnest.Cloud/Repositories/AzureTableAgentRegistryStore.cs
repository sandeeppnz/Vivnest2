using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableAgentRegistryStore : IAgentRegistryStore
{
    private readonly AzureTableStore<AgentRegistryEntity> _store;

    public AzureTableAgentRegistryStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentRegistryEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentRegistry);
    }

    private static string PartitionKey(string tenantId, string siteId) => $"{tenantId}|{siteId}";

    public Task<IReadOnlyList<AgentRegistryEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = PartitionKey(tenantId, siteId);

        return _store.QueryAsync(x => x.PartitionKey == partitionKey, cancellationToken);
    }

    public Task<AgentRegistryEntity?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(PartitionKey(tenantId, siteId), agentId, cancellationToken);
    }

    public Task CreateAsync(
        AgentRegistryEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        AgentRegistryEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }

    public Task DeleteAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(PartitionKey(tenantId, siteId), agentId, cancellationToken);
    }
}
