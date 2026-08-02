using Vivnest.Core.Enums;

namespace Vivnest.Core.Options;

public class HomeAssistantEntityOptions
{
    // Mirrors DeviceOptions.Enabled - lets a mapping stay fully configured
    // but inactive, same "toggle, don't delete" pattern as the native side,
    // so a device can move between native and HA-sourced coverage without
    // losing either config.
    public bool Enabled { get; set; } = true;

    public string EntityId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public string EventType { get; set; } = string.Empty;
}
