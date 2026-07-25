using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.Heartbeat;

namespace Vivnest.Infrastructure.Stores;

public class AzureTableHeartbeatRepository : IHeartbeatRepository
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    public AzureTableHeartbeatRepository(IOptions<HeartbeatOptions> options)
    {
        var heartbeatOptions = options.Value;

        _enabled = heartbeatOptions.Enabled;

        if (!_enabled)
        {
            return;
        }

        var service = new TableServiceClient(
            heartbeatOptions.ConnectionString);

        _table = service.GetTableClient(
            heartbeatOptions.TableName);

        _table.CreateIfNotExists();
    }

    public async Task SaveAsync(
        Core.Models.Heartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        var entity = new HeartbeatEntity
        {
            PartitionKey = "Agent",
            RowKey = heartbeat.AgentId,
            Status = heartbeat.Status.ToString(),
            Version = heartbeat.Version,
            BlobName = heartbeat.BlobName,
            Error = heartbeat.Error,
            LastSeenUtc = heartbeat.LastSeenUtc,
            LastCaptureUtc = heartbeat.LastCaptureUtc
        };

        await _table!.UpsertEntityAsync(
            entity,
            cancellationToken: cancellationToken);
    }
}