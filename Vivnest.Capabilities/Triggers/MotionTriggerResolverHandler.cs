using Microsoft.Extensions.Logging;
using Vivnest.Capabilities.MotionSensor;
using Vivnest.Core.Events;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Capabilities.Triggers;

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
            motionSensor = _deviceRegistry.GetDevice(@event.DeviceId, DeviceType.MotionSensor);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        foreach (var targetDeviceId in motionSensor.Trigger.DeviceIds)
        {
            // A target id can now resolve to more than one capability (e.g.
            // a camera triggering its own capture on its own motion event -
            // docs/architecture/dashboard-domain-model.md §8) - publish once
            // per capability it actually has and let each one's own handler
            // decide whether it cares (CaptureOnTriggerHandler already
            // filters to DeviceType.Camera).
            var targets = _deviceRegistry.GetDevices(targetDeviceId);

            if (targets.Count == 0)
            {
                _logger.LogWarning(
                    "Motion sensor {DeviceId} is configured to trigger unknown device {TargetDeviceId}.",
                    @event.DeviceId,
                    targetDeviceId);

                continue;
            }

            foreach (var target in targets)
            {
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
}
