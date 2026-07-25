using Vivnest.Core.Models;

namespace Vivnest.Core.Interfaces;

public interface IHeartbeatRepository
{
    Task SaveAsync(
        Heartbeat heartbeat,
        CancellationToken cancellationToken = default);
}