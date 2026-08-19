using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Configuration;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Cloud.Api;

public sealed class DeviceCapabilitiesQueryService : IDeviceCapabilitiesQueryService
{
    // The Agent side reads these same blobs via Microsoft.Extensions.Configuration's
    // binder, which parses string enums natively. Raw JsonSerializer.Deserialize
    // does not do this by default (it expects enums as numbers), so DeviceType's
    // "Camera" string needs an explicit converter here or every deserialize
    // throws JsonException and gets silently swallowed as a 404.
    private static readonly JsonSerializerOptions DeviceJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IBlobStorageService _blobStorage;
    private readonly IAgentQueryService _agentQueryService;
    private readonly IDeviceHeartbeatReader _deviceHeartbeats;

    public DeviceCapabilitiesQueryService(
        IBlobStorageService blobStorage,
        IAgentQueryService agentQueryService,
        IDeviceHeartbeatReader deviceHeartbeats)
    {
        _blobStorage = blobStorage;
        _agentQueryService = agentQueryService;
        _deviceHeartbeats = deviceHeartbeats;
    }

    public async Task<DeviceCapabilitiesDto?> GetCapabilitiesAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await TryLoadDeviceAsync(tenant, deviceId, cancellationToken);
        if (device is null)
            return null;

        // Tenant-scope via OwningAgentId, not a Table Storage device
        // lookup - a deliberate improvement over the pattern every other
        // query service here uses, not a copy of it: IDeviceQueryService.GetDeviceAsync
        // requires the device to have already sent a heartbeat, which a
        // freshly-configured device might not have yet even though its
        // blob is completely real. The blob is never returned to an
        // unauthenticated/wrong-tenant caller either way - this check gates
        // the response, the download above already happened server-side.
        //
        // Decision-log.md ADR-078 - caches the resolved AgentSummaryDto,
        // not just a bool, so the same lookup this ownership check already
        // does also answers "is the owning agent Healthy" for the new
        // per-capability OperationalStatus below, with no second call.
        var agentCache = new Dictionary<string, AgentSummaryDto?>(StringComparer.Ordinal);

        if (!await IsOwnedByTenantAsync(tenant, device.OwningAgentId, agentCache, cancellationToken))
            return null;

        var agentStatus = agentCache.GetValueOrDefault(device.OwningAgentId)?.Status;

        // Decision-log.md ADR-078 - the device's own heartbeat row is the
        // "runtime reports capability active" signal the Phase 8 spec asks
        // for, for the two capability types that actually have one
        // (SinkCleanlinessEnabled/ObjectDetectionEnabled). Found live during
        // this pass's own verification: DeviceHeartbeatEntity's PartitionKey
        // is TenantId|SiteId|AgentId, not TenantId|SiteId - the AgentId
        // segment isn't known ahead of a lookup by deviceId alone, so a
        // direct GetAsync point lookup (what this used to be) silently
        // returns null. Scans by tenant/site and filters by RowKey instead -
        // the exact same shape DeviceQueryService.GetDeviceAsync already
        // uses to find a device by id for the same reason.
        var heartbeat = (await _deviceHeartbeats.GetByTenantAsync(
                tenant.TenantId, tenant.SiteId, cancellationToken))
            .FirstOrDefault(e => string.Equals(e.RowKey, deviceId, StringComparison.Ordinal));

        var capabilities = await BuildCapabilitiesAsync(tenant, device, agentStatus, heartbeat, cancellationToken);
        var triggeredBy = await BuildTriggeredByAsync(tenant, deviceId, agentCache, cancellationToken);
        var sourceSensors = BuildSourceSensors(device);

        return new DeviceCapabilitiesDto(capabilities, triggeredBy, sourceSensors);
    }

    // Decision-log.md ADR-078 - Running iff the owning Agent is Healthy AND
    // this service is Enabled AND, where a runtime signal actually exists
    // for it (runtimeActive non-null), that signal agrees. Unknown when the
    // Agent's health can't vouch for anything the device last reported -
    // same reasoning DeviceStatusResolver already uses for its own
    // "can't trust it" cases, one level down.
    private static string ComputeOperationalStatus(string? agentStatus, bool enabled, bool? runtimeActive)
    {
        if (!string.Equals(agentStatus, nameof(DeviceHeartbeatStatus.Healthy), StringComparison.Ordinal))
            return nameof(CapabilityOperationalStatus.Unknown);

        if (!enabled || runtimeActive == false)
            return nameof(CapabilityOperationalStatus.NotRunning);

        return nameof(CapabilityOperationalStatus.Running);
    }

    // The blob at DeviceConfigBlob.BlobName holds one of *two* shapes: the
    // legacy flat DeviceOptions shape (any device never republished since
    // ADR-064), or the capabilities[]-shaped DeviceRuntimeConfigWireDocument
    // that DeviceRuntimeConfigurationPublisher now writes to both the
    // versioned blob and this flat name. Deserializing the second one
    // straight into DeviceOptions binds almost nothing - Name/Type/Enabled/
    // Location/Brand/Model/Firmware live under "Device", and Schedule/
    // Trigger/SinkCleanliness/ObjectDetection/Sensors live inside
    // "Capabilities" - so this endpoint silently reported a blank name, a
    // Type defaulted to Camera, and no derived capabilities for every
    // republished device. Found by shape-checking real cached documents:
    // four of five were still legacy, which is why it went unnoticed.
    //
    // DeviceConfigRuntimeAdapter.Adapt is the same translation the Agent
    // already applies at startup, moved into Vivnest.Core so both sides
    // share one implementation rather than maintaining two (the codebase's
    // own "extract when a second real consumer needs it" rule - this is
    // that second consumer). A legacy-shape document passes through it
    // unchanged, so both shapes converge here on one code path.
    private async Task<DeviceOptions?> TryLoadDeviceAsync(
        TenantContext tenant, string deviceId, CancellationToken cancellationToken)
    {
        var key = new ConfigBlobKey(tenant.TenantId, tenant.SiteId, deviceId);

        try
        {
            byte[]? bytes = null;

            // Scoped layout first, then the legacy one - a device that has
            // not been republished since scoping only exists at the
            // unscoped name.
            foreach (var candidate in new[] { key, key.Unscoped() })
            {
                try
                {
                    bytes = await _blobStorage.DownloadAsync(
                        DeviceConfigBlob.ContainerName,
                        DeviceConfigBlob.BlobName(candidate),
                        cancellationToken);

                    break;
                }
                catch (RequestFailedException inner) when (inner.Status == 404)
                {
                }
            }

            if (bytes == null || JsonNode.Parse(bytes) is not JsonObject raw)
                return null;

            var flattened = DeviceConfigRuntimeAdapter.Adapt(raw);

            return flattened.Deserialize<DeviceOptions>(DeviceJsonOptions);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (UnsupportedConfigurationSchemaException)
        {
            // Same tolerance the Agent applies per-device: a document
            // declaring a schema this build doesn't understand is skipped,
            // not surfaced as a 500.
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> IsOwnedByTenantAsync(
        TenantContext tenant,
        string owningAgentId,
        Dictionary<string, AgentSummaryDto?> agentCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owningAgentId))
            return false;

        if (agentCache.TryGetValue(owningAgentId, out var cached))
            return cached is not null;

        var agent = await _agentQueryService.GetAgentAsync(tenant, owningAgentId, cancellationToken);
        agentCache[owningAgentId] = agent;

        return agent is not null;
    }

    // A capability (e.g. "Image Classification") is a fixed, canonical
    // concept, distinct from the device/service that actually provides it
    // for this device (its Services list) - see decision-log.md ADR-041.
    // Only capabilities this device actually has get an entry; there's no
    // placeholder row for e.g. "Motion Detection" on a Camera device.
    private async Task<IReadOnlyList<CapabilityDto>> BuildCapabilitiesAsync(
        TenantContext tenant,
        DeviceOptions device,
        string? agentStatus,
        DeviceHeartbeatEntity? heartbeat,
        CancellationToken cancellationToken)
    {
        var capabilities = new List<CapabilityDto>();
        var aiAgentCache = new Dictionary<string, AiClassificationOptions?>(StringComparer.Ordinal);

        if (device.Type == DeviceType.Camera)
        {
            capabilities.Add(new CapabilityDto(
                Name: "Image Capture",
                Source: "Built-in",
                Services: [new CapabilityServiceDto(
                    Name: "Camera",
                    Enabled: device.Enabled,
                    ExecutingAgentId: null,
                    RoiLeft: null, RoiTop: null, RoiRight: null, RoiBottom: null,
                    Host: device.Settings.Host,
                    Username: device.Settings.Username,
                    ModelPath: null, ConfidenceThreshold: null,
                    LivenessInterval: null, WarningMultiplier: null,
                    OperationalStatus: ComputeOperationalStatus(agentStatus, device.Enabled, null))]));
        }

        // Each native device type's own primary function, same treatment as
        // Image Capture above - previously only Camera had one, leaving a
        // MotionSensor/SmartPlug device's Capabilities tab showing nothing
        // but Health Monitoring. Both are direct hardware readings, not run
        // through an AI model/agent - Built-in, not Derived.
        if (device.Type == DeviceType.MotionSensor)
        {
            capabilities.Add(new CapabilityDto(
                Name: "Motion Detection",
                Source: "Built-in",
                Services: [new CapabilityServiceDto(
                    Name: "MotionSensing",
                    Enabled: device.Enabled,
                    ExecutingAgentId: null,
                    RoiLeft: null, RoiTop: null, RoiRight: null, RoiBottom: null,
                    Host: device.Settings.Host,
                    Username: device.Settings.Username,
                    ModelPath: null, ConfidenceThreshold: null,
                    LivenessInterval: null, WarningMultiplier: null,
                    OperationalStatus: ComputeOperationalStatus(agentStatus, device.Enabled, null))]));
        }

        if (device.Type == DeviceType.SmartPlug)
        {
            capabilities.Add(new CapabilityDto(
                Name: "Power Monitoring",
                Source: "Built-in",
                Services: [new CapabilityServiceDto(
                    Name: "PowerMonitoring",
                    Enabled: device.Enabled,
                    ExecutingAgentId: null,
                    RoiLeft: null, RoiTop: null, RoiRight: null, RoiBottom: null,
                    Host: device.Settings.Host,
                    Username: device.Settings.Username,
                    ModelPath: null, ConfidenceThreshold: null,
                    LivenessInterval: null, WarningMultiplier: null,
                    OperationalStatus: ComputeOperationalStatus(agentStatus, device.Enabled, null))]));
        }

        if (device.SinkCleanliness is { } sinkRoi)
        {
            var model = sinkRoi.ExecutingAgentId.Length > 0
                ? (await TryLoadAiClassificationAsync(tenant, sinkRoi.ExecutingAgentId, aiAgentCache, cancellationToken))
                    ?.Devices
                    .FirstOrDefault(d => string.Equals(d.DeviceId, device.DeviceId, StringComparison.Ordinal))
                    ?.SinkCleanliness
                : null;

            capabilities.Add(new CapabilityDto(
                Name: "Image Classification",
                Source: "Derived",
                Services: [new CapabilityServiceDto(
                    Name: "SinkCleanliness",
                    Enabled: sinkRoi.Enabled,
                    ExecutingAgentId: sinkRoi.ExecutingAgentId,
                    RoiLeft: sinkRoi.RoiLeft, RoiTop: sinkRoi.RoiTop, RoiRight: sinkRoi.RoiRight, RoiBottom: sinkRoi.RoiBottom,
                    Host: null, Username: null,
                    ModelPath: model?.ModelPath, ConfidenceThreshold: model?.ConfidenceThreshold,
                    LivenessInterval: null, WarningMultiplier: null,
                    OperationalStatus: ComputeOperationalStatus(
                        agentStatus, sinkRoi.Enabled, heartbeat?.SinkCleanlinessEnabled))]));
        }

        if (device.ObjectDetection is { } detectionRoi)
        {
            var model = detectionRoi.ExecutingAgentId.Length > 0
                ? (await TryLoadAiClassificationAsync(tenant, detectionRoi.ExecutingAgentId, aiAgentCache, cancellationToken))
                    ?.Devices
                    .FirstOrDefault(d => string.Equals(d.DeviceId, device.DeviceId, StringComparison.Ordinal))
                    ?.ObjectDetection
                : null;

            capabilities.Add(new CapabilityDto(
                Name: "Image Analysis",
                Source: "Derived",
                Services: [new CapabilityServiceDto(
                    Name: "ObjectDetection",
                    Enabled: detectionRoi.Enabled,
                    ExecutingAgentId: detectionRoi.ExecutingAgentId,
                    RoiLeft: detectionRoi.RoiLeft, RoiTop: detectionRoi.RoiTop, RoiRight: detectionRoi.RoiRight, RoiBottom: detectionRoi.RoiBottom,
                    Host: null, Username: null,
                    ModelPath: model?.ModelPath, ConfidenceThreshold: model?.ConfidenceThreshold,
                    LivenessInterval: null, WarningMultiplier: null,
                    OperationalStatus: ComputeOperationalStatus(
                        agentStatus, detectionRoi.Enabled, heartbeat?.ObjectDetectionEnabled))]));
        }

        // "SystemMetrics" is agent-scoped, not device-scoped (confirmed
        // during the earlier mockup brainstorm: AgentMetrics/AgentHeartbeat
        // have no per-device fields at all), so it's deliberately not
        // listed here - every device gets Health Monitoring, unconditionally.
        capabilities.Add(new CapabilityDto(
            Name: "Health Monitoring",
            Source: "System",
            Services: [new CapabilityServiceDto(
                Name: "DeviceHeartbeat",
                Enabled: true,
                ExecutingAgentId: null,
                RoiLeft: null, RoiTop: null, RoiRight: null, RoiBottom: null,
                Host: null, Username: null,
                ModelPath: null, ConfidenceThreshold: null,
                LivenessInterval: device.LivenessInterval.ToString(), WarningMultiplier: device.WarningMultiplier,
                OperationalStatus: ComputeOperationalStatus(agentStatus, true, null))]));

        return capabilities;
    }

    private async Task<AiClassificationOptions?> TryLoadAiClassificationAsync(
        TenantContext tenant,
        string executingAgentId,
        Dictionary<string, AiClassificationOptions?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(executingAgentId, out var cached))
            return cached;

        AiClassificationOptions? result;

        try
        {
            byte[]? bytes = null;
            var key = new ConfigBlobKey(tenant.TenantId, tenant.SiteId, executingAgentId);

            foreach (var candidate in new[] { key, key.Unscoped() })
            {
                try
                {
                    bytes = await _blobStorage.DownloadAsync(
                        AgentConfigBlob.ContainerName,
                        AgentConfigBlob.BlobName(candidate),
                        cancellationToken);

                    break;
                }
                catch (RequestFailedException inner) when (inner.Status == 404)
                {
                }
            }

            if (bytes == null)
            {
                cache[executingAgentId] = null;
                return null;
            }

            using var doc = JsonDocument.Parse(bytes);

            result = doc.RootElement.TryGetProperty("AiClassification", out var aiClassificationElement)
                ? aiClassificationElement.Deserialize<AiClassificationOptions>()
                : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            result = null;
        }
        catch (JsonException)
        {
            result = null;
        }

        cache[executingAgentId] = result;
        return result;
    }

    // O(N) blob reads, where N is now this tenant/site's own devices rather
    // than every device in the storage account. It used to download other
    // tenants' device blobs and discard them after an ownership check -
    // correct, but it meant routinely pulling data across the wire that the
    // caller had no business seeing. The ownership check below is kept
    // anyway: legacy-layout blobs still have no tenant in their name, so
    // the prefix filter alone cannot carry the guarantee yet.
    private async Task<IReadOnlyList<TriggeredByDto>> BuildTriggeredByAsync(
        TenantContext tenant,
        string deviceId,
        Dictionary<string, AgentSummaryDto?> agentCache,
        CancellationToken cancellationToken)
    {
        var triggeredBy = new List<TriggeredByDto>();

        IReadOnlyList<string> blobNames;

        try
        {
            blobNames = await _blobStorage.ListBlobNamesAsync(DeviceConfigBlob.ContainerName, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return triggeredBy;
        }

        var prefix = DeviceConfigBlob.Prefix(tenant.TenantId, tenant.SiteId);

        // Two shapes are in play while both layouts exist: "{prefix}{id}.json"
        // and, for anything not yet republished, a bare "{id}.json" with no
        // prefix at all. Version and manifest blobs are excluded by the
        // slash test - they are not device documents and deserializing them
        // as one only ever produced nulls that were then skipped.
        var candidateNames = blobNames
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                ? !n[prefix.Length..].Contains('/')
                : !n.Contains('/'))
            .ToList();

        foreach (var blobName in candidateNames)
        {
            byte[] bytes;

            try
            {
                bytes = await _blobStorage.DownloadAsync(DeviceConfigBlob.ContainerName, blobName, cancellationToken);
            }
            catch (RequestFailedException)
            {
                continue;
            }

            DeviceOptions? candidate;

            try
            {
                candidate = JsonSerializer.Deserialize<DeviceOptions>(bytes, DeviceJsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (candidate is null)
                continue;

            if (!candidate.Trigger.DeviceIds.Contains(deviceId, StringComparer.Ordinal))
                continue;

            if (!await IsOwnedByTenantAsync(tenant, candidate.OwningAgentId, agentCache, cancellationToken))
                continue;

            triggeredBy.Add(new TriggeredByDto(
                DeviceId: candidate.DeviceId,
                DeviceName: candidate.Name,
                DeviceType: candidate.Type.ToString()));
        }

        return triggeredBy;
    }

    // "Used by" is computed, not stored - there's no generic
    // capability-to-sensor graph in this codebase, and building one now
    // would be premature for a relationship with exactly one real instance
    // today. The sensor named "Camera" feeds ObjectDetection/SinkCleanliness
    // (both process camera frames) when each is enabled, plus the Camera
    // capability itself; every other sensor name is used by nothing yet.
    private static IReadOnlyList<SourceSensorDto> BuildSourceSensors(DeviceOptions device)
    {
        return device.Sensors
            .Select(sensor => new SourceSensorDto(
                Name: sensor.Name,
                Accessible: sensor.Accessible,
                InaccessibleReason: sensor.InaccessibleReason,
                UsedByCount: ComputeUsedByCount(device, sensor.Name)))
            .ToList();
    }

    private static int ComputeUsedByCount(DeviceOptions device, string sensorName)
    {
        if (!string.Equals(sensorName, "Camera", StringComparison.Ordinal))
            return 0;

        var count = device.Type == DeviceType.Camera ? 1 : 0;

        if (device.SinkCleanliness is { Enabled: true })
            count++;

        if (device.ObjectDetection is { Enabled: true })
            count++;

        return count;
    }
}
