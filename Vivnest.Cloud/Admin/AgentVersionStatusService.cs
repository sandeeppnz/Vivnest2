using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Sites;

namespace Vivnest.Cloud.Admin;

public sealed class AgentVersionStatusService : IAgentVersionStatusService
{
    private readonly IAgentRegistryStore _agents;
    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly IAgentInstallationManagementService _installations;

    public AgentVersionStatusService(
        IAgentRegistryStore agents,
        IAgentHeartbeatReader agentHeartbeats,
        IAgentInstallationManagementService installations)
    {
        _agents = agents;
        _agentHeartbeats = agentHeartbeats;
        _installations = installations;
    }

    public async Task<AgentVersionStatusDto> GetStatusAsync(
        TenantContext tenant,
        string agentId,
        string? desiredVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(desiredVersion))
            return new AgentVersionStatusDto(null, null, AgentVersionStatus.NeverDeployed);

        var agent = await _agents.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (agent == null || string.IsNullOrWhiteSpace(agent.RuntimeAgentId))
            return new AgentVersionStatusDto(desiredVersion, null, AgentVersionStatus.Unknown);

        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;
        var heartbeat = await _agentHeartbeats.GetAsync(partitionKey, agent.RuntimeAgentId, cancellationToken);

        return BuildStatus(desiredVersion, heartbeat?.FirmwareVersion);
    }

    public async Task<AgentVersionStatusDto> GetStatusForRuntimeAgentAsync(
        TenantContext tenant,
        string runtimeAgentId,
        string? runningVersion,
        CancellationToken cancellationToken = default)
    {
        var desiredVersion = await _installations.GetActiveImageVersionByRuntimeAgentIdAsync(
            tenant, runtimeAgentId, cancellationToken);

        if (string.IsNullOrWhiteSpace(desiredVersion))
            return new AgentVersionStatusDto(null, null, AgentVersionStatus.NeverDeployed);

        return BuildStatus(desiredVersion, runningVersion);
    }

    private static AgentVersionStatusDto BuildStatus(string desiredVersion, string? runningVersion)
    {
        if (string.IsNullOrWhiteSpace(runningVersion))
            return new AgentVersionStatusDto(desiredVersion, null, AgentVersionStatus.Unknown);

        // Exact string match, deliberately not semver-aware - a git-SHA
        // build (pre-ADR-073) will never match a real semver
        // AgentInstallation.ImageVersion, which is the correct answer
        // (they genuinely are different things), not a bug to special-case.
        var status = string.Equals(desiredVersion, runningVersion, StringComparison.Ordinal)
            ? AgentVersionStatus.UpToDate
            : AgentVersionStatus.Outdated;

        return new AgentVersionStatusDto(desiredVersion, runningVersion, status);
    }
}
