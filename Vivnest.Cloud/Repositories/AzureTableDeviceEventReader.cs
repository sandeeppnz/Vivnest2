using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceEventReader : IDeviceEventReader
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    public AzureTableDeviceEventReader(
        IOptions<DeviceEventOptions> options,
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _enabled = options.Value.Enabled;

        if (!_enabled)
        {
            return;
        }

        var tableSettings = tablesOptions.Value;
        _table = tableServiceClient.GetTableClient(tableSettings.DeviceEvents);

        _table.CreateIfNotExists();
    }
    public async Task<DeviceEventEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return null;

        try
        {
            var response = await _table!.GetEntityAsync<DeviceEventEntity>(
                partitionKey,
                rowKey,
                cancellationToken: cancellationToken);

            return response.Value;
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            return null;
        }
    }
    public async Task<IReadOnlyList<DeviceEventEntity>> GetByDeviceAsync(
        string tenantId,
        string siteId,
        string deviceId,
        string? eventType,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return Array.Empty<DeviceEventEntity>();

        var query = _table!.QueryAsync<DeviceEventEntity>(
            e => e.PartitionKey == deviceId
                && e.TenantId == tenantId
                && e.SiteId == siteId,
            cancellationToken: cancellationToken);

        var results = new List<DeviceEventEntity>();

        await foreach (var entity in query)
        {
            if (eventType != null && entity.EventType != eventType)
                continue;

            results.Add(entity);
        }

        return results
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<DeviceEventEntity>> GetByDeviceAndDateRangeAsync(
        string tenantId,
        string siteId,
        string deviceId,
        string? eventType,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return Array.Empty<DeviceEventEntity>();

        // RowKey is "{OccurredAtUtc:yyyyMMddHHmmssfff}-{EventId}", so it's
        // already sorted by time within the partition - a RowKey range
        // filter narrows the query at the table service instead of
        // fetching every row for this device and filtering client-side.
        var fromRowKey = fromUtc.ToString("yyyyMMddHHmmssfff");
        var toRowKey = toUtc.ToString("yyyyMMddHHmmssfff");

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {deviceId} and RowKey ge {fromRowKey} and RowKey lt {toRowKey}");

        var query = _table!.QueryAsync<DeviceEventEntity>(
            filter,
            cancellationToken: cancellationToken);

        var results = new List<DeviceEventEntity>();

        await foreach (var entity in query)
        {
            if (entity.TenantId != tenantId || entity.SiteId != siteId)
                continue;

            if (eventType != null && entity.EventType != eventType)
                continue;

            results.Add(entity);
        }

        return results
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();
    }

    public async Task MarkProcessingAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(
            partitionKey,
            rowKey,
            cancellationToken);

        if (entity == null)
            return;

        entity.ProcessingStatus = DeviceEventProcessingStatus.Processing.ToString();

        await _table!.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task MarkCompletedAsync(
     string partitionKey,
     string rowKey,
     CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(
            partitionKey,
            rowKey,
            cancellationToken);

        if (entity == null)
            return;

        entity.ProcessingStatus = DeviceEventProcessingStatus.Completed.ToString();
        entity.ProcessedAtUtc = DateTime.UtcNow;
        entity.ProcessingLastError = null;

        await _table!.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        string partitionKey,
        string rowKey,
        string error,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(
            partitionKey,
            rowKey,
            cancellationToken);

        if (entity == null)
            return;

        entity.ProcessingStatus = DeviceEventProcessingStatus.Failed.ToString();
        entity.ProcessingRetryCount++;
        entity.ProcessingLastError = error;

        await _table!.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return 0;

        var query = _table!.QueryAsync<DeviceEventEntity>(
            e => e.OccurredAtUtc < cutoffUtc,
            cancellationToken: cancellationToken);

        var deleted = 0;

        await foreach (var entity in query)
        {
            await _table.DeleteEntityAsync(
                entity.PartitionKey,
                entity.RowKey,
                cancellationToken: cancellationToken);

            deleted++;
        }

        return deleted;
    }
}
