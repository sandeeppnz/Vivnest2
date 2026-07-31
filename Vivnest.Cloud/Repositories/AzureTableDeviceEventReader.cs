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
}
