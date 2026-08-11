using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Cloud.Admin;

// A genuine hard delete, same reasoning as CapabilityManagementService -
// this is a declared identity record meant to actually shrink, not an
// audit trail. Type string validation (Enum.TryParse<AgentType>) happens
// in the Function layer before calling here.
public sealed class AgentRegistryManagementService : IAgentRegistryManagementService
{
    private readonly IAgentRegistryStore _agentRegistry;

    public AgentRegistryManagementService(IAgentRegistryStore agentRegistry)
    {
        _agentRegistry = agentRegistry;
    }

    public async Task<IReadOnlyList<AgentRegistryDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var entities = await _agentRegistry.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<AgentRegistryDto> CreateAsync(
        TenantContext tenant,
        string name,
        string firmwareVersion,
        string type,
        IReadOnlyList<Guid>? capabilityIds,
        CancellationToken cancellationToken = default)
    {
        var entity = new AgentRegistryEntity
        {
            PartitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey,
            RowKey = Guid.NewGuid().ToString(),
            TenantId = tenant.TenantId,
            SiteId = tenant.SiteId,
            Name = name,
            FirmwareVersion = firmwareVersion,
            Type = type,
            CapabilityIds = SerializeCapabilityIds(capabilityIds)
        };

        await _agentRegistry.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<AgentRegistryDto?> UpdateAsync(
        TenantContext tenant,
        string agentId,
        string name,
        string firmwareVersion,
        string type,
        IReadOnlyList<Guid>? capabilityIds,
        CancellationToken cancellationToken = default)
    {
        var entity = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (entity == null)
            return null;

        entity.Name = name;
        entity.FirmwareVersion = firmwareVersion;
        entity.Type = type;
        entity.CapabilityIds = SerializeCapabilityIds(capabilityIds);

        await _agentRegistry.UpdateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (entity == null)
            return false;

        await _agentRegistry.DeleteAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        return true;
    }

    private static AgentRegistryDto ToDto(AgentRegistryEntity entity)
    {
        return new AgentRegistryDto(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.FirmwareVersion,
            entity.Type,
            entity.TenantId,
            entity.SiteId,
            ParseCapabilityIds(entity.CapabilityIds));
    }

    private static string SerializeCapabilityIds(IReadOnlyList<Guid>? capabilityIds)
    {
        if (capabilityIds == null || capabilityIds.Count == 0)
            return "";

        return string.Join(',', capabilityIds);
    }

    private static IReadOnlyList<Guid> ParseCapabilityIds(string capabilityIds)
    {
        if (string.IsNullOrWhiteSpace(capabilityIds))
            return [];

        return capabilityIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }
}
