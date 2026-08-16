using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

public sealed class AgentRuntimeConfigurationProjector : IAgentRuntimeConfigurationProjector
{
    private readonly IAgentRegistryStore _agentRegistry;
    private readonly IDeviceRegistryStore _devices;
    private readonly IDeviceCapabilityStore _deviceCapabilities;
    private readonly ICapabilityStore _capabilities;
    private readonly IEnumerable<ICapabilityRuntimeProjector> _capabilityProjectors;

    public AgentRuntimeConfigurationProjector(
        IAgentRegistryStore agentRegistry,
        IDeviceRegistryStore devices,
        IDeviceCapabilityStore deviceCapabilities,
        ICapabilityStore capabilities,
        IEnumerable<ICapabilityRuntimeProjector> capabilityProjectors)
    {
        _agentRegistry = agentRegistry;
        _devices = devices;
        _deviceCapabilities = deviceCapabilities;
        _capabilities = capabilities;
        _capabilityProjectors = capabilityProjectors;
    }

    public async Task<AgentRuntimeConfigurationDocumentDto?> ProjectAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var agent = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        if (agent == null)
            return null;

        var warnings = new List<string>();

        var runtimeAgentId = agent.RuntimeAgentId;

        if (string.IsNullOrWhiteSpace(runtimeAgentId))
        {
            warnings.Add(
                "RuntimeAgentId is not set - this projection can't be matched to a real agent-config file yet.");
        }

        // Devices[]-shaped contributions keyed by RuntimeDeviceId, each
        // holding at most one ObjectDetection + one SinkCleanliness
        // contribution - mirrors the real AiClassificationOptions.Devices[]
        // entry shape exactly.
        var deviceContributions = new Dictionary<string, Dictionary<string, IReadOnlyDictionary<string, string>>>();

        var assignments = await _deviceCapabilities.GetByExecutingAgentAsync(
            tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        foreach (var assignment in assignments)
        {
            if (!string.Equals(assignment.Status, nameof(DeviceCapabilityStatus.Active), StringComparison.Ordinal))
                continue;

            var device = await _devices.GetAsync(
                tenant.TenantId, tenant.SiteId, assignment.DeviceId, cancellationToken);

            if (device == null)
            {
                warnings.Add(
                    $"DeviceCapability \"{assignment.RowKey}\" references DeviceId \"{assignment.DeviceId}\", which doesn't exist.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(device.RuntimeDeviceId))
            {
                warnings.Add(
                    $"Device \"{device.Name}\" ({assignment.DeviceId}) has no RuntimeDeviceId mapped yet - its capability assignment to this Agent can't be published.");
                continue;
            }

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
                    $"No runtime projector registered for capability \"{capability.CapabilityName}\" - this device's assignment to this Agent won't be reflected in the published config.");
                continue;
            }

            var result = projector.Project(assignment, device, runtimeAgentId);

            warnings.AddRange(result.Warnings);

            if (result.AgentEntry == null)
                continue;

            var entry = result.AgentEntry;

            if (!deviceContributions.TryGetValue(entry.RuntimeDeviceId, out var byCapability))
            {
                byCapability = new Dictionary<string, IReadOnlyDictionary<string, string>>();
                deviceContributions[entry.RuntimeDeviceId] = byCapability;
            }

            byCapability[entry.CapabilityName] = entry.Settings;
        }

        var devicesDto = deviceContributions
            .Select(kvp => new AiDeviceClassificationEntryDto(
                kvp.Key,
                kvp.Value.TryGetValue("Object Detection", out var objectDetection) ? objectDetection : null,
                kvp.Value.TryGetValue("Sink Cleanliness", out var sinkCleanliness) ? sinkCleanliness : null))
            .ToList();

        return new AgentRuntimeConfigurationDocumentDto(
            string.IsNullOrWhiteSpace(runtimeAgentId) ? null : runtimeAgentId,
            agent.Name,
            devicesDto,
            warnings);
    }
}
