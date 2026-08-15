using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

// GetByAgent/GetByMachine/GetActiveBy* are partition-scoped scans
// (PartitionKey == TenantId|SiteId) filtered further by AgentId/MachineId/
// Status in the same query expression - same shape DeviceQueryService and
// AzureTableDeviceEventReader already use for tenant-wide queries; fine at
// current data volume (a handful of installations per agent/machine).
public class AzureTableAgentInstallationStore : IAgentInstallationStore
{
    private readonly AzureTableStore<AgentInstallationEntity> _store;

    public AzureTableAgentInstallationStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<AgentInstallationEntity>(
            tableServiceClient,
            tablesOptions.Value.AgentInstallations);
    }

    public Task<AgentInstallationEntity?> GetAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(new SiteScope(tenantId, siteId).PartitionKey, installationId, cancellationToken);
    }

    public Task<IReadOnlyList<AgentInstallationEntity>> GetByAgentAsync(
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

    public Task<IReadOnlyList<AgentInstallationEntity>> GetByMachineAsync(
        string tenantId,
        string siteId,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.MachineId == machineId,
            cancellationToken);
    }

    // Decision-log.md ADR-071 - "active" here means "the current
    // installation" (not decommissioned), not literally
    // AgentInstallationStatus.Active - a Pending/Installing/Installed/
    // Updating installation is still the one occupying the "at most one
    // active installation per Agent" slot InstallAsync enforces; only a
    // Decommissioned one has freed that slot up. Matches this store's own
    // name (GetActiveBy*) more loosely than it used to before Pass 1 added
    // the intermediate lifecycle states.
    public async Task<AgentInstallationEntity?> GetActiveByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;
        var decommissionedStatus = AgentInstallationStatus.Decommissioned.ToString();

        var results = await _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.AgentId == agentId && x.Status != decommissionedStatus,
            cancellationToken);

        return results.FirstOrDefault();
    }

    public Task<IReadOnlyList<AgentInstallationEntity>> GetActiveByMachineAsync(
        string tenantId,
        string siteId,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;
        var decommissionedStatus = AgentInstallationStatus.Decommissioned.ToString();

        return _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.MachineId == machineId && x.Status != decommissionedStatus,
            cancellationToken);
    }

    public Task CreateAsync(
        AgentInstallationEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        AgentInstallationEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
