namespace Vivnest.Cloud.Interfaces;

public interface IHealthMonitorService
{
    // Full sweep of every device/agent — the only way to catch an agent
    // that has gone silent (no queue message will ever arrive for that).
    Task RunAsync(CancellationToken cancellationToken = default);

    // Re-evaluate a single device, triggered by its DeviceHeartbeatQueue
    // message — near-instant reaction to an explicit status change.
    Task ProcessDeviceAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    // Re-evaluate a single agent, triggered by its AgentHeartbeatQueue
    // message. An agent can only ever report itself alive, so this is
    // effectively recovery-only in practice — going offline is silence,
    // which only RunAsync's periodic sweep can detect.
    Task ProcessAgentAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);
}
