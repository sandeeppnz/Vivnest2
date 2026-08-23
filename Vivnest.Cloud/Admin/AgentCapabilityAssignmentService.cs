using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using System.Text.Json;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Sites;

namespace Vivnest.Cloud.Admin;

// Orchestrates AgentCapability lifecycle (Assign/Unassign) per
// decision-log.md ADR-059 - belongs here, not in
// AzureTableAgentCapabilityStore, same "orchestration lives in the
// management service, not the Table repository" split every other admin
// feature in this codebase uses. Validates Agent and Capability exist
// before creating a real declaration - same reasoning
// CapabilityAssignmentService already established for Device/Capability.
public sealed class AgentCapabilityAssignmentService : IAgentCapabilityAssignmentService
{
    private readonly IAgentCapabilityStore _declarations;
    private readonly IAgentRegistryStore _agents;
    private readonly ICapabilityStore _capabilities;

    public AgentCapabilityAssignmentService(
        IAgentCapabilityStore declarations,
        IAgentRegistryStore agents,
        ICapabilityStore capabilities)
    {
        _declarations = declarations;
        _agents = agents;
        _capabilities = capabilities;
    }

    public async Task<IReadOnlyList<AgentCapabilityDto>> ListByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _declarations.GetByAgentAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<AgentCapabilityDto?> AssignAsync(
        TenantContext tenant,
        string agentId,
        string capabilityId,
        IReadOnlyDictionary<string, string>? settings = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await _agents.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (agent == null)
            return null;

        var capability = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (capability == null)
            return null;

        var existingActive = await _declarations.GetActiveByAgentAndCapabilityAsync(
            tenant.TenantId, tenant.SiteId, agentId, capabilityId, cancellationToken);

        if (existingActive != null)
            return null;

        var declaration = new AgentCapability(
            tenant.TenantId, tenant.SiteId, agentId, capabilityId, settings);

        var entity = ToEntity(declaration);

        await _declarations.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<AgentCapabilityDto?> UnassignAsync(
        TenantContext tenant,
        string agentId,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _declarations.GetActiveByAgentAndCapabilityAsync(
            tenant.TenantId, tenant.SiteId, agentId, capabilityId, cancellationToken);

        if (entity == null)
            return null;

        var declaration = ToDomain(entity);
        declaration.Remove();

        var updated = ToEntity(declaration);
        updated.ETag = entity.ETag;

        await _declarations.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    private static AgentCapability ToDomain(AgentCapabilityEntity entity)
    {
        return AgentCapability.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.AgentId,
            entity.CapabilityId,
            Enum.Parse<AgentCapabilityStatus>(entity.Status),
            entity.AssignedUtc,
            entity.RemovedUtc,
            entity.UpdatedUtc,
            ParseSettings(entity.Settings));
    }

    private static AgentCapabilityEntity ToEntity(AgentCapability declaration)
    {
        return new AgentCapabilityEntity
        {
            PartitionKey = new SiteScope(declaration.TenantId, declaration.SiteId).PartitionKey,
            RowKey = declaration.AgentCapabilityId,
            TenantId = declaration.TenantId,
            SiteId = declaration.SiteId,
            AgentId = declaration.AgentId,
            CapabilityId = declaration.CapabilityId,
            Status = declaration.Status.ToString(),
            AssignedUtc = declaration.AssignedUtc,
            RemovedUtc = declaration.RemovedUtc,
            UpdatedUtc = declaration.UpdatedUtc,
            Settings = JsonSerializer.Serialize(declaration.Settings)
        };
    }

    // Malformed Settings must not break a plain listing - the projector
    // is where bad JSON becomes a published-configuration warning
    // (ADR-097); here it degrades to "no settings" so the admin UI can
    // still show the assignment that needs fixing.
    private static IReadOnlyDictionary<string, string> ParseSettings(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static AgentCapabilityDto ToDto(AgentCapabilityEntity entity)
    {
        return new AgentCapabilityDto(
            entity.RowKey,
            entity.AgentId,
            entity.CapabilityId,
            entity.Status,
            entity.AssignedUtc,
            entity.RemovedUtc,
            entity.UpdatedUtc,
            entity.TenantId,
            entity.SiteId,
            ParseSettings(entity.Settings));
    }
}
