using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Capabilities.Camera;

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

            Data = JsonSerializer.Serialize(new
            {
                failure.ErrorCode,
                failure.ExceptionMessage
            })
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
