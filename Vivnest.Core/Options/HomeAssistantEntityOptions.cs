using Vivnest.Core.Enums;

namespace Vivnest.Core.Options;

public class HomeAssistantEntityOptions
{
    public string EntityId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public string EventType { get; set; } = string.Empty;
}
