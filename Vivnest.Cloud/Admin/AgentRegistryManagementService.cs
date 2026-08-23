using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Sites;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Agent domain model (Vivnest.Core.Domain)
// to/from AgentRegistryEntity for storage (decision-log.md ADR-057,
// extending the same "domain class separate from the Table entity"
// pattern to Agent) - same shape MachineManagementService/DeviceService
// already established. A genuine hard delete, same reasoning as
// CapabilityManagementService - this is a declared identity record meant
// to actually shrink, not an audit trail. Type string validation
// (Enum.TryParse<AgentType>) happens in the Function layer before calling
// here.
//
// No longer touches CapabilityIds (ADR-059) - capability declaration is
// AgentCapabilityAssignmentService's job now.
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
        string? description,
        string firmwareVersion,
        string type,
        string? runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        var agent = new Agent(
            tenant.TenantId,
            tenant.SiteId,
            name,
            description,
            firmwareVersion,
            Enum.Parse<AgentType>(type));
        agent.Update(name, description, firmwareVersion, Enum.Parse<AgentType>(type), runtimeAgentId ?? "");

        var entity = ToEntity(agent);

        await _agentRegistry.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<AgentRegistryDto?> UpdateAsync(
        TenantContext tenant,
        string agentId,
        string name,
        string? description,
        string status,
        string firmwareVersion,
        string type,
        string? runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (entity == null)
            return null;

        var agent = ToDomain(entity);
        agent.Update(name, description, firmwareVersion, Enum.Parse<AgentType>(type), runtimeAgentId ?? "");
        agent.SetStatus(Enum.Parse<AgentStatus>(status));

        var updated = ToEntity(agent);
        updated.ETag = entity.ETag;

        await _agentRegistry.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
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

    public async Task<AgentRegistryDto?> SetRuntimeAgentIdAsync(
        string tenantId,
        string siteId,
        string agentId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _agentRegistry.GetAsync(tenantId, siteId, agentId, cancellationToken);

        if (entity == null)
            return null;

        var agent = ToDomain(entity);
        agent.Update(agent.Name, agent.Description, agent.FirmwareVersion, agent.Type, runtimeAgentId);

        var updated = ToEntity(agent);
        updated.ETag = entity.ETag;

        await _agentRegistry.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    // Blank Status means this row predates ADR-053 - treat as Active
    // rather than requiring a backfill, same tolerance AgentRegistryEntity's
    // own comment documents.
    private static Agent ToDomain(AgentRegistryEntity entity)
    {
        var status = string.IsNullOrWhiteSpace(entity.Status)
            ? AgentStatus.Active
            : Enum.Parse<AgentStatus>(entity.Status);

        return Agent.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.Name,
            entity.Description,
            status,
            entity.FirmwareVersion,
            Enum.Parse<AgentType>(entity.Type),
            entity.RuntimeAgentId ?? "",
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }

    private static AgentRegistryEntity ToEntity(Agent agent)
    {
        // Rows that predate ADR-053 never had CreatedUtc stored, so it
        // deserializes as C#'s default(DateTime) - Kind Unspecified, which
        // the Azure Table SDK rejects on write ("requires it to be UTC").
        // No real creation timestamp exists for these rows; backfill with
        // UpdatedUtc rather than crash. A real CreatedUtc from a row this
        // field was actually set on already round-trips as Kind Utc.
        var createdUtc = agent.CreatedUtc == default
            ? agent.UpdatedUtc
            : DateTime.SpecifyKind(agent.CreatedUtc, DateTimeKind.Utc);

        return new AgentRegistryEntity
        {
            PartitionKey = new SiteScope(agent.TenantId, agent.SiteId).PartitionKey,
            RowKey = agent.AgentId,
            TenantId = agent.TenantId,
            SiteId = agent.SiteId,
            Name = agent.Name,
            Description = agent.Description,
            Status = agent.Status.ToString(),
            FirmwareVersion = agent.FirmwareVersion,
            Type = agent.Type.ToString(),
            RuntimeAgentId = agent.RuntimeAgentId,
            CreatedUtc = createdUtc,
            UpdatedUtc = agent.UpdatedUtc
        };
    }

    private static AgentRegistryDto ToDto(AgentRegistryEntity entity)
    {
        return new AgentRegistryDto(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.Description,
            string.IsNullOrWhiteSpace(entity.Status) ? AgentStatus.Active.ToString() : entity.Status,
            entity.FirmwareVersion,
            entity.Type,
            entity.RuntimeAgentId ?? "",
            entity.TenantId,
            entity.SiteId,
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }
}
