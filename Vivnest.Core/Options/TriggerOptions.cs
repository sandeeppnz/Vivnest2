namespace Vivnest.Core.Options;

/// <summary>
/// Other devices this device triggers when it fires an event worth
/// reacting to (currently: a motion sensor going Detected). Resolved by
/// MotionTriggerResolverHandler, empty for devices that don't trigger
/// anything.
/// </summary>
public sealed class TriggerOptions
{
    public string[] DeviceIds { get; init; } = [];
}
