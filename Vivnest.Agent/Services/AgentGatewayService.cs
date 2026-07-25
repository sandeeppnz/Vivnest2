using Microsoft.Extensions.Logging;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;

namespace Vivnest.Agent.Services;

public sealed class AgentGatewayService : IAgentGateway
{
    private readonly IHeartbeatRepository _heartbeatRepository;
    private readonly ILogger<AgentGatewayService> _logger;

    public AgentGatewayService(ILogger<AgentGatewayService> logger, IHeartbeatRepository heartbeatRepository)
    {
        _logger = logger;
        _heartbeatRepository = heartbeatRepository;
    }

    public Task PublishEventAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            """
                    Device Event Published
                    DeviceId: {DeviceId}
                    DeviceType: {DeviceType}
                    EventType: {EventType}
                    Severity: {Severity}
                    Timestamp: {Timestamp}
                    """,
            deviceEvent.DeviceId,
            deviceEvent.DeviceType,
            deviceEvent.EventType,
            deviceEvent.Severity,
            deviceEvent.Timestamp);

        return Task.CompletedTask;

    }

    public Task PublishHeartbeatAsync(
        Heartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        return _heartbeatRepository.SaveAsync(
            heartbeat,
            cancellationToken);
    }

}

