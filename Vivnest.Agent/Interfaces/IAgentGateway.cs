using Vivnest.Core.Models.Camera;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;

public interface IAgentGateway
{
    Task PublishCaptureAsync(
        CaptureResult capture,
        AgentOptions agent,
        CancellationToken cancellationToken = default);

    Task UpdateDeviceFailureAsync(
        AgentOptions agentOptions,
        string deviceId,
        string error,
        CancellationToken cancellationToken = default);

    Task SaveAgentHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);
}