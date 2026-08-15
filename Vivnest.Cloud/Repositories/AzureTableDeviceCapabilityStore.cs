using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Repositories;

// GetByDevice/GetActiveByDeviceAndCapability are partition-scoped scans
// (PartitionKey == TenantId|SiteId) filtered further by DeviceId/
// CapabilityId/Status in the same query expression - same shape
// AzureTableAgentInstallationStore already uses.
public class AzureTableDeviceCapabilityStore : IDeviceCapabilityStore
{
    private readonly AzureTableStore<DeviceCapabilityEntity> _store;

    public AzureTableDeviceCapabilityStore(
        IOptions<TablesOptions> tablesOptions,
        TableServiceClient tableServiceClient)
    {
        _store = new AzureTableStore<DeviceCapabilityEntity>(
            tableServiceClient,
            tablesOptions.Value.DeviceCapabilities);
    }

    public Task<DeviceCapabilityEntity?> GetAsync(
        string tenantId,
        string siteId,
        string deviceCapabilityId,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(new SiteScope(tenantId, siteId).PartitionKey, deviceCapabilityId, cancellationToken);
    }

    public Task<IReadOnlyList<DeviceCapabilityEntity>> GetByDeviceAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.DeviceId == deviceId,
            cancellationToken);
    }

    public Task<IReadOnlyList<DeviceCapabilityEntity>> GetByExecutingAgentAsync(
        string tenantId,
        string siteId,
        string executingAgentId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;

        return _store.QueryAsync(
            x => x.PartitionKey == partitionKey && x.ExecutingAgentId == executingAgentId,
            cancellationToken);
    }

    public async Task<DeviceCapabilityEntity?> GetActiveByDeviceAndCapabilityAsync(
        string tenantId,
        string siteId,
        string deviceId,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new SiteScope(tenantId, siteId).PartitionKey;
        var activeStatus = DeviceCapabilityStatus.Active.ToString();

        var results = await _store.QueryAsync(
            x => x.PartitionKey == partitionKey
                && x.DeviceId == deviceId
                && x.CapabilityId == capabilityId
                && x.Status == activeStatus,
            cancellationToken);

        return results.FirstOrDefault();
    }

    public Task CreateAsync(
        DeviceCapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpsertAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(
        DeviceCapabilityEntity entity,
        CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(entity, cancellationToken);
    }
}
