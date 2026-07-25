using Vivnest.Core.Entities;
using Vivnest.Core.Models;

namespace Vivnest.Agent.Services;

public interface IAgentGateway
{
    Task SaveHeartbeatAsync(
            Heartbeat heartbeat,
            CancellationToken cancellationToken = default);

    Task<DeviceEventEntity?> SaveEventAsync(
        DeviceEvent deviceEvent,
        CancellationToken cancellationToken = default);


    Task PublishEventAsync(
        CameraCapturedMessage message,
        CancellationToken cancellationToken = default);


    //Task<AgentConfiguration?> GetConfigurationAsync(
    //CancellationToken cancellationToken = default);

    //Task<UpdateResponse?> CheckForUpdatesAsync(
    //    CancellationToken cancellationToken = default);
}