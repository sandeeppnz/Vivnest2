using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Core.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

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
            "Motion sensor reading failure persisted for device {DeviceId}.",
            failure.DeviceId);
    }
}
