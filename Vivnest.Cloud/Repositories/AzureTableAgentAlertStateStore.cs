using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Cloud.Repositories;

// Sprint 8. Unlike its sibling stores, this one builds its AzureTableStore
// lazily rather than in the constructor.
//
// AzureTableStore's constructor calls CreateIfNotExists(), and
// TablesOptions defaults every name to "" with no fallback. Operational
// alerting ships disabled, so a deployment that has not opted in has no
// reason to have set Tables__AgentAlertState - and eager construction would
// then throw the first time an agent-event message arrived, for a feature
// nobody turned on. That is a real path, not a hypothetical: the Agent
// publishes to agent-events based on its OWN config, independently of
// whether Cloud has alerting enabled.
//
// Deferring means disabled-and-unconfigured is silent, as it should be,
// while enabled-and-unconfigured fails loudly with a message naming the
// setting to fix.
public sealed class AzureTableAgentAlertStateStore : IAgentAlertStateStore
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly string _tableName;
    private readonly object _gate = new();

    private AzureTableStore<AgentAlertStateEntity>? _store;

    public AzureTableAgentAlertStateStore(
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions)
    {
        _tableServiceClient = tableServiceClient;
        _tableName = tablesOptions.Value.AgentAlertState;
    }

    private AzureTableStore<AgentAlertStateEntity> Store
    {
        get
        {
            if (_store is not null)
                return _store;

            lock (_gate)
            {
                if (_store is not null)
                    return _store;

                if (string.IsNullOrWhiteSpace(_tableName))
                {
                    throw new InvalidOperationException(
                        "Tables:AgentAlertState is not configured, but operational alerting is enabled "
                        + "(OperationalAlert:Enabled). Set the table name, or turn alerting off.");
                }

                return _store = new AzureTableStore<AgentAlertStateEntity>(
                    _tableServiceClient, _tableName);
            }
        }
    }

    public Task<AgentAlertStateEntity?> GetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        Store.GetAsync(partitionKey, rowKey, cancellationToken);

    public Task UpsertAsync(
        AgentAlertStateEntity entity, CancellationToken cancellationToken = default) =>
        Store.UpsertAsync(entity, cancellationToken);
}
