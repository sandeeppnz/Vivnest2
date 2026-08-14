using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

// Orchestrates DeviceCapability lifecycle (Assign/Update/Unassign) per
// decision-log.md ADR-057/058 - belongs here, not in
// AzureTableDeviceCapabilityStore, same "orchestration lives in the
// management service, not the Table repository" split every other admin
// feature in this codebase uses. Validates Device and Capability exist,
// and (ADR-058) that ExecutingAgentId - if provided - resolves to a real
// Agent in this tenant/site, before creating/updating a real assignment -
// same reasoning AgentInstallationManagementService already established
// for Agent/Machine.
public sealed class CapabilityAssignmentService : ICapabilityAssignmentService
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private readonly IDeviceCapabilityStore _assignments;
    private readonly IDeviceRegistryStore _devices;
    private readonly ICapabilityStore _capabilities;
    private readonly IAgentRegistryStore _agentRegistry;

    public CapabilityAssignmentService(
        IDeviceCapabilityStore assignments,
        IDeviceRegistryStore devices,
        ICapabilityStore capabilities,
        IAgentRegistryStore agentRegistry)
    {
        _assignments = assignments;
        _devices = devices;
        _capabilities = capabilities;
        _agentRegistry = agentRegistry;
    }

    public async Task<IReadOnlyList<DeviceCapabilityDto>> ListByDeviceAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _assignments.GetByDeviceAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<DeviceCapabilityDto?> AssignAsync(
        TenantContext tenant,
        string deviceId,
        string capabilityId,
        string? executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        if (device == null)
            return null;

        var capability = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (capability == null)
            return null;

        if (!await IsValidExecutingAgentAsync(tenant, executingAgentId, cancellationToken))
            return null;

        var existingActive = await _assignments.GetActiveByDeviceAndCapabilityAsync(
            tenant.TenantId, tenant.SiteId, deviceId, capabilityId, cancellationToken);

        if (existingActive != null)
            return null;

        var assignment = new DeviceCapability(
            tenant.TenantId, tenant.SiteId, deviceId, capabilityId, executingAgentId ?? "", enabled, settings);

        var entity = ToEntity(assignment);

        await _assignments.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<DeviceCapabilityDto?> UpdateAssignmentAsync(
        TenantContext tenant,
        string deviceCapabilityId,
        string? executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var entity = await _assignments.GetAsync(tenant.TenantId, tenant.SiteId, deviceCapabilityId, cancellationToken);

        if (entity == null)
            return null;

        if (!await IsValidExecutingAgentAsync(tenant, executingAgentId, cancellationToken))
            return null;

        var assignment = ToDomain(entity);
        assignment.Update(executingAgentId ?? "", enabled, settings);

        var updated = ToEntity(assignment);
        updated.ETag = entity.ETag;

        await _assignments.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    public async Task<DeviceCapabilityDto?> UnassignAsync(
        TenantContext tenant,
        string deviceId,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _assignments.GetActiveByDeviceAndCapabilityAsync(
            tenant.TenantId, tenant.SiteId, deviceId, capabilityId, cancellationToken);

        if (entity == null)
            return null;

        var assignment = ToDomain(entity);
        assignment.Remove();

        var updated = ToEntity(assignment);
        updated.ETag = entity.ETag;

        await _assignments.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    // Empty ExecutingAgentId means "not assigned yet" - always valid. A
    // non-empty one must resolve to a real Agent in this exact Tenant/Site
    // (ADR-058) - same authorization boundary DeviceService.IsValidOwningAgentAsync
    // enforces for Device.OwningAgentId.
    private async Task<bool> IsValidExecutingAgentAsync(
        TenantContext tenant,
        string? executingAgentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executingAgentId))
            return true;

        var agent = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, executingAgentId, cancellationToken);

        return agent != null;
    }

    private static DeviceCapability ToDomain(DeviceCapabilityEntity entity)
    {
        return DeviceCapability.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.DeviceId,
            entity.CapabilityId,
            entity.ExecutingAgentId,
            entity.Enabled,
            ParseSettings(entity.Settings),
            Enum.Parse<DeviceCapabilityStatus>(entity.Status),
            entity.AssignedUtc,
            entity.RemovedUtc,
            entity.UpdatedUtc);
    }

    private static DeviceCapabilityEntity ToEntity(DeviceCapability assignment)
    {
        return new DeviceCapabilityEntity
        {
            PartitionKey = new SiteScope(assignment.TenantId, assignment.SiteId).PartitionKey,
            RowKey = assignment.DeviceCapabilityId,
            TenantId = assignment.TenantId,
            SiteId = assignment.SiteId,
            DeviceId = assignment.DeviceId,
            CapabilityId = assignment.CapabilityId,
            ExecutingAgentId = assignment.ExecutingAgentId,
            Enabled = assignment.Enabled,
            Settings = SerializeSettings(assignment.Settings),
            Status = assignment.Status.ToString(),
            AssignedUtc = assignment.AssignedUtc,
            RemovedUtc = assignment.RemovedUtc,
            UpdatedUtc = assignment.UpdatedUtc
        };
    }

    private static DeviceCapabilityDto ToDto(DeviceCapabilityEntity entity)
    {
        return new DeviceCapabilityDto(
            entity.RowKey,
            entity.DeviceId,
            entity.CapabilityId,
            entity.ExecutingAgentId,
            entity.Enabled,
            ParseSettings(entity.Settings),
            entity.Status,
            entity.AssignedUtc,
            entity.RemovedUtc,
            entity.UpdatedUtc,
            entity.TenantId,
            entity.SiteId);
    }

    private static string SerializeSettings(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null || settings.Count == 0)
            return "{}";

        return System.Text.Json.JsonSerializer.Serialize(settings);
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return EmptySettings;

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
