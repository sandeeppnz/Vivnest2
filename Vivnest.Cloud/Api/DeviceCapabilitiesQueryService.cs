using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
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

    public DeviceCapabilitiesQueryService(
        IBlobStorageService blobStorage,
        IAgentQueryService agentQueryService)
    {
        _blobStorage = blobStorage;
        _agentQueryService = agentQueryService;
    }

    public async Task<DeviceCapabilitiesDto?> GetCapabilitiesAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await TryLoadDeviceAsync(deviceId, cancellationToken);
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
        var agentCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        if (!await IsOwnedByTenantAsync(tenant, device.OwningAgentId, agentCache, cancellationToken))
            return null;

        var capabilities = await BuildCapabilitiesAsync(device, cancellationToken);
        var triggeredBy = await BuildTriggeredByAsync(tenant, deviceId, agentCache, cancellationToken);
        var sourceSensors = BuildSourceSensors(device);

        return new DeviceCapabilitiesDto(capabilities, triggeredBy, sourceSensors);
    }

    private async Task<DeviceOptions?> TryLoadDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _blobStorage.DownloadAsync(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.BlobName(deviceId),
                cancellationToken);

            return JsonSerializer.Deserialize<DeviceOptions>(bytes, DeviceJsonOptions);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
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
        Dictionary<string, bool> agentCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owningAgentId))
            return false;

        if (agentCache.TryGetValue(owningAgentId, out var cached))
            return cached;

        var agent = await _agentQueryService.GetAgentAsync(tenant, owningAgentId, cancellationToken);
        var owned = agent is not null;
        agentCache[owningAgentId] = owned;

        return owned;
    }

    // A capability (e.g. "Image Classification") is a fixed, canonical
    // concept, distinct from the device/service that actually provides it
    // for this device (its Services list) - see decision-log.md ADR-041.
    // Only capabilities this device actually has get an entry; there's no
    // placeholder row for e.g. "Motion Detection" on a Camera device.
    private async Task<IReadOnlyList<CapabilityDto>> BuildCapabilitiesAsync(
        DeviceOptions device,
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
                    LivenessInterval: null, WarningMultiplier: null)]));
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
                    LivenessInterval: null, WarningMultiplier: null)]));
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
                    LivenessInterval: null, WarningMultiplier: null)]));
        }

        if (device.SinkCleanliness is { } sinkRoi)
        {
            var model = sinkRoi.ExecutingAgentId.Length > 0
                ? (await TryLoadAiClassificationAsync(sinkRoi.ExecutingAgentId, aiAgentCache, cancellationToken))
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
                    LivenessInterval: null, WarningMultiplier: null)]));
        }

        if (device.ObjectDetection is { } detectionRoi)
        {
            var model = detectionRoi.ExecutingAgentId.Length > 0
                ? (await TryLoadAiClassificationAsync(detectionRoi.ExecutingAgentId, aiAgentCache, cancellationToken))
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
                    LivenessInterval: null, WarningMultiplier: null)]));
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
                LivenessInterval: device.LivenessInterval.ToString(), WarningMultiplier: device.WarningMultiplier)]));

        return capabilities;
    }

    private async Task<AiClassificationOptions?> TryLoadAiClassificationAsync(
        string executingAgentId,
        Dictionary<string, AiClassificationOptions?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(executingAgentId, out var cached))
            return cached;

        AiClassificationOptions? result;

        try
        {
            var bytes = await _blobStorage.DownloadAsync(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.BlobName(executingAgentId),
                cancellationToken);

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

    // O(N) blob reads (every device-config blob, downloaded and checked) -
    // fine at this project's actual device count, not worth optimizing for
    // a scale that doesn't exist. Each candidate's own OwningAgentId is
    // checked against the same tenant-ownership helper as the main device -
    // defense in depth against a cross-tenant DeviceId collision, which is
    // practically impossible with GUIDs but free to check since the helper
    // (and its cache) already exists.
    private async Task<IReadOnlyList<TriggeredByDto>> BuildTriggeredByAsync(
        TenantContext tenant,
        string deviceId,
        Dictionary<string, bool> agentCache,
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

        foreach (var blobName in blobNames)
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
