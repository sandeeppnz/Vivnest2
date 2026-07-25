using Vivnest.Core.Models;

namespace Vivnest.Agent.Services;

public interface IAgentGateway
{
    Task PublishHeartbeatAsync(
            Heartbeat heartbeat,
            CancellationToken cancellationToken = default);

    Task PublishEventAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);

    //Task<AgentConfiguration?> GetConfigurationAsync(
    //CancellationToken cancellationToken = default);

    //Task<UpdateResponse?> CheckForUpdatesAsync(
    //    CancellationToken cancellationToken = default);
}