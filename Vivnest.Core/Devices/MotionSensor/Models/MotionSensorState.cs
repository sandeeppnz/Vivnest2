namespace Vivnest.Core.Devices.MotionSensor.Models;

public sealed class MotionSensorState
{
    public bool Detected { get; init; }

    public bool? BatteryLow { get; init; }
    public int? SignalLevel { get; init; }

    public string? Model { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? HardwareVersion { get; init; }
    public string? MacAddress { get; init; }
}
