namespace Vivnest.Core.Heartbeat;

public interface IHeartbeatService
{
    Task SendAsync(
        Heartbeat heartbeat,
        CancellationToken cancellationToken = default);
}