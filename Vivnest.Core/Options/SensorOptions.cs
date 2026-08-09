namespace Vivnest.Core.Options;

/// <summary>
/// A single piece of hardware present on a device - hand-authored, not
/// implied by <see cref="DeviceType"/> (a camera's PIR being inaccessible
/// is a firmware fact about that specific device/model, not true of every
/// camera). Presence is implied by inclusion in
/// <see cref="DeviceOptions.Sensors"/> - a device's list only contains
/// hardware that's actually there, nothing gets an "absent" entry
/// (decision-log.md ADR-040).
/// </summary>
public sealed class SensorOptions
{
    public string Name { get; init; } = "";

    public bool Accessible { get; init; } = true;

    public string? InaccessibleReason { get; init; }
}
