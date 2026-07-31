using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableAgentHeartbeatRepository : IAgentHeartbeatRepository
{
    private readonly AzureTableStore<AgentHeartbeatEntity> _store;

    public AzureTableAgentHeartbeatRepository(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentHeartbeatEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentHeartbeat);
    }

    public Task<IReadOnlyList<AgentHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(cancellationToken: cancellationToken);
    }
}
