using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Bridges.TapoHub;

public interface ITapoHubReachabilityChecker
{
    Task<bool> CheckReachabilityAsync(
        DeviceOptions hub,
        CancellationToken cancellationToken = default);
}
