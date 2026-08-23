using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Shared;

namespace Vivnest.Capabilities.Camera;

public sealed class CameraCaptureFailedHandler
    : IEventHandler<CameraCaptureFailedEvent>
{
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;
    private readonly MessagingOptions _messagingOptions;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<CameraCaptureFailedHandler> _logger;

    public CameraCaptureFailedHandler(
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<AgentOptions> agentOptions,
        ILogger<CameraCaptureFailedHandler> logger)
    {
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
        _messagingOptions = messagingOptions.Value;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(
        CameraCaptureFailedEvent @event,
        CancellationToken cancellationToken = default)
    {
        var failure = @event.Failure;

        var deviceEvent = new DeviceEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _agentOptions.AgentId,
            TenantId = _agentOptions.TenantId,
            SiteId = _agentOptions.SiteId,
            DeviceId = failure.DeviceId,
            DeviceType = DeviceType.Camera,
            EventType = DeviceEventTypes.CameraCaptureFailed,
            Severity = EventSeverity.Critical,
            OccurredAtUtc = failure.TimestampUtc,

            // An object, not JsonSerializer.Serialize(...). Data is
            // object? and AzureTableDeviceEventWriter serialises it, so a
            // string here produced a JSON string CONTAINING JSON - unlike
            // every other event type, which stores a real object. Nothing
            // parsed it, so nothing broke; the first consumer to try
            // JsonDocument.Parse(payload).GetProperty("ErrorCode") - the
            // pattern DeviceEventQueueHandler already uses elsewhere -
            // would have. Rows written before 2026-08-24 still carry the
            // double-encoded shape.
            Data = new
            {
                failure.ErrorCode,
                failure.ExceptionMessage
            }
        };

        var entity = await _deviceEventWriter.SaveAsync(
            deviceEvent,
            cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning(
                "Unable to persist DeviceEvent for {DeviceId}",
                failure.DeviceId);

            return;
        }

        _logger.LogInformation(
            "Camera capture failure persisted for device {DeviceId}.",
            failure.DeviceId);

        var message = new CameraCapturedFailedQueueMessage
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey
        };

        await _queuePublisher.PublishAsync(
            _messagingOptions.DeviceEventQueue,
            message,
            cancellationToken);

        _logger.LogInformation(
            "Camera capture failure queued for cloud processing.");
    }
}
