using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceCapabilityStore
{
    Task<DeviceCapabilityEntity?> GetAsync(
        string tenantId,
        string siteId,
        string deviceCapabilityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceCapabilityEntity>> GetByDeviceAsync(
        string tenantId,
        string siteId,
        string deviceId,
        CancellationToken cancellationToken = default);

    // Reverse lookup for Agent Configuration Projection (decision-log.md
    // ADR-064) - "every capability this Agent executes, across every
    // Device" - mirrors GetByDeviceAsync exactly, just filtered by
    // ExecutingAgentId instead of DeviceId.
    Task<IReadOnlyList<DeviceCapabilityEntity>> GetByExecutingAgentAsync(
        string tenantId,
        string siteId,
        string executingAgentId,
        CancellationToken cancellationToken = default);

    // At most one active assignment should ever exist per (Device,
    // Capability) pair - enforced by CapabilityAssignmentService.AssignAsync,
    // not by any table-level constraint, same reasoning as
    // AgentInstallationEntity's "at most one active per Agent" invariant.
    Task<DeviceCapabilityEntity?> GetActiveByDeviceAndCapabilityAsync(
        string tenantId,
        string siteId,
        string deviceId,
        string capabilityId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        DeviceCapabilityEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        DeviceCapabilityEntity entity,
        CancellationToken cancellationToken = default);
}
