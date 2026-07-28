using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Entities;
using Vivnest.Core.Options;

namespace Vivnest.Cloud.Repositories;

public class AzureTableDeviceEventRepository : IDeviceEventRepository
{
    private readonly TableClient _table;

    public AzureTableDeviceEventRepository(
        IOptions<DeviceEventOptions> options,
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        var tableSettings = tablesOptions.Value; 
        _table = tableServiceClient.GetTableClient(tableSettings.DeviceEvents);

        _table.CreateIfNotExists();
    }
    public async Task<DeviceEventEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<DeviceEventEntity>(
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

        entity.ProcessingStatus = "Processing";

        await _table.UpdateEntityAsync(
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

        entity.ProcessingStatus = "Completed";
        entity.ProcessedAtUtc = DateTime.UtcNow;
        entity.ProcessingLastError = null;

        await _table.UpdateEntityAsync(
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

        entity.ProcessingStatus = "Failed";
        entity.ProcessingRetryCount++;
        entity.ProcessingLastError = error;

        await _table.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Replace,
            cancellationToken);
    }
}
