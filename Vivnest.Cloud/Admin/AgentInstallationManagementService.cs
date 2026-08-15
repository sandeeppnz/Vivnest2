using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

// Orchestrates AgentInstallation lifecycle (Install/Move/Uninstall) per
// decision-log.md ADR-053's spec section 19-20 - this belongs here, not in
// AzureTableAgentInstallationStore, same "orchestration lives in the
// management service, not the Table repository" split every other admin
// feature uses. Validates Agent/Machine existence before creating a real
// operational relationship - same reasoning ADR-052 established for API
// keys. Purely declarative: does not call into Vivnest.Agent.Updater or
// touch the real docker deploy pipeline in any way.
public sealed class AgentInstallationManagementService : IAgentInstallationManagementService
{
    private readonly IAgentInstallationStore _installations;
    private readonly IAgentRegistryStore _agents;
    private readonly IMachineStore _machines;
    private readonly IInstallTokenService _installTokens;

    public AgentInstallationManagementService(
        IAgentInstallationStore installations,
        IAgentRegistryStore agents,
        IMachineStore machines,
        IInstallTokenService installTokens)
    {
        _installations = installations;
        _agents = agents;
        _machines = machines;
        _installTokens = installTokens;
    }

    public async Task<IReadOnlyList<AgentInstallationDto>> GetByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _installations.GetByAgentAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<AgentInstallationDto>> GetByMachineAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _installations.GetByMachineAsync(tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<AgentInstallationDto?> GetActiveByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _installations.GetActiveByAgentAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<AgentInstallationDto>> GetActiveByMachineAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _installations.GetActiveByMachineAsync(tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<AgentInstallationCreationResult?> InstallAsync(
        TenantContext tenant,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        CancellationToken cancellationToken = default)
    {
        var agent = await _agents.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (agent == null)
            return null;

        var machine = await _machines.GetAsync(tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        if (machine == null)
            return null;

        var existingActive = await _installations.GetActiveByAgentAsync(
            tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (existingActive != null)
            return null;

        var installation = new AgentInstallation(
            tenant.TenantId, tenant.SiteId, agentId, machineId, containerId, imageName, imageVersion);

        var entity = ToEntity(installation);

        await _installations.CreateAsync(entity, cancellationToken);

        var token = await _installTokens.CreateAsync(
            tenant.TenantId, tenant.SiteId, installation.InstallationId, cancellationToken);

        return new AgentInstallationCreationResult(ToDto(entity), token.InstallToken, token.ExpiresUtc);
    }

    public async Task<AgentInstallationCreationResult?> MoveAsync(
        TenantContext tenant,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        CancellationToken cancellationToken = default)
    {
        var agent = await _agents.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (agent == null)
            return null;

        var machine = await _machines.GetAsync(tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        if (machine == null)
            return null;

        var existingActiveEntity = await _installations.GetActiveByAgentAsync(
            tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (existingActiveEntity != null)
        {
            var existingActive = ToDomain(existingActiveEntity);
            existingActive.Decommission();

            var retiredEntity = ToEntity(existingActive);
            retiredEntity.ETag = existingActiveEntity.ETag;

            await _installations.UpdateAsync(retiredEntity, cancellationToken);
        }

        var newInstallation = new AgentInstallation(
            tenant.TenantId, tenant.SiteId, agentId, machineId, containerId, imageName, imageVersion);

        var newEntity = ToEntity(newInstallation);

        await _installations.CreateAsync(newEntity, cancellationToken);

        var token = await _installTokens.CreateAsync(
            tenant.TenantId, tenant.SiteId, newInstallation.InstallationId, cancellationToken);

        return new AgentInstallationCreationResult(ToDto(newEntity), token.InstallToken, token.ExpiresUtc);
    }

    public async Task<AgentInstallationDto?> UninstallAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _installations.GetActiveByAgentAsync(
            tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (entity == null)
            return null;

        var installation = ToDomain(entity);
        installation.Decommission();

        var updated = ToEntity(installation);
        updated.ETag = entity.ETag;

        await _installations.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    private static AgentInstallation ToDomain(AgentInstallationEntity entity)
    {
        return AgentInstallation.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.AgentId,
            entity.MachineId,
            entity.ContainerId,
            entity.ImageName,
            entity.ImageVersion,
            Enum.Parse<AgentInstallationStatus>(entity.Status),
            entity.InstalledUtc,
            entity.RemovedUtc,
            entity.UpdatedUtc);
    }

    private static AgentInstallationEntity ToEntity(AgentInstallation installation)
    {
        return new AgentInstallationEntity
        {
            PartitionKey = new SiteScope(installation.TenantId, installation.SiteId).PartitionKey,
            RowKey = installation.InstallationId,
            TenantId = installation.TenantId,
            SiteId = installation.SiteId,
            AgentId = installation.AgentId,
            MachineId = installation.MachineId,
            ContainerId = installation.ContainerId,
            ImageName = installation.ImageName,
            ImageVersion = installation.ImageVersion,
            Status = installation.Status.ToString(),
            InstalledUtc = installation.InstalledUtc,
            RemovedUtc = installation.RemovedUtc,
            UpdatedUtc = installation.UpdatedUtc
        };
    }

    private static AgentInstallationDto ToDto(AgentInstallationEntity entity)
    {
        return new AgentInstallationDto(
            entity.RowKey,
            entity.AgentId,
            entity.MachineId,
            entity.ContainerId,
            entity.ImageName,
            entity.ImageVersion,
            entity.Status,
            entity.InstalledUtc,
            entity.RemovedUtc,
            entity.UpdatedUtc,
            entity.TenantId,
            entity.SiteId);
    }
}
