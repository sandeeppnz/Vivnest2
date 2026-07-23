using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Core.Heartbeat;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Heartbeat;

public class AzureTableHeartbeatService : IHeartbeatService
{
    private readonly TableClient? _table;
    private readonly bool _enabled;

    public AzureTableHeartbeatService(IOptions<HeartbeatOptions> options)
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

    public async Task SendAsync(
        Vivnest.Core.Heartbeat.Heartbeat heartbeat,
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
            LastCaptureUtc = heartbeat.LastCaptureUtc
        };

        await _table!.UpsertEntityAsync(
            entity,
            cancellationToken: cancellationToken);
    }
}