using Vivnest.Abstractions.Enums;

namespace Vivnest.Agent.Capabilities.Bridges.HomeAssistant;

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
