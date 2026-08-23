using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public class AzureTableInstallationTokenStore : IInstallationTokenStore
{
    private const string InfoRowKey = "info";

    private readonly AzureTableStore<AgentInstallationTokenEntity> _store;

    public AzureTableInstallationTokenStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentInstallationTokenEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentInstallationTokens);
    }

    public Task<AgentInstallationTokenEntity?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(tokenHash, InfoRowKey, cancellationToken);
    }

    public Task CreateAsync(
        AgentInstallationTokenEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        AgentInstallationTokenEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
