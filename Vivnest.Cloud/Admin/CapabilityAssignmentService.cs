using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

// Orchestrates DeviceCapability lifecycle (Assign/Update/Unassign) per
// decision-log.md ADR-057/058/059/062 - belongs here, not in
// AzureTableDeviceCapabilityStore, same "orchestration lives in the
// management service, not the Table repository" split every other admin
// feature in this codebase uses. AssignAsync runs the full "complete
// assignment algorithm" (ADR-062 spec §21/44): DeviceType compatibility,
// ExecutingAgent validity/declaration (ADR-058/059), direct dependency
// satisfaction, and configuration-schema validation, all before a
// DeviceCapability is ever created. UpdateAssignmentAsync only
// re-validates what it can actually change (ExecutingAgent, Settings) -
// compatibility/dependencies were already true when the assignment was
// first created and don't change from an Update.
public sealed class CapabilityAssignmentService : ICapabilityAssignmentService
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private static readonly IReadOnlyList<CapabilityConfigurationField> EmptySchema =
        Array.Empty<CapabilityConfigurationField>();

    private readonly IDeviceCapabilityStore _assignments;
    private readonly IDeviceRegistryStore _devices;
    private readonly ICapabilityStore _capabilities;
    private readonly IAgentRegistryStore _agentRegistry;
    private readonly IAgentCapabilityStore _agentCapabilities;
    private readonly IDeviceTypeCapabilityStore _compatibility;
    private readonly ICapabilityDependencyStore _dependencies;
    private readonly ICapabilityConfigurationService _configuration;

    public CapabilityAssignmentService(
        IDeviceCapabilityStore assignments,
        IDeviceRegistryStore devices,
        ICapabilityStore capabilities,
        IAgentRegistryStore agentRegistry,
        IAgentCapabilityStore agentCapabilities,
        IDeviceTypeCapabilityStore compatibility,
        ICapabilityDependencyStore dependencies,
        ICapabilityConfigurationService configuration)
    {
        _assignments = assignments;
        _devices = devices;
        _capabilities = capabilities;
        _agentRegistry = agentRegistry;
        _agentCapabilities = agentCapabilities;
        _compatibility = compatibility;
        _dependencies = dependencies;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<DeviceCapabilityDto>> ListByDeviceAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _assignments.GetByDeviceAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<CapabilityAssignmentResult> AssignAsync(
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
        {
            return Error(CapabilityAssignmentErrorCode.DeviceNotFound, $"DeviceId \"{deviceId}\" doesn't exist.");
        }

        var capabilityEntity = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (capabilityEntity == null)
        {
            return Error(CapabilityAssignmentErrorCode.CapabilityNotFound, $"CapabilityId \"{capabilityId}\" doesn't exist.");
        }

        if (string.IsNullOrWhiteSpace(device.DeviceTypeId))
        {
            return Error(
                CapabilityAssignmentErrorCode.IncompatibleDeviceType,
                "This device has no DeviceType set - capability compatibility cannot be verified.");
        }

        var compatible = await _compatibility.ListByDeviceTypeAsync(device.DeviceTypeId, cancellationToken);

        if (!compatible.Any(c => c.CapabilityId == capabilityId))
        {
            return Error(
                CapabilityAssignmentErrorCode.IncompatibleDeviceType,
                "Capability is not compatible with this DeviceType.");
        }

        if (!await IsValidExecutingAgentAsync(tenant, executingAgentId, capabilityId, cancellationToken))
        {
            return Error(
                CapabilityAssignmentErrorCode.ExecutingAgentInvalid,
                $"ExecutingAgentId \"{executingAgentId}\" doesn't exist for this tenant/site or doesn't declare this capability.");
        }

        var existingActive = await _assignments.GetActiveByDeviceAndCapabilityAsync(
            tenant.TenantId, tenant.SiteId, deviceId, capabilityId, cancellationToken);

        if (existingActive != null)
        {
            return Error(
                CapabilityAssignmentErrorCode.AlreadyAssigned,
                "This device already has an active assignment for this capability - update or unassign it first.");
        }

        var directDependencies = await _dependencies.ListByCapabilityAsync(capabilityId, cancellationToken);

        foreach (var dependency in directDependencies)
        {
            var satisfied = await _assignments.GetActiveByDeviceAndCapabilityAsync(
                tenant.TenantId, tenant.SiteId, deviceId, dependency.DependsOnCapabilityId, cancellationToken);

            if (satisfied != null)
                continue;

            var dependsOnEntity = await _capabilities.GetAsync(dependency.DependsOnCapabilityId, cancellationToken);
            var dependsOnName = dependsOnEntity?.CapabilityName ?? dependency.DependsOnCapabilityId;

            return Error(
                CapabilityAssignmentErrorCode.MissingDependency,
                $"Required capability \"{dependsOnName}\" is not enabled for this device.");
        }

        var capability = CapabilityToDomain(capabilityEntity);
        var merged = _configuration.ApplyDefaults(capability, settings);

        if (!_configuration.Validate(capability, merged, out var errors))
        {
            return Error(
                CapabilityAssignmentErrorCode.InvalidConfiguration,
                "Invalid capability configuration: " + string.Join(" ", errors));
        }

        var assignment = new DeviceCapability(
            tenant.TenantId, tenant.SiteId, deviceId, capabilityId, executingAgentId ?? "", enabled, merged);

        var entity = ToEntity(assignment);

        await _assignments.CreateAsync(entity, cancellationToken);

        return new CapabilityAssignmentResult(ToDto(entity), null, null);
    }

    public async Task<CapabilityAssignmentResult> UpdateAssignmentAsync(
        TenantContext tenant,
        string deviceCapabilityId,
        string? executingAgentId,
        bool enabled,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var entity = await _assignments.GetAsync(tenant.TenantId, tenant.SiteId, deviceCapabilityId, cancellationToken);

        if (entity == null)
        {
            return Error(CapabilityAssignmentErrorCode.AssignmentNotFound, "Device capability assignment not found.");
        }

        if (!await IsValidExecutingAgentAsync(tenant, executingAgentId, entity.CapabilityId, cancellationToken))
        {
            return Error(
                CapabilityAssignmentErrorCode.ExecutingAgentInvalid,
                $"ExecutingAgentId \"{executingAgentId}\" doesn't exist for this tenant/site or doesn't declare this capability.");
        }

        var capabilityEntity = await _capabilities.GetAsync(entity.CapabilityId, cancellationToken);

        if (capabilityEntity == null)
        {
            return Error(CapabilityAssignmentErrorCode.CapabilityNotFound, $"CapabilityId \"{entity.CapabilityId}\" doesn't exist.");
        }

        var capability = CapabilityToDomain(capabilityEntity);
        var merged = _configuration.ApplyDefaults(capability, settings);

        if (!_configuration.Validate(capability, merged, out var errors))
        {
            return Error(
                CapabilityAssignmentErrorCode.InvalidConfiguration,
                "Invalid capability configuration: " + string.Join(" ", errors));
        }

        var assignment = ToDomain(entity);
        assignment.Update(executingAgentId ?? "", enabled, merged);

        var updated = ToEntity(assignment);
        updated.ETag = entity.ETag;

        await _assignments.UpdateAsync(updated, cancellationToken);

        return new CapabilityAssignmentResult(ToDto(updated), null, null);
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

    private static CapabilityAssignmentResult Error(CapabilityAssignmentErrorCode code, string message)
    {
        return new CapabilityAssignmentResult(null, code, message);
    }

    // Empty ExecutingAgentId means "not assigned yet" - always valid. A
    // non-empty one must (ADR-058) resolve to a real Agent in this exact
    // Tenant/Site - same authorization boundary
    // DeviceService.IsValidOwningAgentAsync enforces for Device.OwningAgentId
    // - AND (ADR-059) that Agent must have an active AgentCapability
    // declaration for this exact CapabilityId.
    private async Task<bool> IsValidExecutingAgentAsync(
        TenantContext tenant,
        string? executingAgentId,
        string capabilityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executingAgentId))
            return true;

        var agent = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, executingAgentId, cancellationToken);

        if (agent == null)
            return false;

        var declaration = await _agentCapabilities.GetActiveByAgentAndCapabilityAsync(
            tenant.TenantId, tenant.SiteId, executingAgentId, capabilityId, cancellationToken);

        return declaration != null;
    }

    // Minimal Capability rehydration for config validation only - mirrors
    // CapabilityManagementService.ToDomain's mapping, duplicated rather
    // than shared, same "each service maps its own way" convention this
    // codebase already uses for DeviceCapabilityEntity<->DeviceCapability.
    private static Capability CapabilityToDomain(CapabilityEntity entity)
    {
        return Capability.Rehydrate(
            entity.RowKey,
            entity.CapabilityName,
            entity.CapabilityKey,
            Enum.Parse<CapabilityType>(entity.CapabilityType),
            string.IsNullOrWhiteSpace(entity.Status) ? CapabilityStatus.Active : Enum.Parse<CapabilityStatus>(entity.Status),
            ParseSchema(entity.ConfigurationSchema),
            entity.ConfigurationSchemaVersion == 0 ? 1 : entity.ConfigurationSchemaVersion,
            ParseCapabilityDefaults(entity.DefaultConfiguration));
    }

    private static IReadOnlyList<CapabilityConfigurationField> ParseSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
            return EmptySchema;

        var dtos = System.Text.Json.JsonSerializer.Deserialize<List<CapabilityConfigurationFieldDto>>(schema);

        if (dtos == null || dtos.Count == 0)
            return EmptySchema;

        return dtos
            .Select(dto => new CapabilityConfigurationField(
                dto.Name,
                Enum.Parse<CapabilityConfigurationFieldType>(dto.Type),
                dto.Required,
                dto.Minimum,
                dto.Maximum,
                dto.AllowedValues,
                dto.DefaultValue))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseCapabilityDefaults(string? defaults)
    {
        if (string.IsNullOrWhiteSpace(defaults))
            return EmptySettings;

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(defaults)
            ?? new Dictionary<string, string>();
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
