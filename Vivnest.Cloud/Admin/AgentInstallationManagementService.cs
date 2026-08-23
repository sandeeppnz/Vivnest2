using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Microsoft.Extensions.Logging;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Machines;
using Vivnest.Domain.Sites;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin;

// Orchestrates AgentInstallation lifecycle (Install/Move/Uninstall/Register/
// ReportDeployComplete/NoteAgentHeartbeat) per decision-log.md ADR-053's
// spec section 19-20, extended ADR-071/072 - this belongs here, not in
// AzureTableAgentInstallationStore, same "orchestration lives in the
// management service, not the Table repository" split every other admin
// feature uses. Validates Agent/Machine existence before creating a real
// operational relationship - same reasoning ADR-052 established for API
// keys. Install/Move/Uninstall stay purely declarative (never touch Docker
// directly); Register/ReportDeployComplete are the two points where this
// service's own state actually meets the real deploy pipeline, both via
// IAgentCommandPublisher's existing queue, never a direct call into
// Vivnest.Agent.Updater.
public sealed class AgentInstallationManagementService : IAgentInstallationManagementService
{
    private readonly IAgentInstallationStore _installations;
    private readonly IAgentRegistryStore _agents;
    private readonly IMachineStore _machines;
    private readonly IInstallTokenService _installTokens;
    private readonly IAgentRegistryManagementService _agentRegistryManagement;
    private readonly IAgentCommandPublisher _agentCommands;
    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly IAgentStatusResolver _agentStatusResolver;
    private readonly IApiKeyManagementService _apiKeys;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<AgentInstallationManagementService> _logger;

    public AgentInstallationManagementService(
        IAgentInstallationStore installations,
        IAgentRegistryStore agents,
        IMachineStore machines,
        IInstallTokenService installTokens,
        IAgentRegistryManagementService agentRegistryManagement,
        IAgentCommandPublisher agentCommands,
        IAgentHeartbeatReader agentHeartbeats,
        IAgentStatusResolver agentStatusResolver,
        IApiKeyManagementService apiKeys,
        IOptions<StorageOptions> storageOptions,
        ILogger<AgentInstallationManagementService> logger)
    {
        _installations = installations;
        _agents = agents;
        _machines = machines;
        _installTokens = installTokens;
        _agentRegistryManagement = agentRegistryManagement;
        _agentCommands = agentCommands;
        _agentHeartbeats = agentHeartbeats;
        _agentStatusResolver = agentStatusResolver;
        _apiKeys = apiKeys;
        _storageOptions = storageOptions.Value;
        _logger = logger;
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

    // Decision-log.md ADR-076 - Machine status is derived from its Agents'
    // own health, never a stored field: Unknown if nothing's installed
    // (nothing to derive from), Online only if every installed Agent is
    // Online, Offline only if every one is Offline, Warning for any real
    // mix in between - deliberately *not* "one offline Agent = Machine
    // offline," per the spec's own example (a Machine can host several
    // Agents; one going down shouldn't hide that the others are fine).
    public async Task<DeviceHeartbeatStatus> GetMachineOperationalStatusAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        var installations = await _installations.GetActiveByMachineAsync(
            tenant.TenantId, tenant.SiteId, machineId, cancellationToken);

        if (installations.Count == 0)
            return DeviceHeartbeatStatus.Unknown;

        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;
        var statuses = new List<DeviceHeartbeatStatus>();

        foreach (var installation in installations)
        {
            var agentEntity = await _agents.GetAsync(
                tenant.TenantId, tenant.SiteId, installation.AgentId, cancellationToken);

            if (agentEntity == null || string.IsNullOrWhiteSpace(agentEntity.RuntimeAgentId))
            {
                statuses.Add(DeviceHeartbeatStatus.Unknown);
                continue;
            }

            var heartbeat = await _agentHeartbeats.GetAsync(
                partitionKey, agentEntity.RuntimeAgentId, cancellationToken);

            statuses.Add(_agentStatusResolver.Determine(heartbeat).Status);
        }

        if (statuses.All(s => s == DeviceHeartbeatStatus.Healthy))
            return DeviceHeartbeatStatus.Healthy;

        if (statuses.All(s => s == DeviceHeartbeatStatus.Offline))
            return DeviceHeartbeatStatus.Offline;

        return DeviceHeartbeatStatus.Degraded;
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

    public async Task<AgentRegistrationResult?> RegisterAsync(
        string installToken,
        CancellationToken cancellationToken = default)
    {
        var token = await _installTokens.ValidateAndConsumeAsync(installToken, cancellationToken);

        if (token == null)
            return null;

        var installationEntity = await _installations.GetAsync(
            token.TenantId, token.SiteId, token.InstallationId, cancellationToken);

        // The token names an installation that no longer exists or has
        // already moved past Pending (e.g. a rare double-submit racing
        // itself) - the token is already consumed either way (best-effort,
        // never left replayable), nothing more to do.
        if (installationEntity == null || installationEntity.Status != AgentInstallationStatus.Pending.ToString())
            return null;

        var agentEntity = await _agents.GetAsync(
            token.TenantId, token.SiteId, installationEntity.AgentId, cancellationToken);

        if (agentEntity == null)
            return null;

        // Reuses an existing RuntimeAgentId rather than always minting a
        // new one - covers Move onto replacement hardware for an Agent
        // that's already been registered once before; only a genuinely
        // new Agent gets a fresh identity here.
        var runtimeAgentId = string.IsNullOrWhiteSpace(agentEntity.RuntimeAgentId)
            ? Guid.NewGuid().ToString()
            : agentEntity.RuntimeAgentId;

        if (runtimeAgentId != agentEntity.RuntimeAgentId)
        {
            await _agentRegistryManagement.SetRuntimeAgentIdAsync(
                token.TenantId, token.SiteId, installationEntity.AgentId, runtimeAgentId, cancellationToken);
        }

        var installation = ToDomain(installationEntity);
        installation.Register();

        var updatedInstallation = ToEntity(installation);
        updatedInstallation.ETag = installationEntity.ETag;

        await _installations.UpdateAsync(updatedInstallation, cancellationToken);

        await _agentCommands.PublishDeployCommandAsync(
            runtimeAgentId, installationEntity.ImageVersion, cancellationToken);

        // The Agent's own scoped credential for its command callbacks,
        // returned exactly once here. Best-effort: a mint failure must not
        // fail a registration that has already consumed its install token
        // and moved the installation to Installing - the Agent simply runs
        // without a key, which Cloud tolerates while
        // AgentAuth:RequireApiKey is false.
        string? agentApiKey = null;

        try
        {
            var minted = await _apiKeys.CreateForAgentAsync(
                token.TenantId, token.SiteId, runtimeAgentId, cancellationToken);

            agentApiKey = minted?.ApiKey;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to mint an agent API key for {RuntimeAgentId}; registering without one.", runtimeAgentId);
        }

        return new AgentRegistrationResult(
            runtimeAgentId,
            installationEntity.RowKey,
            token.TenantId,
            token.SiteId,
            installationEntity.ImageVersion,
            _storageOptions.ConnectionString,
            agentApiKey);
    }

    public async Task<bool> ReportDeployCompleteAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _installations.GetAsync(tenantId, siteId, installationId, cancellationToken);

        if (entity == null)
            return false;

        var installation = ToDomain(entity);
        installation.MarkInstalled();

        var updated = ToEntity(installation);
        updated.ETag = entity.ETag;

        await _installations.UpdateAsync(updated, cancellationToken);

        return true;
    }

    public async Task NoteAgentHeartbeatAsync(
        string tenantId,
        string siteId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        var agentEntity = await _agents.GetByRuntimeAgentIdAsync(tenantId, siteId, runtimeAgentId, cancellationToken);

        if (agentEntity == null)
            return;

        var installationEntity = await _installations.GetActiveByAgentAsync(
            tenantId, siteId, agentEntity.RowKey, cancellationToken);

        // Collapses Installing/Installed/Updating straight to Active on a
        // single real heartbeat, rather than requiring the deploy-complete
        // callback to have landed first - a heartbeat is unambiguous proof
        // the container is running regardless of which sub-state preceded
        // it, which makes the whole lifecycle self-healing against a
        // missed callback instead of fragile to one. Pending (never
        // registered) and Active/Decommissioned (nothing to do) are left
        // alone.
        if (installationEntity == null)
            return;

        var isMidProvisioning =
            installationEntity.Status == AgentInstallationStatus.Installing.ToString() ||
            installationEntity.Status == AgentInstallationStatus.Installed.ToString() ||
            installationEntity.Status == AgentInstallationStatus.Updating.ToString();

        if (!isMidProvisioning)
            return;

        var installation = ToDomain(installationEntity);
        installation.MarkActive();

        var updated = ToEntity(installation);
        updated.ETag = installationEntity.ETag;

        await _installations.UpdateAsync(updated, cancellationToken);
    }

    public async Task<AgentInstallationDto?> SetImageVersionAsync(
        TenantContext tenant,
        string agentId,
        string? imageVersion,
        CancellationToken cancellationToken = default)
    {
        var activeEntity = await _installations.GetActiveByAgentAsync(
            tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (activeEntity == null)
            return null;

        var installation = ToDomain(activeEntity);
        installation.RetargetImageVersion(imageVersion);

        var entity = ToEntity(installation);
        entity.ETag = activeEntity.ETag;

        await _installations.UpdateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<string?> GetActiveImageVersionByRuntimeAgentIdAsync(
        TenantContext tenant,
        string runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        var agentEntity = await _agents.GetByRuntimeAgentIdAsync(
            tenant.TenantId, tenant.SiteId, runtimeAgentId, cancellationToken);

        if (agentEntity == null)
            return null;

        var installationEntity = await _installations.GetActiveByAgentAsync(
            tenant.TenantId, tenant.SiteId, agentEntity.RowKey, cancellationToken);

        return installationEntity?.ImageVersion;
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
