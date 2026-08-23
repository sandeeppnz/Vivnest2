using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

public sealed class AzureTableAgentEventStore : IAgentEventStore
{
    private readonly AzureTableStore<AgentEventEntity> _store;

    public AzureTableAgentEventStore(
        TableServiceClient tableServiceClient, IOptions<TablesOptions> tablesOptions)
    {
        _store = new AzureTableStore<AgentEventEntity>(tableServiceClient, tablesOptions.Value.AgentEvents);
    }

    public Task UpsertAsync(AgentEventEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);
}

public sealed class AzureTableDeviceEventStore : IDeviceEventStore
{
    private readonly AzureTableStore<DeviceEventEntity> _store;

    public AzureTableDeviceEventStore(
        TableServiceClient tableServiceClient, IOptions<TablesOptions> tablesOptions)
    {
        _store = new AzureTableStore<DeviceEventEntity>(tableServiceClient, tablesOptions.Value.DeviceEvents);
    }

    public Task UpsertAsync(DeviceEventEntity entity, CancellationToken cancellationToken = default) =>
        _store.UpsertAsync(entity, cancellationToken);
}
