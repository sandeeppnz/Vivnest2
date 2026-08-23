using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Domain.Sites;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public class AzureTableAgentCommandStore : IAgentCommandStore
{
    private readonly AzureTableStore<AgentCommandEntity> _store;

    public AzureTableAgentCommandStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentCommandEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentCommands);
    }

    public Task<AgentCommandEntity?> GetAsync(
        string tenantId,
        string siteId,
        string commandId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(new SiteScope(tenantId, siteId).PartitionKey, commandId, cancellationToken);
    }

    public Task<IReadOnlyList<AgentCommandEntity>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.AgentId == agentId,
            cancellationToken);
    }

    public Task<IReadOnlyList<AgentCommandEntity>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }

    public Task CreateAsync(
        AgentCommandEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        AgentCommandEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
