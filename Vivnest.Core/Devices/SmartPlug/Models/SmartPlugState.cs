namespace Vivnest.Core.Devices.SmartPlug.Models;

public sealed class SmartPlugState
{
    public bool IsOn { get; init; }

    public double? CurrentConsumptionWatts { get; init; }
    public double? VoltageVolts { get; init; }
    public double? CurrentAmps { get; init; }
    public double? ConsumptionTotalKwh { get; init; }

    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? HardwareVersion { get; init; }
    public string? MacAddress { get; init; }
}
