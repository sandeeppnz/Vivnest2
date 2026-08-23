using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vivnest.Runtime.Capabilities;

// Watches a BackgroundService a capability started, so that a fault in its
// ExecuteAsync is not swallowed.
//
// Why this exists (ADR-103). Before ADR-101 these workers were registered
// with AddHostedService, and the framework observed ExecuteAsync for us:
// a fault triggered BackgroundServiceExceptionBehavior, whose default is
// StopHost, so the container died, the restart policy restarted it, and
// capture resumed within seconds. Loud and self-healing.
//
// Starting a BackgroundService by hand - which is what a capability now
// does - stores ExecuteTask and never awaits it. A fault is then swallowed
// entirely: no log, no status change, no restart. The capability keeps
// reporting Running because nothing tells it otherwise.
//
// That was observed in production on 2026-08-22: CameraCaptureWorker
// stopped at 22:26:29 and stayed stopped for over four hours while the
// Agent reported healthy heartbeats every minute. The platform workers,
// still registered as hosted services, resumed across the same gap. On a
// camera monitoring system, silently ceasing to monitor is the worst
// available failure mode - worse than crashing, because nothing says so.
//
// This restores the previous behaviour rather than inventing a new policy:
// mark the capability Failed, say so at Error, and stop the host so the
// container's restart policy can do what it used to do. Whether a single
// failed capability *should* take down an Agent that is otherwise fine is
// the fault-isolation question ADR-095 deferred - deliberately unchanged
// here, because restoring known behaviour and choosing new behaviour are
// different decisions and should not ride in on the same fix.
public static class CapabilityWorkerSupervisor
{
    public static void Observe(
        BackgroundService worker,
        string capabilityId,
        ILogger logger,
        Action markFailed,
        IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(markFailed);
        ArgumentNullException.ThrowIfNull(lifetime);

        var task = worker.ExecuteTask;

        // Null when StartAsync has not run, or when ExecuteAsync completed
        // synchronously without ever yielding. Nothing to watch either way.
        if (task == null)
            return;

        _ = task.ContinueWith(
            completed =>
            {
                markFailed();

                logger.LogError(
                    completed.Exception,
                    "Capability {CapabilityId} worker faulted and has stopped. " +
                    "Stopping the Agent so it can restart.",
                    capabilityId);

                lifetime.StopApplication();
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        // A worker that simply RETURNS is not a fault, but for these
        // capabilities it still means the work stopped - e.g.
        // CameraCaptureWorker returns immediately when no cameras are
        // configured. That is legitimate and must not restart anything, so
        // it is logged and left alone rather than escalated.
        _ = task.ContinueWith(
            _ => logger.LogInformation(
                "Capability {CapabilityId} worker completed without faulting.",
                capabilityId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}
