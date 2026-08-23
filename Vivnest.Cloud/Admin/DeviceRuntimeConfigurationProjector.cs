using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Configuration;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin;

public sealed class DeviceRuntimeConfigurationProjector : IDeviceRuntimeConfigurationProjector
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private readonly IDeviceRegistryStore _devices;
    private readonly IDeviceTypeStore _deviceTypes;
    private readonly IAgentRegistryStore _agentRegistry;
    private readonly IDeviceCapabilityStore _deviceCapabilities;
    private readonly ICapabilityStore _capabilities;
    private readonly ICapabilityConfigurationService _capabilityConfiguration;
    private readonly IEnumerable<ICapabilityRuntimeProjector> _capabilityProjectors;

    public DeviceRuntimeConfigurationProjector(
        IDeviceRegistryStore devices,
        IDeviceTypeStore deviceTypes,
        IAgentRegistryStore agentRegistry,
        IDeviceCapabilityStore deviceCapabilities,
        ICapabilityStore capabilities,
        ICapabilityConfigurationService capabilityConfiguration,
        IEnumerable<ICapabilityRuntimeProjector> capabilityProjectors)
    {
        _devices = devices;
        _deviceTypes = deviceTypes;
        _agentRegistry = agentRegistry;
        _deviceCapabilities = deviceCapabilities;
        _capabilities = capabilities;
        _capabilityConfiguration = capabilityConfiguration;
        _capabilityProjectors = capabilityProjectors;
    }

    public async Task<DeviceRuntimeConfigurationDocumentDto?> ProjectAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        if (device == null)
            return null;

        var warnings = new List<string>();

        var runtimeDeviceId = device.RuntimeDeviceId;

        if (string.IsNullOrWhiteSpace(runtimeDeviceId))
        {
            warnings.Add(
                "RuntimeDeviceId is not set - this projection can't be matched to a real device-config file yet.");
            runtimeDeviceId = null;
        }

        string? type = null;

        if (!string.IsNullOrWhiteSpace(device.DeviceTypeId))
        {
            var deviceType = await _deviceTypes.GetAsync(device.DeviceTypeId, cancellationToken);

            if (deviceType == null)
            {
                warnings.Add($"DeviceTypeId \"{device.DeviceTypeId}\" doesn't exist.");
            }
            else
            {
                type = MatchRuntimeDeviceType(deviceType.DeviceTypeName);

                if (type == null)
                {
                    warnings.Add(
                        $"DeviceType \"{deviceType.DeviceTypeName}\" has no matching runtime DeviceType enum value.");
                }
            }
        }

        string? owningAgentId = null;

        if (!string.IsNullOrWhiteSpace(device.OwningAgentId))
        {
            var owningAgent = await _agentRegistry.GetAsync(
                tenant.TenantId, tenant.SiteId, device.OwningAgentId, cancellationToken);

            if (owningAgent == null)
            {
                warnings.Add($"OwningAgentId \"{device.OwningAgentId}\" doesn't exist.");
            }
            else if (string.IsNullOrWhiteSpace(owningAgent.RuntimeAgentId))
            {
                warnings.Add(
                    $"OwningAgentId \"{device.OwningAgentId}\" ({owningAgent.Name}) has no RuntimeAgentId mapped yet.");
            }
            else
            {
                owningAgentId = owningAgent.RuntimeAgentId;
            }
        }

        var settings = ParseSettings(device.Settings);

        var capabilities = await ProjectCapabilitiesAsync(tenant, device, warnings, cancellationToken);

        return new DeviceRuntimeConfigurationDocumentDto(
            runtimeDeviceId,
            device.Name,
            type,
            string.Equals(device.Status, nameof(DeviceStatus.Active), StringComparison.Ordinal),
            device.Location,
            device.Brand,
            device.Model,
            device.Firmware,
            owningAgentId,
            settings,
            device.LivenessIntervalSeconds,
            device.WarningMultiplier,
            capabilities,
            warnings);
    }

    // Runs each of the Device's Active DeviceCapability assignments through
    // the shared ICapabilityRuntimeProjector registry (ADR-064), keeping
    // only each result's DeviceEntry - AgentEntry contributions are this
    // same registry's output too, but collected independently by
    // AgentRuntimeConfigurationProjector when the executing Agent is
    // published, not here.
    private async Task<IReadOnlyList<CapabilityDocumentEntryDto>> ProjectCapabilitiesAsync(
        TenantContext tenant,
        DeviceRegistryEntity device,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var assignments = await _deviceCapabilities.GetByDeviceAsync(
            tenant.TenantId, tenant.SiteId, device.RowKey, cancellationToken);

        var entries = new List<CapabilityDocumentEntryDto>();

        foreach (var assignment in assignments)
        {
            if (!string.Equals(assignment.Status, nameof(DeviceCapabilityStatus.Active), StringComparison.Ordinal))
                continue;

            var capability = await _capabilities.GetAsync(assignment.CapabilityId, cancellationToken);

            if (capability == null)
            {
                warnings.Add($"CapabilityId \"{assignment.CapabilityId}\" doesn't exist.");
                continue;
            }

            var projector = CapabilityRuntimeProjectorLookup.Find(_capabilityProjectors, capability.CapabilityName);

            if (projector == null)
            {
                warnings.Add(
                    $"No runtime projector registered for capability \"{capability.CapabilityName}\" - this device's assigned capability won't be reflected in the published config.");
                continue;
            }

            var executingRuntimeAgentId = await ResolveRuntimeAgentIdAsync(
                tenant, assignment.ExecutingAgentId, warnings, cancellationToken);

            // ADR-100 - "required" means resolvable after defaults, not
            // "must be stored". Defaults are materialised at write time
            // too, so for anything created through the admin API this is a
            // no-op; it covers what write-time defaulting cannot - a schema
            // field added after an assignment was written, and settings
            // edited outside the API. Supplied values always win, so
            // re-applying is idempotent.
            var effective = _capabilityConfiguration.ResolveEffectiveSettings(
                capability, ParseSettings(assignment.Settings));

            var result = projector.Project(
                WithEffectiveSettings(assignment, effective), device, executingRuntimeAgentId);

            warnings.AddRange(result.Warnings);

            if (result.DeviceEntry != null)
                entries.Add(result.DeviceEntry);
        }

        return entries;
    }

    // A shallow copy so the stored entity is never mutated - the same
    // assignment object is not re-read per publish, and a projector that
    // saw mutated settings would make the defaulting invisible to anyone
    // reading the row afterwards.
    private static DeviceCapabilityEntity WithEffectiveSettings(
        DeviceCapabilityEntity assignment,
        IReadOnlyDictionary<string, string> effective) =>
        new()
        {
            PartitionKey = assignment.PartitionKey,
            RowKey = assignment.RowKey,
            ETag = assignment.ETag,
            Timestamp = assignment.Timestamp,
            TenantId = assignment.TenantId,
            SiteId = assignment.SiteId,
            DeviceId = assignment.DeviceId,
            CapabilityId = assignment.CapabilityId,
            Status = assignment.Status,
            Enabled = assignment.Enabled,
            ExecutingAgentId = assignment.ExecutingAgentId,
            Settings = System.Text.Json.JsonSerializer.Serialize(effective)
        };

    private async Task<string?> ResolveRuntimeAgentIdAsync(
        TenantContext tenant,
        string executingAgentId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executingAgentId))
            return null;

        var executingAgent = await _agentRegistry.GetAsync(
            tenant.TenantId, tenant.SiteId, executingAgentId, cancellationToken);

        if (executingAgent == null)
        {
            warnings.Add($"ExecutingAgentId \"{executingAgentId}\" doesn't exist.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(executingAgent.RuntimeAgentId))
        {
            warnings.Add(
                $"ExecutingAgentId \"{executingAgentId}\" ({executingAgent.Name}) has no RuntimeAgentId mapped yet.");
            return null;
        }

        return executingAgent.RuntimeAgentId;
    }

    // "Motion Sensor" (admin-typed, space-separated) -> DeviceType.MotionSensor -
    // matched case/whitespace-insensitively since the Admin DeviceType master
    // list is free text, not the fixed runtime enum.
    private static string? MatchRuntimeDeviceType(string deviceTypeName)
    {
        return RuntimeNameMatch.ToDeviceType(deviceTypeName)?.ToString();
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return EmptySettings;

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
