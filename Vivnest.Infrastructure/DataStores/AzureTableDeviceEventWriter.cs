using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Domain.Devices;

namespace Vivnest.Infrastructure.DataStores;

public sealed class AzureTableDeviceEventWriter : IDeviceEventWriter
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    // Takes the DI-registered TableServiceClient, which AddInfrastructure
    // creates once from Storage:ConnectionString - this class used to new
    // up a second client from the same connection string, making it one of
    // two places that read the setting and the only writer that bypassed
    // the shared client the heartbeat writers already inject.
    public AzureTableDeviceEventWriter(
        IOptions<DeviceEventOptions> deviceEventOptions, IOptions<TablesOptions> tablesOptions, TableServiceClient tableServiceClient)
    {
        _enabled = deviceEventOptions.Value.Enabled;

        if (!_enabled)
        {
            return;
        }

        _table = tableServiceClient.GetTableClient(
            tablesOptions.Value.DeviceEvents);

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
            RowKey = EventRowKey.For(deviceEvent.OccurredAtUtc, deviceEvent.EventId),
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
