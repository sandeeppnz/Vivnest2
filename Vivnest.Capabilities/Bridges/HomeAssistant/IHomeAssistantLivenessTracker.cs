using Vivnest.Core.Enums;

namespace Vivnest.Capabilities.Bridges.HomeAssistant;

public interface IHomeAssistantLivenessTracker
{
    Task ReportAsync(
        string deviceId,
        DeviceType deviceType,
        string entityId,
        string? state,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default);
}
