using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentInstallationStore
{
    Task<AgentInstallationEntity?> GetAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationEntity>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationEntity>> GetByMachineAsync(
        string tenantId,
        string siteId,
        string machineId,
        CancellationToken cancellationToken = default);

    // At most one active installation should ever exist per Agent - this
    // is enforced by AgentInstallationManagementService.InstallAsync, not
    // by any table-level constraint (Azure Table Storage has none).
    Task<AgentInstallationEntity?> GetActiveByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationEntity>> GetActiveByMachineAsync(
        string tenantId,
        string siteId,
        string machineId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        AgentInstallationEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AgentInstallationEntity entity,
        CancellationToken cancellationToken = default);
}
