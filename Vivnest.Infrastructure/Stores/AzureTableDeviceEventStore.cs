using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Core.Entities;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Stores;

public sealed class AzureTableDeviceEventStore : IDeviceEventStore
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    public AzureTableDeviceEventStore(
        IOptions<DeviceEventOptions> deviceEventOptions, IOptions<TablesOptions> tablesOptions, IOptions<StorageOptions> storageOptions)
    {
        var deviceEventSettings = deviceEventOptions.Value;
        var tablesSettings = tablesOptions.Value;
        var storageSettings = storageOptions.Value;

        _enabled = deviceEventSettings.Enabled;

        if (!_enabled)
        {
            return;
        }

        var service = new TableServiceClient(
            storageSettings.ConnectionString);

        _table = service.GetTableClient(
            tablesSettings.DeviceEvents);

        _table.CreateIfNotExists();
    }

    public async Task<DeviceEventEntity?> SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return null;
        }

        var entity = new DeviceEventEntity
        {
            PartitionKey = deviceEvent.DeviceId,
            RowKey = $"{deviceEvent.OccurredAtUtc:yyyyMMddHHmmssfff}-{deviceEvent.EventId}",
            AgentId = deviceEvent.AgentId,
            TenantId = deviceEvent.TenantId,
            SiteId = deviceEvent.SiteId,
            DeviceId = deviceEvent.DeviceId,
            DeviceType = deviceEvent.DeviceType.ToString(),
            EventType = deviceEvent.EventType.ToString(),
            Severity = deviceEvent.Severity.ToString(),
            OccurredAtUtc = deviceEvent.OccurredAtUtc,
            Payload = JsonSerializer.Serialize(deviceEvent.Data)
        };

        await _table!.AddEntityAsync(
            entity,
            cancellationToken);

        return entity;

    }
}
