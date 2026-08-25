using Vivnest.Core.Options;

namespace Vivnest.Core.Devices.Hubs;

public interface ITapoHubReachabilityChecker
{
    Task<bool> CheckReachabilityAsync(
        DeviceOptions hub,
        CancellationToken cancellationToken = default);
}
