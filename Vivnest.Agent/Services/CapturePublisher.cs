using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models;
using Vivnest.Core.Models.Camera;
using Vivnest.Core.Models.Heartbeats;
using Vivnest.Core.Options;
using Vivnest.Core.Options.Heartbeats;

namespace Vivnest.Agent.Services;

public sealed class CapturePublisher(
  ILogger<DeviceHeartbeatPublisher> logger,
  IAgentHeartbeatStore agentHeartbeatStore,
  IDeviceHeartbeatStore deviceHeartbeatStore,
  IDeviceEventStore deviceEventStore,
  IOptions<AgentOptions> options,
  IOptions<DeviceHeartbeatOptions> deviceHeartbeatOptions,
  IQueuePublisher queuePublisher) : ICapturePublisher
{
    private readonly IAgentHeartbeatStore _agentHeartbeatStore = agentHeartbeatStore;
    private readonly IDeviceHeartbeatStore _deviceHeartbeatStore = deviceHeartbeatStore;
    private readonly IDeviceEventStore _deviceEventStore = deviceEventStore;
    private readonly IQueuePublisher _queuePublisher = queuePublisher;

    private readonly ILogger<DeviceHeartbeatPublisher> _logger = logger;

    public async Task PublishAsync(CaptureResult capture, CancellationToken cancellationToken = default)
    {
        var deviceEvent = new DeviceEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = options.Value.AgentId,
            TenantId = options.Value.TenantId,
            SiteId = options.Value.SiteId,
            DeviceId = capture.DeviceId,
            DeviceType = DeviceType.Camera,
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

        //await _deviceHeartbeatStore.SaveAsync(
        //    new DeviceHeartbeat
        //    {
        //        AgentId = options.Value.AgentId,
        //        TenantId = options.Value.TenantId,
        //        SiteId = options.Value.SiteId,
        //        DeviceId = capture.DeviceId,
        //        DeviceType = DeviceType.Camera,
        //        Status = DeviceHeartbeatStatus.Online,
        //        LastHeartbeatUtc = DateTime.UtcNow,
        //        LastActivityUtc = capture.CapturedAtUtc,
        //        ExpectedActivityInterval = capture.CaptureInterval,
        //        ExpectedHeartbeatInterval = deviceHeartbeatOptions.Value.HeartbeatInterval,
        //        Error = null
        //    },
        //    cancellationToken);

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
}
