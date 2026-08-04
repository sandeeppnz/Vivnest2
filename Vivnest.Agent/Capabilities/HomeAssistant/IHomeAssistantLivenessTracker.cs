using Vivnest.Core.Enums;

namespace Vivnest.Agent.Capabilities.HomeAssistant;

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
