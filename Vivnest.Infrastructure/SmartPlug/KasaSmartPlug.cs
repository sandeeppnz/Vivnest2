using System.Net.Sockets;
using System.Text.Json;
using Vivnest.Core.Options;
using Vivnest.Core.SmartPlug.Models;

namespace Vivnest.Infrastructure.SmartPlug;

public sealed class KasaSmartPlug : Core.SmartPlug.ISmartPlug
{
    private const string InfoCommand =
        """{"system":{"get_sysinfo":{}},"emeter":{"get_realtime":{}}}""";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly DeviceOptions _options;

    public KasaSmartPlug(DeviceOptions options)
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
                9999,
                linkedCts.Token);

            return client.Connected;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<SmartPlugState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var responseJson = await KasaProtocolClient.SendCommandAsync(
            _options.Settings.Host,
            InfoCommand,
            Timeout,
            cancellationToken);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var sysInfo = root.GetProperty("system").GetProperty("get_sysinfo");

        double? watts = null, volts = null, amps = null, totalKwh = null;

        if (root.TryGetProperty("emeter", out var emeter) &&
            emeter.TryGetProperty("get_realtime", out var realtime) &&
            (!realtime.TryGetProperty("err_code", out var errCode) || errCode.GetInt32() == 0))
        {
            // Kasa reports these in milli-units (mV/mA/mW).
            if (realtime.TryGetProperty("voltage_mv", out var v))
                volts = v.GetDouble() / 1000.0;

            if (realtime.TryGetProperty("current_ma", out var a))
                amps = a.GetDouble() / 1000.0;

            if (realtime.TryGetProperty("power_mw", out var w))
                watts = w.GetDouble() / 1000.0;

            if (realtime.TryGetProperty("total_wh", out var t))
                totalKwh = t.GetDouble() / 1000.0;
        }

        return new SmartPlugState
        {
            IsOn = sysInfo.TryGetProperty("relay_state", out var relayState) && relayState.GetInt32() == 1,
            CurrentConsumptionWatts = watts,
            VoltageVolts = volts,
            CurrentAmps = amps,
            ConsumptionTotalKwh = totalKwh,
            Brand = "TP-Link",
            Model = GetStringOrNull(sysInfo, "model"),
            FirmwareVersion = GetStringOrNull(sysInfo, "sw_ver"),
            HardwareVersion = GetStringOrNull(sysInfo, "hw_ver"),
            MacAddress = GetStringOrNull(sysInfo, "mac"),
        };
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }
}
