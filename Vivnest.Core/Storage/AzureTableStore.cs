using System.Linq.Expressions;
using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.Storage;

public sealed class AzureTableStore<T> where T : class, ITableEntity
{
    private readonly TableClient _table;

    public AzureTableStore(TableServiceClient tableServiceClient, string tableName)
    {
        _table = tableServiceClient.GetTableClient(tableName);
        _table.CreateIfNotExists();
    }

    public async Task<T> UpsertAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        await _table.UpsertEntityAsync(
            entity,
            TableUpdateMode.Replace,
            cancellationToken);

        return entity;
    }

    public async Task<T?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<T>(
                partitionKey,
                rowKey,
                cancellationToken: cancellationToken);

            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync(
        Expression<Func<T, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();

        var query = filter is null
            ? _table.QueryAsync<T>(cancellationToken: cancellationToken)
            : _table.QueryAsync(filter, cancellationToken: cancellationToken);

        await foreach (var entity in query)
        {
            results.Add(entity);
        }

        return results;
    }

    public async Task UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        await _table.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Replace,
            cancellationToken);
    }
}
