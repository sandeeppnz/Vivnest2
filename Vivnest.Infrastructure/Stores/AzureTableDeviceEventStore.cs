using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.Heartbeat;

namespace Vivnest.Infrastructure.Stores;

public sealed class AzureTableDeviceEventStore : IDeviceEventStore
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    public AzureTableDeviceEventStore(
        IOptions<DeviceEventOptions> options)
    {
        var deviceEventSettings = options.Value;

        _enabled = options.Value.Enabled;

        if (!_enabled)
        {
            return;
        }

        var service = new TableServiceClient(
            deviceEventSettings.ConnectionString);

        _table = service.GetTableClient(
            deviceEventSettings.TableName);

        _table.CreateIfNotExists();
    }

    public async Task SaveAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        var entity = new DeviceEventEntity
        {
            PartitionKey = deviceEvent.DeviceId,
            RowKey = $"{deviceEvent.Timestamp:yyyyMMddHHmmssfff}-{deviceEvent.Id}",
            AgentId = deviceEvent.AgentId,
            AgentVersion = deviceEvent.AgentVersion,
            DeviceId = deviceEvent.DeviceId,
            DeviceType = deviceEvent.DeviceType.ToString(),
            EventType = deviceEvent.EventType.ToString(),
            Severity = deviceEvent.Severity.ToString(),
            EventTimestampUtc = deviceEvent.Timestamp,
            Payload = JsonSerializer.Serialize(deviceEvent.Data)
        };

        await _table!.AddEntityAsync(
            entity,
            cancellationToken);
    }
}
