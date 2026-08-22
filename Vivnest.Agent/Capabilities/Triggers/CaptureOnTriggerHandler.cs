using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Events;
using Vivnest.Agent.Capabilities.Camera;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Triggers;

// Deliberately narrow: fires one immediate capture and flips two fields on
// DeviceRuntimeState (BurstUntilUtc/BurstInterval) - it doesn't loop, and
// it doesn't touch DeviceEvent/blob persistence at all. The repeating
// every-BurstInterval captures are CameraCaptureWorker's own existing
// timer loop noticing burst mode each tick; the persistence is the same
// CameraCaptureCompletedEvent -> CameraCaptureHandler pipeline every
// capture already goes through, scheduled or triggered alike.
public sealed class CaptureOnTriggerHandler : IEventHandler<DeviceTriggeredEvent>
{
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly ICameraCaptureExecutor _executor;
    private readonly ILogger<CaptureOnTriggerHandler> _logger;

    public CaptureOnTriggerHandler(
        IDeviceRuntimeStore deviceRegistry,
        IDeviceRuntimeStateStore statusStore,
        ICameraCaptureExecutor executor,
        ILogger<CaptureOnTriggerHandler> logger)
    {
        _deviceRegistry = deviceRegistry;
        _statusStore = statusStore;
        _executor = executor;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeviceTriggeredEvent @event,
        CancellationToken cancellationToken)
    {
        if (@event.DeviceType != DeviceType.Camera)
            return;

        DeviceOptions camera;

        try
        {
            camera = _deviceRegistry.GetDevice(@event.DeviceId, DeviceType.Camera);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        var runtime = _statusStore.GetOrAdd(camera.DeviceId);

        runtime.BurstInterval = camera.Schedule.Burst.Interval;
        runtime.BurstUntilUtc = DateTime.UtcNow.Add(camera.Schedule.Burst.Duration);
        runtime.BurstReason = @event.Reason;

        _logger.LogInformation(
            "Motion burst started for {DeviceId} ({Reason}): every {Interval} until {Until:u}.",
            camera.DeviceId,
            @event.Reason,
            runtime.BurstInterval,
            runtime.BurstUntilUtc);

        // Wake CameraCaptureWorker's loop immediately rather than letting
        // it discover burst mode whenever its current sleep happens to end.
        // Release() throws if the signal is already pending (a second
        // trigger arriving before the worker's consumed the first) -
        // harmless, it just means the worker's already going to wake up.
        try
        {
            runtime.WakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }

        await _executor.CaptureAsync(camera, runtime, cancellationToken, @event.Reason);
    }
}
