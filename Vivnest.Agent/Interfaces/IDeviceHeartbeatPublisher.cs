using Vivnest.Core.Models.Camera;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;

public interface IDeviceHeartbeatPublisher
{
    Task PublishAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken);

}
