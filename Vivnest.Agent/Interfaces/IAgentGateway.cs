using Vivnest.Core.Models.Camera;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;

public interface IAgentGateway
{
    Task PublishCaptureAsync(
        CaptureResult capture,
        AgentOptions agent,
        DeviceHeartbeatOptions deviceHeartbeatOptions,
        CancellationToken cancellationToken = default);

    //Task UpdateDeviceFailureAsync(
    //    AgentOptions agentOptions,
    //    string deviceId,
    //    string error,
    //    CancellationToken cancellationToken = default);

    Task PublishAgentHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task PublishDeviceHeartbeatAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken);

}