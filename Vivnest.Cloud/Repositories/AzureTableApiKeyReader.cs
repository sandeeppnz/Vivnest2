using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

public class AzureTableApiKeyReader : IApiKeyReader
{
    private const string InfoRowKey = "info";

    private readonly AzureTableStore<ApiKeyEntity> _store;

    public AzureTableApiKeyReader(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<ApiKeyEntity>(
            tableServiceClient,
            tablesOptions.Value.ApiKeys);
    }

    public Task<ApiKeyEntity?> GetByHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(keyHash, InfoRowKey, cancellationToken);
    }
}
