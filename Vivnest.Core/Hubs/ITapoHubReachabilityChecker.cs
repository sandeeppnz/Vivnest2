using Vivnest.Core.Options;

namespace Vivnest.Core.Hubs;

public interface ITapoHubReachabilityChecker
{
    Task<bool> CheckReachabilityAsync(
        DeviceOptions hub,
        CancellationToken cancellationToken = default);
}
