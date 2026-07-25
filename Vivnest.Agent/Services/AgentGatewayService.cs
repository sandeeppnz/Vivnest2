using Microsoft.Extensions.Logging;
using System.Text.Json;
using Vivnest.Core.Entities;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;

namespace Vivnest.Agent.Services;

public sealed class AgentGatewayService : IAgentGateway
{
    private readonly IHeartbeatStore _heartbeatStore;
    private readonly IDeviceEventStore _deviceEventStore;

    private readonly IQueuePublisher _queuePublisher;

    private readonly ILogger<AgentGatewayService> _logger;

    public AgentGatewayService(ILogger<AgentGatewayService> logger, 
        IHeartbeatStore heartbeatRepository,
        IQueuePublisher queuePublisher,
        IDeviceEventStore deviceEventStore)
    {
        _logger = logger;
        _heartbeatStore = heartbeatRepository;
        _queuePublisher = queuePublisher;
        _deviceEventStore = deviceEventStore;
    }

    public Task PublishEventAsync(CameraCapturedMessage message, CancellationToken cancellationToken = default)
    {
        return _queuePublisher.PublishAsync<CameraCapturedMessage>(
                   Core.Constants.QueueNames.CameraCaptured,
                   message,
                   cancellationToken);
    }

    public Task<DeviceEventEntity?> SaveEventAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {

        return _deviceEventStore.SaveAsync(
            deviceEvent,
            cancellationToken);
    }

    public Task SaveHeartbeatAsync(
        Heartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        return _heartbeatStore.SaveAsync(
            heartbeat,
            cancellationToken);
    }

}

