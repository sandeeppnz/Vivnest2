using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin.CapabilityProjection;

// Mirrors ImageCaptureRuntimeProjector's flat-field, device-local shape
// (decision-log.md ADR-067) - "Motion Detection" is a Built-in capability
// with a real runtime consumer (MotionSensorMonitorWorker/Service,
// Vivnest.Agent/Capabilities/MotionSensor/), driving standalone
// DeviceType.MotionSensor devices exclusively via the same root
// LivenessInterval/WarningMultiplier/Schedule.Interval fields
// ImageCaptureRuntimeProjector already claims for Camera devices.
//
// Real device-type gate, not a convention: a device that isn't actually a
// Motion Sensor (e.g. the real Kitchen Camera, which has both Image
// Capture and Motion Detection assigned) would silently collide with
// Image Capture's own claim on those same fields if this projector wrote
// them unconditionally - and gets no real distinct behavior from Motion
// Detection anyway, since no video-based motion logic exists in
// CameraCaptureWorker. Confirmed with the user: gate by device type
// instead of guessing a field-merge policy.
public sealed class MotionDetectionRuntimeProjector : ICapabilityRuntimeProjector
{
    public string CapabilityName => "Motion Detection";

    private const string LivenessIntervalMinutesKey = "LivenessIntervalMinutes";
    private const string WarningMultiplierKey = "WarningMultiplier";
    private const string BatteryReportIntervalMinutesKey = "BatteryReportIntervalMinutes";

    private static readonly string[] RequiredKeys =
    [
        LivenessIntervalMinutesKey,
        WarningMultiplierKey
    ];

    private readonly IDeviceTypeStore _deviceTypes;

    public MotionDetectionRuntimeProjector(IDeviceTypeStore deviceTypes)
    {
        _deviceTypes = deviceTypes;
    }

    public CapabilityProjectionResult Project(
        DeviceCapabilityEntity assignment,
        DeviceRegistryEntity device,
        string? executingRuntimeAgentId)
    {
        var warnings = new List<string>();

        // ICapabilityRuntimeProjector.Project is deliberately synchronous
        // (every implementation runs inline inside a foreach, no
        // IAsyncEnumerable machinery exists in either caller) - blocking
        // here on one small, rarely-changing DeviceType master-row lookup
        // is safe under the Isolated Worker host (no captured
        // SynchronizationContext to deadlock against) and confines this
        // one capability's device-type need to this file alone, rather
        // than threading a resolved type string through the shared
        // interface and both of its callers for a single consumer.
        var resolvedType = ResolveRuntimeDeviceType(device.DeviceTypeId);

        if (resolvedType != DeviceType.MotionSensor)
        {
            warnings.Add(
                $"Motion Detection requires a Motion Sensor device, but this device is a " +
                $"{(resolvedType?.ToString() ?? "device of an unrecognized type")}.");

            return new CapabilityProjectionResult(null, null, warnings);
        }

        var settings = new Dictionary<string, string>();
        var assignedSettings = ParseSettings(assignment.Settings);

        foreach (var key in RequiredKeys)
        {
            if (!assignedSettings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                warnings.Add($"Motion Detection: \"{key}\" is not set.");
                continue;
            }

            if (!double.TryParse(value, out _))
            {
                warnings.Add($"Motion Detection: \"{key}\" value \"{value}\" is not a number.");
                continue;
            }

            settings[key] = value;
        }

        // Optional (ADR-067) - mirrors MotionSensorMonitorWorker.ReadAsync's
        // own tolerance for an unset Schedule.Interval (falls back to a
        // 2-hour battery-report cadence) - omitting it keeps today's
        // behavior unchanged rather than forcing every assignment to pick
        // a value.
        if (assignedSettings.TryGetValue(BatteryReportIntervalMinutesKey, out var batteryReportInterval)
            && !string.IsNullOrWhiteSpace(batteryReportInterval))
        {
            if (double.TryParse(batteryReportInterval, out _))
            {
                settings[BatteryReportIntervalMinutesKey] = batteryReportInterval;
            }
            else
            {
                warnings.Add(
                    $"Motion Detection: \"{BatteryReportIntervalMinutesKey}\" value \"{batteryReportInterval}\" is not a number.");
            }
        }

        if (warnings.Count > 0)
            return new CapabilityProjectionResult(null, null, warnings);

        var deviceEntry = new CapabilityDocumentEntryDto(
            assignment.CapabilityId,
            CapabilityName,
            assignment.Enabled,
            executingRuntimeAgentId,
            settings);

        return new CapabilityProjectionResult(deviceEntry, null, warnings);
    }

    // Same admin-typed-free-text-vs-runtime-enum match
    // DeviceRuntimeConfigurationProjector.MatchRuntimeDeviceType already
    // uses - duplicated rather than shared, since that method is private
    // to a different class and this is this capability's only consumer.
    private DeviceType? ResolveRuntimeDeviceType(string deviceTypeId)
    {
        if (string.IsNullOrWhiteSpace(deviceTypeId))
            return null;

        var deviceType = _deviceTypes.GetAsync(deviceTypeId).GetAwaiter().GetResult();

        if (deviceType == null)
            return null;

        var normalized = deviceType.DeviceTypeName.Replace(" ", "");

        return Enum.GetNames<DeviceType>()
            .Where(n => string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(n => (DeviceType?)Enum.Parse<DeviceType>(n))
            .FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
