using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.SmartPlug;

// No queue publish here, unlike CameraCaptureHandler - a routine power
// reading needs no Cloud-side processing (no Telegram alert, no blob
// download); persisting it is enough for the dashboard's event feed.
public class SmartPlugReadingHandler : IEventHandler<SmartPlugReadingCompletedEvent>
{
    private readonly ILogger<SmartPlugReadingHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;

    public SmartPlugReadingHandler(
        ILogger<SmartPlugReadingHandler> logger,
        IOptions<AgentOptions> agentOptions,
        IDeviceEventWriter deviceEventWriter)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _deviceEventWriter = deviceEventWriter;
    }

    public async Task HandleAsync(
        SmartPlugReadingCompletedEvent @event,
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
                DeviceType = DeviceType.SmartPlug,
                EventType = DeviceEventTypes.PowerReading,
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
                    "Unable to persist DeviceEvent for {DeviceId}",
                    reading.DeviceId);

                return;
            }

            _logger.LogInformation(
                "Power reading persisted for {DeviceId}",
                reading.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist power reading for {DeviceId}",
                @event.Result.DeviceId);

            throw;
        }
    }
}
