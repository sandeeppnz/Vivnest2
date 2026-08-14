using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

public sealed class DeviceConfigurationProjector : IDeviceConfigurationProjector
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private readonly IDeviceRegistryStore _devices;
    private readonly IDeviceTypeStore _deviceTypes;
    private readonly IAgentRegistryStore _agentRegistry;

    public DeviceConfigurationProjector(
        IDeviceRegistryStore devices,
        IDeviceTypeStore deviceTypes,
        IAgentRegistryStore agentRegistry)
    {
        _devices = devices;
        _deviceTypes = deviceTypes;
        _agentRegistry = agentRegistry;
    }

    public async Task<ProjectedDeviceConfigDto?> ProjectAsync(
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

        return new ProjectedDeviceConfigDto(
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
            warnings);
    }

    // "Motion Sensor" (admin-typed, space-separated) -> DeviceType.MotionSensor -
    // matched case/whitespace-insensitively since the Admin DeviceType master
    // list is free text, not the fixed runtime enum.
    private static string? MatchRuntimeDeviceType(string deviceTypeName)
    {
        var normalized = deviceTypeName.Replace(" ", "");

        return Enum.GetNames<DeviceType>()
            .FirstOrDefault(n => string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return EmptySettings;

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
