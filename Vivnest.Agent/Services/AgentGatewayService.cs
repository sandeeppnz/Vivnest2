using Microsoft.Extensions.Logging;
using Vivnest.Core.Constants;
using Vivnest.Core.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models;
using Vivnest.Core.Models.Camera;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Services;

public sealed class AgentGatewayService : IAgentGateway
{
    private readonly IAgentHeartbeatStore _agentHeartbeatStore;
    private readonly IDeviceHeartbeatStore _deviceHeartbeatStore;
    private readonly IDeviceEventStore _deviceEventStore;
    private readonly IQueuePublisher _queuePublisher;

    private readonly ILogger<AgentGatewayService> _logger;

    public AgentGatewayService(
        ILogger<AgentGatewayService> logger,
        IAgentHeartbeatStore agentHeartbeatStore,
        IDeviceHeartbeatStore deviceHeartbeatStore,
        IDeviceEventStore deviceEventStore,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _agentHeartbeatStore = agentHeartbeatStore;
        _deviceHeartbeatStore = deviceHeartbeatStore;
        _deviceEventStore = deviceEventStore;
        _queuePublisher = queuePublisher;
    }

    public async Task PublishCaptureAsync(
        CaptureResult capture,
        AgentOptions agent,
        CancellationToken cancellationToken = default)
    {
        var deviceEvent = new DeviceEvent
        {
            Id = Guid.NewGuid(),
            AgentId = agent.AgentId,
            DeviceId = capture.DeviceId,
            DeviceType = DeviceType.Camera,
            FirmwareVersion = agent.FirmwareVersion,
            EventType = EventTypes.CameraCaptured,
            Severity = EventSeverity.Information,
            OccurredAtUtc = capture.CapturedAtUtc,

            Data = new CameraCapturedData
            {
                BlobName = capture.BlobName!,
                BlobContainer = capture.BlobContainer!,
                CapturedAt = capture.CapturedAtUtc,
                CaptureDuration = capture.CaptureDuration,
                UploadDuration = capture.UploadDuration
            }
        };

        DeviceEventEntity? entity =
            await _deviceEventStore.SaveAsync(
                deviceEvent,
                cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning(
                "Unable to persist DeviceEvent for {DeviceId}",
                capture.DeviceId);

            return;
        }

        await _deviceHeartbeatStore.SaveAsync(
            new DeviceHeartbeat
            {
                AgentId = agent.AgentId,
                DeviceId = capture.DeviceId,
                DeviceType = DeviceType.Camera,
                Status = DeviceHeartbeatStatus.Online,
                LastHeartbeatUtc = DateTime.UtcNow,
                LastActivityUtc = capture.CapturedAtUtc,
                AgentFirmwareVersion = agent.FirmwareVersion,
                ExpectedActivityInterval = capture.CaptureInterval,
                Error = null
            },
            cancellationToken);

        await _queuePublisher.PublishAsync(
            QueueNames.CameraCaptured,
            new CameraCapturedMessage
            {
                PartitionKey = entity.PartitionKey,
                RowKey = entity.RowKey
            },
            cancellationToken);

        _logger.LogInformation(
            "Published CameraCaptured event for {DeviceId}.",
            capture.DeviceId);
    }

    public async Task UpdateDeviceFailureAsync(
        string agentId,
        string deviceId,
        string error,
        CancellationToken cancellationToken = default)
    {
        var heartbeat =
            await _deviceHeartbeatStore.GetAsync(
                agentId,
                deviceId,
                cancellationToken);

        heartbeat ??= new DeviceHeartbeat
        {
            AgentId = agentId,
            DeviceId = deviceId,
            DeviceType = DeviceType.Camera,
            Status = DeviceHeartbeatStatus.Error,
            Error = error,
            LastHeartbeatUtc = DateTime.UtcNow,
            // Leave LastActivityUtc unchanged
        };


        await _deviceHeartbeatStore.SaveAsync(
            heartbeat,
            cancellationToken);
    }

    public Task SaveAgentHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        return _agentHeartbeatStore.SaveAsync(
            heartbeat,
            cancellationToken);
    }
}