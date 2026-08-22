using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Abstraction.Agent.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.MotionSensor;

// No queue publish here, same reasoning as SmartPlugReadingHandler - a
// routine battery/signal reading needs no Cloud-side processing; persisting
// it is enough for the dashboard's battery history.
public class MotionSensorBatteryHandler : IEventHandler<MotionSensorBatteryReportedEvent>
{
    private readonly ILogger<MotionSensorBatteryHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;

    public MotionSensorBatteryHandler(
        ILogger<MotionSensorBatteryHandler> logger,
        IOptions<AgentOptions> agentOptions,
        IDeviceEventWriter deviceEventWriter)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _deviceEventWriter = deviceEventWriter;
    }

    public async Task HandleAsync(
        MotionSensorBatteryReportedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var reading = @event.Result;

            var deviceEvent = new DeviceEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                DeviceId = reading.DeviceId,
                DeviceType = DeviceType.MotionSensor,
                EventType = DeviceEventTypes.BatteryStatus,
                Severity = EventSeverity.Information,
                OccurredAtUtc = reading.ReadAtUtc,
                Data = reading.State,
            };

            var entity = await _deviceEventWriter.SaveAsync(
                deviceEvent,
                cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist battery status DeviceEvent for {DeviceId}",
                    reading.DeviceId);

                return;
            }

            _logger.LogInformation(
                "Battery status persisted for {DeviceId}",
                reading.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist battery status for {DeviceId}",
                @event.Result.DeviceId);

            throw;
        }
    }
}
