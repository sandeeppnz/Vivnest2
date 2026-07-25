using Microsoft.Extensions.Logging;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;

namespace Vivnest.Agent.Services;

public sealed class AgentGatewayService : IAgentGateway
{
    private readonly IHeartbeatStore _heartbeatStore;
    private readonly IDeviceEventStore _deviceEventStore;
    private readonly ILogger<AgentGatewayService> _logger;

    public AgentGatewayService(ILogger<AgentGatewayService> logger, 
        IHeartbeatStore heartbeatRepository,
        IDeviceEventStore deviceEventStore)
    {
        _logger = logger;
        _heartbeatStore = heartbeatRepository;
        _deviceEventStore = deviceEventStore;
    }

    public Task PublishEventAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {

        return _deviceEventStore.SaveAsync(
            deviceEvent,
            cancellationToken);
    }

    public Task PublishHeartbeatAsync(
        Heartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        return _heartbeatStore.SaveAsync(
            heartbeat,
            cancellationToken);
    }

}

