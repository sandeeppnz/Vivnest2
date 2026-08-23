using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Core.Capabilities;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Capabilities;

// The lifecycle every device-backed capability shares: start a worker,
// supervise it, stop it, and report status honestly throughout.
//
// Why this exists (ADR-107). CameraCapability, MotionSensorCapability and
// SmartPlugCapability were 59-68% identical at ~430 tokens each. Only the
// worker type, the manifest, the DeviceType filter and one log phrase ever
// differed; the ~78 lines below were copied verbatim into all three.
//
// That duplication was created by ADR-103, in the same change that
// extracted CapabilityWorkerSupervisor to stop the three drifting on
// worker supervision. The supervisor was hoisted; the lifecycle around it
// was pasted. Both halves needed doing and only one was done, which is
// easy to miss precisely because the extracted part looks like the fix.
//
// The rules encoded here, each of which cost something to learn:
//
//   Starting/Running is re-entrant-safe - a second StartAsync is a no-op
//   rather than a second worker.
//
//   A capability with no devices to act on is Failed, not Running
//   (ADR-103). Its worker would start, find nothing, return immediately,
//   and the capability would sit at Running forever while nothing
//   happened. Checked here rather than in the worker: a generic
//   BackgroundService has no business inventing a CapabilityStatus, and
//   throwing from the worker would report a configuration mistake as a
//   stack trace.
//
//   Observe() is wired AFTER the status is set to Running, not before.
//   The observer reads that status to tell a death from a shutdown, so a
//   worker that faults instantly would otherwise be judged against
//   Starting.
//
//   A throw from StartAsync leaves the capability Failed and rethrows.
//   CapabilityHost's own catch decides what that means for the Agent -
//   still ADR-095's open fault-isolation question.
public abstract class DeviceCapabilityBase : ICapability
{
    private readonly BackgroundService _worker;
    private readonly IDeviceRuntimeStore _devices;

    private CapabilityStatus _status = CapabilityStatus.Registered;

    protected DeviceCapabilityBase(
        BackgroundService worker,
        IDeviceRuntimeStore devices,
        ILogger logger)
    {
        _worker = worker;
        _devices = devices;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    // The task watching this capability's worker, completing once the
    // worker has stopped and the observer has reacted. Task.CompletedTask
    // before StartAsync, and when the worker was never started (no
    // devices). Exposed so the observation can be awaited rather than
    // waited for - see CapabilityWorkerSupervisor.Observe.
    public Task WorkerObservation { get; private set; } = Task.CompletedTask;

    public CapabilityStatus Status =>
        _status;

    public abstract CapabilityManifest Manifest { get; }

    // Which devices this capability needs at least one of to be healthy.
    protected abstract DeviceType DeviceType { get; }

    // Plural, lower case, as it reads mid-sentence: "no camera devices are
    // assigned to this Agent". Only ever used in that message.
    protected abstract string DeviceNoun { get; }

    // Overridable so a capability can say more about what it started with.
    // camera.capture logs its assignment here (ADR-101) - the others have
    // nothing extra to say, and an empty override is better than every
    // capability carrying a field it does not read.
    protected virtual void LogStarting(ICapabilityContext context) =>
        Logger.LogInformation(
            "Starting capability {CapabilityId} for agent {AgentId}.",
            Manifest.Id,
            context.AgentId);

    public async Task StartAsync(
        ICapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (_status is
            CapabilityStatus.Starting or
            CapabilityStatus.Running)
        {
            return;
        }

        _status = CapabilityStatus.Starting;

        try
        {
            LogStarting(context);

            var deviceCount = _devices
                .GetDevices()
                .Count(d => d.Type == DeviceType);

            if (deviceCount == 0)
            {
                _status = CapabilityStatus.Failed;

                Logger.LogError(
                    "Capability {CapabilityId} cannot start: no {DeviceNoun} are assigned " +
                    "to this Agent. The Agent stays up; this capability is " +
                    "Failed until devices are assigned and it is restarted.",
                    Manifest.Id,
                    DeviceNoun);

                return;
            }

            await _worker.StartAsync(
                cancellationToken);

            _status = CapabilityStatus.Running;

            WorkerObservation = CapabilityWorkerSupervisor.Observe(
                _worker,
                Manifest.Id,
                Logger,
                isRunning: () => _status == CapabilityStatus.Running,
                markFailed: () => _status = CapabilityStatus.Failed);

            Logger.LogInformation(
                "Capability {CapabilityId} started.",
                Manifest.Id);
        }
        catch
        {
            _status = CapabilityStatus.Failed;

            throw;
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        if (_status is
            CapabilityStatus.Stopped or
            CapabilityStatus.Registered)
        {
            return;
        }

        _status = CapabilityStatus.Stopping;

        try
        {
            Logger.LogInformation(
                "Stopping capability {CapabilityId}.",
                Manifest.Id);

            await _worker.StopAsync(
                cancellationToken);

            _status = CapabilityStatus.Stopped;

            Logger.LogInformation(
                "Capability {CapabilityId} stopped.",
                Manifest.Id);
        }
        catch
        {
            _status = CapabilityStatus.Failed;

            throw;
        }
    }
}
