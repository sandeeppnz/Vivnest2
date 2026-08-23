using Vivnest.Core.Hubs;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Tapo;

// Talks to the hub directly (TapoKlapClient.SendAsync, not
// SendChildRequestAsync) - a handshake plus one get_device_info call is
// enough to confirm the hub itself is up, without going through the
// control_child envelope any specific child sensor read would need.
public sealed class TapoHubReachabilityChecker : ITapoHubReachabilityChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<bool> CheckReachabilityAsync(
        DeviceOptions hub,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TapoKlapClient(
                hub.Settings.Host,
                hub.Settings.Username,
                hub.Settings.Password,
                Timeout);

            await client.SendAsync("get_device_info", null, cancellationToken);

            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
