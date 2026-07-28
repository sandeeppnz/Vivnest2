using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vivnest.Core.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces.Heartbeats;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Infrastructure.Repositories;

public sealed class AgentHeartbeatRepository : IAgentHeartbeatRepository
{
    private readonly TableClient _table;

    public AgentHeartbeatRepository(IOptions<AgentHeartbeatOptions> options)
    {
        var settings = options.Value;

        _table = new TableClient(
            settings.ConnectionString,
            settings.TableName);

        _table.CreateIfNotExists();
    }

    public async Task UpsertAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var entity = new AgentHeartbeatEntity
        {
            PartitionKey = heartbeat.AgentId,
            RowKey = heartbeat.AgentId,
            StartedUtc = heartbeat.StartedUtc,
            LastHeartbeatUtc = heartbeat.LastHeartbeatUtc,
            FirmwareVersion = heartbeat.FirmwareVersion,
            HostName = heartbeat.HostName,
            Error = heartbeat.Error,
            HeartbeatInterval = heartbeat.HeartbeatInterval

        };

        await _table.UpsertEntityAsync(
            entity,
            TableUpdateMode.Replace,
            cancellationToken);
    }

    public async Task<AgentHeartbeat?> GetAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<AgentHeartbeatEntity>(
                partitionKey: agentId,
                rowKey: agentId,
                cancellationToken: cancellationToken);

            var entity = response.Value;

            return new AgentHeartbeat
            {
                AgentId = entity.PartitionKey,
                StartedUtc = entity.StartedUtc,
                LastHeartbeatUtc = entity.LastHeartbeatUtc,
                FirmwareVersion = entity.FirmwareVersion,
                HostName = entity.HostName,
                Error = entity.Error,
                HeartbeatInterval = entity.HeartbeatInterval
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}