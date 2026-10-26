using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Events;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Shared;

namespace Vivnest.Capabilities.Audio;

// Persists the detection as a DeviceEvent and queues it for Cloud, which
// decides whether and how to notify - the same path MotionSensor uses
// (workers never persist directly; queue messages carry only keys).
public sealed class AudioAlarmDetectedHandler : IEventHandler<AudioAlarmDetectedEvent>
{
    private readonly ILogger<AudioAlarmDetectedHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;

    public AudioAlarmDetectedHandler(
        ILogger<AudioAlarmDetectedHandler> logger,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        AudioAlarmDetectedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var deviceEvent = new DeviceEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                DeviceId = @event.DeviceId,
                DeviceType = DeviceType.Camera,
                EventType = DeviceEventTypes.FireAlarmDetected,
                Severity = EventSeverity.Critical,
                OccurredAtUtc = @event.DetectedAtUtc,
                Data = new
                {
                    @event.FrequencyHz,
                    @event.BurstCount,
                    LongestToneSeconds = @event.LongestTone.TotalSeconds,
                },
            };

            var entity = await _deviceEventWriter.SaveAsync(
                deviceEvent,
                cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist DeviceEvent for {DeviceId}",
                    @event.DeviceId);

                return;
            }

            await _queuePublisher.PublishAsync(
                _messagingOptions.DeviceEventQueue,
                new DeviceEventQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);

            _logger.LogInformation(
                "Fire alarm detection queued for cloud processing for {DeviceId}.",
                @event.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist fire alarm detection for {DeviceId}",
                @event.DeviceId);

            throw;
        }
    }
}
