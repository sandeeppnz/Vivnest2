using Microsoft.Extensions.Logging;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Infrastructure.Stores.Heartbeats;

namespace Vivnest.Agent.Publishers;

public sealed class DeviceHeartbeatPublisher(
    ILogger<DeviceHeartbeatPublisher> logger,
    IDeviceHeartbeatStore deviceHeartbeatStore) : IDeviceHeartbeatPublisher
{
    private readonly IDeviceHeartbeatStore _deviceHeartbeatStore = deviceHeartbeatStore;

    private readonly ILogger<DeviceHeartbeatPublisher> _logger = logger;

    public Task PublishAsync(
    DeviceHeartbeat heartbeat,
    CancellationToken cancellationToken = default)
    {
        return _deviceHeartbeatStore.SaveAsync(
            heartbeat,
            cancellationToken);
    }
}