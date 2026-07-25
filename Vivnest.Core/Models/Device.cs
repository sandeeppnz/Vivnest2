using Vivnest.Core.Enums;

namespace Vivnest.Core.Models;

public sealed class Device
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DeviceType Type { get; init; }
    public string? Location { get; init; }
}
