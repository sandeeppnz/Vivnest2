using Microsoft.Extensions.Logging;
using Vivnest.Agent.Capabilities.MotionSensor;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Triggers;

// Second handler on MotionSensorStateChangedEvent (multicast dispatch
// already supports N handlers per event - MotionSensorStateChangedHandler,
// which persists the DeviceEvent/notification, is unaffected by this one
// existing alongside it). This one's only job is "who does this motion
// sensor trigger" - it doesn't know or care what a triggered device does
// about it, that's DeviceTriggeredEvent's subscribers' job.
public sealed class MotionTriggerResolverHandler : IEventHandler<MotionSensorStateChangedEvent>
{
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly IEventDispatcher _dispatcher;
    private readonly ILogger<MotionTriggerResolverHandler> _logger;

    public MotionTriggerResolverHandler(
        IDeviceRuntimeStore deviceRegistry,
        IEventDispatcher dispatcher,
        ILogger<MotionTriggerResolverHandler> logger)
    {
        _deviceRegistry = deviceRegistry;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task HandleAsync(
        MotionSensorStateChangedEvent @event,
        CancellationToken cancellationToken)
    {
        // Only motion starting is worth triggering anything - clearing
        // isn't an event other devices should react to.
        if (!@event.Detected)
            return;

        DeviceOptions motionSensor;

        try
        {
            motionSensor = _deviceRegistry.GetDevice(@event.DeviceId);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        foreach (var targetDeviceId in motionSensor.TriggersDeviceIds)
        {
            DeviceOptions target;

            try
            {
                target = _deviceRegistry.GetDevice(targetDeviceId);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning(
                    "Motion sensor {DeviceId} is configured to trigger unknown device {TargetDeviceId}.",
                    @event.DeviceId,
                    targetDeviceId);

                continue;
            }

            await _dispatcher.PublishAsync(
                new DeviceTriggeredEvent(
                    target.DeviceId,
                    target.Type,
                    $"Motion:{@event.DeviceId}",
                    @event.ChangedAtUtc),
                cancellationToken);
        }
    }
}
