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
}
