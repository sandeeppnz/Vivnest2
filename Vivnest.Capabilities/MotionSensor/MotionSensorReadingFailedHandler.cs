using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Shared;

namespace Vivnest.Capabilities.MotionSensor;

public sealed class MotionSensorReadingFailedHandler
    : IEventHandler<MotionSensorReadingFailedEvent>
{
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<MotionSensorReadingFailedHandler> _logger;

    public MotionSensorReadingFailedHandler(
        IDeviceEventWriter deviceEventWriter,
        IOptions<AgentOptions> agentOptions,
        ILogger<MotionSensorReadingFailedHandler> logger)
    {
        _deviceEventWriter = deviceEventWriter;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(
        MotionSensorReadingFailedEvent @event,
        CancellationToken cancellationToken = default)
    {
        var failure = @event.Failure;

        var deviceEvent = new DeviceEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = failure.AgentId,
            TenantId = _agentOptions.TenantId,
            SiteId = _agentOptions.SiteId,
            DeviceId = failure.DeviceId,
            DeviceType = DeviceType.MotionSensor,
            EventType = DeviceEventTypes.MotionSensorReadingFailed,
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
            "Motion sensor reading failure persisted for device {DeviceId}.",
            failure.DeviceId);
    }
}
