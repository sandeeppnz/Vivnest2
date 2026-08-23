using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Sites;

namespace Vivnest.Cloud.Repositories;

// GetByAgent/GetActiveByAgentAndCapability are partition-scoped scans
// (PartitionKey == TenantId|SiteId) filtered further by AgentId/
// CapabilityId/Status in the same query expression - same shape
// AzureTableDeviceCapabilityStore already uses.
public class AzureTableAgentCapabilityStore : IAgentCapabilityStore
{
    private readonly AzureTableStore<AgentCapabilityEntity> _store;

    public AzureTableAgentCapabilityStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentCapabilityEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentCapabilities);
    }

    public Task<AgentCapabilityEntity?> GetAsync(
        string tenantId,
        string siteId,
        string agentCapabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(new SiteScope(tenantId, siteId).PartitionKey, agentCapabilityId, cancellationToken);
    }

    public Task<IReadOnlyList<AgentCapabilityEntity>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.AgentId == agentId,
            cancellationToken);
    }

    public async Task<AgentCapabilityEntity?> GetActiveByAgentAndCapabilityAsync(
        string tenantId,
        string siteId,
        string agentId,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;
        var activeStatus = AgentCapabilityStatus.Active.ToString();

        var results = await _store.QueryAsync(
            x => x.PartitionKey == partitionKey
                && x.AgentId == agentId
                && x.CapabilityId == capabilityId
                && x.Status == activeStatus,
            cancellationToken);

        return results.FirstOrDefault();
    }

    public Task CreateAsync(
        AgentCapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        AgentCapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
