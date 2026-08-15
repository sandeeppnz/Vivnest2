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

    // Decision-log.md ADR-077 - a real bug, found live: this used to
    // discard the response's new ETag, leaving entity.ETag stale on the
    // in-memory object. Harmless as long as nothing calls UpdateAsync
    // twice on the same entity within one request - but HealthMonitorService's
    // new ConfigurationApplyFailed-clear path does exactly that (once for
    // NotificationState, once for LastNotifiedConfigurationLoadError), and
    // the second call's stale ETag was silently rejected (412), logged,
    // and swallowed by the per-agent try/catch in RunAsync - confirmed
    // live against real Azure data before this fix.
    public async Task UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        var response = await _table.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Replace,
            cancellationToken);

        if (response.Headers.ETag is { } newETag)
        {
            entity.ETag = newETag;
        }
    }

    public async Task DeleteAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _table.DeleteEntityAsync(partitionKey, rowKey, ETag.All, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - idempotent, same tolerance this store already
            // has elsewhere. Callers that need a definitive "did it exist"
            // 404 check via GetAsync first.
        }
    }
}
