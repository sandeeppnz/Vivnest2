using System.Net.Sockets;
using System.Text.Json;
using Vivnest.Core.Devices.MotionSensor.Models;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.Tapo;

namespace Vivnest.Infrastructure.MotionSensor;

public sealed class TapoMotionSensor : Core.Devices.MotionSensor.IMotionSensor
{
    private const int HubPort = 80;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly DeviceOptions _options;

    public TapoMotionSensor(DeviceOptions options)
    {
        _options = options;
    }

    public async Task<bool> IsReachableAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(Timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            await client.ConnectAsync(
                _options.Settings.Host,
                HubPort,
                linkedCts.Token);

            return client.Connected;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<MotionSensorState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = new TapoKlapClient(
            _options.Settings.Host,
            _options.Settings.Username,
            _options.Settings.Password,
            Timeout);

        var info = await client.SendChildRequestAsync(
            _options.Settings.ChildDeviceId,
            "get_device_info",
            null,
            cancellationToken);

        return new MotionSensorState
        {
            Detected = info.TryGetProperty("detected", out var detected) && detected.GetBoolean(),
            BatteryLow = GetBoolOrNull(info, "at_low_battery"),
            SignalLevel = GetIntOrNull(info, "signal_level"),
            Model = GetStringOrNull(info, "model"),
            FirmwareVersion = GetStringOrNull(info, "fw_ver"),
            HardwareVersion = GetStringOrNull(info, "hw_ver"),
            MacAddress = GetStringOrNull(info, "mac"),
        };
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.GetBoolean()
            : null;
    }

    private static int? GetIntOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.GetInt32()
            : null;
    }
}
