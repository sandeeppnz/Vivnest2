using Vivnest.Core.Models;

namespace Vivnest.Core.Interfaces;

public interface IHeartbeatStore
{
    Task SaveAsync(
        Heartbeat heartbeat,
        CancellationToken cancellationToken = default);
}
