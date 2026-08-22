using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

public sealed class AgentRuntimeConfigurationProjector
    : IAgentRuntimeConfigurationProjector
{
    private readonly IAgentRegistryStore _agentRegistry;
    private readonly IDeviceRegistryStore _devices;
    private readonly IDeviceCapabilityStore _deviceCapabilities;
    private readonly ICapabilityStore _capabilities;
    private readonly IAgentCapabilityStore _agentCapabilities;
    private readonly IEnumerable<ICapabilityRuntimeProjector> _capabilityProjectors;

    public AgentRuntimeConfigurationProjector(
        IAgentRegistryStore agentRegistry,
        IDeviceRegistryStore devices,
        IDeviceCapabilityStore deviceCapabilities,
        ICapabilityStore capabilities,
        IAgentCapabilityStore agentCapabilities,
        IEnumerable<ICapabilityRuntimeProjector> capabilityProjectors)
    {
        _agentRegistry = agentRegistry;
        _devices = devices;
        _deviceCapabilities = deviceCapabilities;
        _capabilities = capabilities;
        _agentCapabilities = agentCapabilities;
        _capabilityProjectors = capabilityProjectors;
    }

    public async Task<AgentRuntimeConfigurationDocumentDto?> ProjectAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        // ------------------------------------------------------------
        // Resolve Agent
        // ------------------------------------------------------------

        var agent = await _agentRegistry.GetAsync(
            tenant.TenantId,
            tenant.SiteId,
            agentId,
            cancellationToken);

        if (agent == null)
            return null;

        var warnings = new List<string>();

        var runtimeAgentId = agent.RuntimeAgentId;

        // ------------------------------------------------------------
        // Agent Capability Assignments
        // ------------------------------------------------------------

        var agentCapabilities =
            await _agentCapabilities.GetByAgentAsync(
                tenant.TenantId,
                tenant.SiteId,
                agentId,
                cancellationToken);

        // ------------------------------------------------------------
        // 5F-B.4
        //
        // Only Active AgentCapability assignments are eligible
        // for runtime projection.
        // ------------------------------------------------------------

        var activeAgentCapabilities =
            agentCapabilities
                .Where(x =>
                    string.Equals(
                        x.Status,
                        nameof(AgentCapabilityStatus.Active),
                        StringComparison.Ordinal))
                .ToList();

        // ------------------------------------------------------------
        // 5F-B.5
        //
        // Resolve each active AgentCapability assignment against
        // the existing Capability store.
        //
        // AgentCapability tells us:
        //
        //     Agent X can execute Capability Y
        //
        // Capability tells us:
        //
        //     What Capability Y actually is.
        // ------------------------------------------------------------

        var capabilityEntries = new List<AgentCapabilityRuntimeDto>();

        foreach (var assignment in activeAgentCapabilities)
        {
            var capability =
                await _capabilities.GetAsync(
                    assignment.CapabilityId,
                    cancellationToken);

            // --------------------------------------------------------
            // Capability definition doesn't exist
            // --------------------------------------------------------

            if (capability == null)
            {
                warnings.Add(
                    $"AgentCapability \"{assignment.RowKey}\" " +
                    $"references CapabilityId \"{assignment.CapabilityId}\", " +
                    "which doesn't exist.");

                continue;
            }

            // --------------------------------------------------------
            // Capability status
            //
            // CapabilityEntity.Status is stored as string?.
            //
            // Existing domain convention:
            // blank/null status is treated as Active.
            // --------------------------------------------------------

            var capabilityStatusIsInactive =
                !string.IsNullOrWhiteSpace(capability.Status)
                && !string.Equals(
                    capability.Status,
                    nameof(CapabilityStatus.Active),
                    StringComparison.Ordinal);

            if (capabilityStatusIsInactive)
            {
                warnings.Add(
                    $"CapabilityId \"{assignment.CapabilityId}\" " +
                    $"has status \"{capability.Status}\" and won't be " +
                    $"published for Agent \"{agentId}\".");

                continue;
            }

            // A capability with no CapabilityKey cannot be matched by any
            // Agent: CapabilityHost joins on manifest id, and the GUID
            // RowKey is not a manifest id. Publishing it anyway produced an
            // assignment the Agent could only warn about, with a blank or
            // meaningless id - so it is refused here, where the reason is
            // still legible. See decision-log.md ADR-096.
            if (string.IsNullOrWhiteSpace(capability.CapabilityKey))
            {
                warnings.Add(
                    $"Capability \"{capability.CapabilityName}\" " +
                    $"({capability.RowKey}) has no CapabilityKey and " +
                    $"won't be published for Agent \"{agentId}\".");

                continue;
            }

            // --------------------------------------------------------
            // Capability is valid and active
            // --------------------------------------------------------

            capabilityEntries.Add(
                new AgentCapabilityRuntimeDto(
                    capability.RowKey,
                    capability.CapabilityKey,
                    capability.CapabilityName,
                    true));
        }

        // ------------------------------------------------------------
        // RuntimeAgentId validation
        // ------------------------------------------------------------

        if (string.IsNullOrWhiteSpace(runtimeAgentId))
        {
            warnings.Add(
                "RuntimeAgentId is not set - this projection can't be matched to a real agent-config file yet.");
        }

        // ------------------------------------------------------------
        // Device capability contributions
        //
        // Devices[]-shaped contributions keyed by RuntimeDeviceId,
        // each holding at most one ObjectDetection + one
        // SinkCleanliness contribution.
        //
        // This preserves the existing AI classification projection.
        // ------------------------------------------------------------

        var deviceContributions =
            new Dictionary<
                string,
                Dictionary<
                    string,
                    IReadOnlyDictionary<string, string>>>();

        var assignments =
            await _deviceCapabilities.GetByExecutingAgentAsync(
                tenant.TenantId,
                tenant.SiteId,
                agentId,
                cancellationToken);

        foreach (var assignment in assignments)
        {
            // --------------------------------------------------------
            // Existing DeviceCapability active filter
            // --------------------------------------------------------

            if (!string.Equals(
                    assignment.Status,
                    nameof(DeviceCapabilityStatus.Active),
                    StringComparison.Ordinal))
            {
                continue;
            }

            // --------------------------------------------------------
            // Resolve Device
            // --------------------------------------------------------

            var device = await _devices.GetAsync(
                tenant.TenantId,
                tenant.SiteId,
                assignment.DeviceId,
                cancellationToken);

            if (device == null)
            {
                warnings.Add(
                    $"DeviceCapability \"{assignment.RowKey}\" " +
                    $"references DeviceId \"{assignment.DeviceId}\", " +
                    "which doesn't exist.");

                continue;
            }

            // --------------------------------------------------------
            // Runtime Device ID is required for Agent configuration
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(device.RuntimeDeviceId))
            {
                warnings.Add(
                    $"Device \"{device.Name}\" ({assignment.DeviceId}) " +
                    "has no RuntimeDeviceId mapped yet - its capability " +
                    "assignment to this Agent can't be published.");

                continue;
            }

            // --------------------------------------------------------
            // Resolve Capability
            // --------------------------------------------------------

            var capability =
                await _capabilities.GetAsync(
                    assignment.CapabilityId,
                    cancellationToken);

            if (capability == null)
            {
                warnings.Add(
                    $"CapabilityId \"{assignment.CapabilityId}\" " +
                    "doesn't exist.");

                continue;
            }

            // --------------------------------------------------------
            // Resolve capability-specific runtime projector
            // --------------------------------------------------------

            var projector =
                CapabilityRuntimeProjectorLookup.Find(
                    _capabilityProjectors,
                    capability.CapabilityName);

            if (projector == null)
            {
                warnings.Add(
                    $"No runtime projector registered for capability " +
                    $"\"{capability.CapabilityName}\" - this device's " +
                    "assignment to this Agent won't be reflected in " +
                    "the published config.");

                continue;
            }

            // --------------------------------------------------------
            // Project DeviceCapability into Agent configuration
            // --------------------------------------------------------

            var result =
                projector.Project(
                    assignment,
                    device,
                    runtimeAgentId);

            warnings.AddRange(result.Warnings);

            if (result.AgentEntry == null)
                continue;

            var entry = result.AgentEntry;

            // --------------------------------------------------------
            // Group by RuntimeDeviceId
            // --------------------------------------------------------

            if (!deviceContributions.TryGetValue(
                    entry.RuntimeDeviceId,
                    out var byCapability))
            {
                byCapability =
                    new Dictionary<
                        string,
                        IReadOnlyDictionary<string, string>>();

                deviceContributions[
                    entry.RuntimeDeviceId] = byCapability;
            }

            byCapability[
                entry.CapabilityName] = entry.Settings;
        }

        // ------------------------------------------------------------
        // Build existing AI Classification device DTO
        // ------------------------------------------------------------

        var devicesDto =
            deviceContributions
                .Select(kvp =>
                    new AiDeviceClassificationEntryDto(
                        kvp.Key,

                        kvp.Value.TryGetValue(
                            "Object Detection",
                            out var objectDetection)
                            ? objectDetection
                            : null,

                        kvp.Value.TryGetValue(
                            "Sink Cleanliness",
                            out var sinkCleanliness)
                            ? sinkCleanliness
                            : null))
                .ToList();

        return new AgentRuntimeConfigurationDocumentDto(
             string.IsNullOrWhiteSpace(runtimeAgentId)
                 ? null
                 : runtimeAgentId,
             agent.Name,
             devicesDto,
             capabilityEntries,
             warnings);
    }
}