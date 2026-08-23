using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vivnest.Runtime.Capabilities;

// Watches the BackgroundService a capability started, so a worker that
// dies stops being invisible.
//
// Why this exists (ADR-103). Before ADR-101 these workers were registered
// with AddHostedService and the framework observed ExecuteAsync for us.
// Starting a BackgroundService by hand - which is what a capability does
// now - assigns ExecuteTask and awaits nothing, so a fault is swallowed
// whole: no log, no status change, and the capability still reporting
// Running because nothing tells it otherwise.
//
// Observed in production on 2026-08-22: CameraCaptureWorker stopped at
// 22:26:29 and stayed stopped for 4h16m while the Agent published healthy
// heartbeats every minute. The platform workers, still hosted services,
// resumed across the same gap. On a camera monitoring system, silently
// ceasing to monitor is the worst failure mode available - worse than
// crashing, because nothing says so.
//
// WHO OWNS WHAT. The host owns capability lifecycle; a capability owns its
// worker's. This is a helper the CAPABILITY calls - it never runs from
// CapabilityHost, which has no business knowing that a CameraCaptureWorker
// exists. It is shared only so the three capabilities cannot drift into
// three subtly different answers to the same question.
//
// WHAT IT DOES NOT DO. It does not stop the host and it does not restart
// anything. A failed capability is a health signal, not an Agent failure -
// an Agent reporting `camera.capture: Failed, motion.sensor: Running` is
// strictly more informative than a container that quietly bounces. The
// first cut of this fix did call StopApplication(); that was auto-restart
// with no retry limit and no backoff, wearing the clothes of a status fix.
// Controlled recovery - retry policy, backoff, capability restart - is its
// own phase and needs those questions answered first.
public static class CapabilityWorkerSupervisor
{
    // isRunning: does the capability still believe this worker should be
    // running? It is the difference between a death and a shutdown, and
    // only the capability can answer it - see the OperationCanceledException
    // note below for why the exception type alone cannot.
    public static void Observe(
        BackgroundService worker,
        string capabilityId,
        ILogger logger,
        Func<bool> isRunning,
        Action markFailed)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(isRunning);
        ArgumentNullException.ThrowIfNull(markFailed);

        var task = worker.ExecuteTask;

        // Null when StartAsync has not run, or when ExecuteAsync completed
        // synchronously without ever yielding. Nothing to watch either way.
        if (task == null)
            return;

        _ = ObserveAsync(task, capabilityId, logger, isRunning, markFailed);
    }

    private static async Task ObserveAsync(
        Task task,
        string capabilityId,
        ILogger logger,
        Func<bool> isRunning,
        Action markFailed)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (!isRunning())
        {
            // Cancelled while we were stopping it: normal shutdown.
            //
            // Guarded on isRunning() rather than on the exception type
            // alone, because OperationCanceledException is NOT proof of
            // shutdown - an HTTP timeout mid-capture surfaces as
            // TaskCanceledException too, and swallowing that would
            // reintroduce exactly the silence this class exists to remove.
            // StopAsync sets Stopping BEFORE cancelling the token, so by
            // the time this runs the status is already correct.
            return;
        }
        catch (Exception ex)
        {
            if (!isRunning())
            {
                // Already stopping - StopAsync owns the outcome and will
                // report it. Not ours to overwrite.
                return;
            }

            markFailed();

            logger.LogError(
                ex,
                "Capability {CapabilityId} worker failed. The capability is " +
                "now Failed; the Agent stays up and other capabilities are " +
                "unaffected.",
                capabilityId);

            return;
        }

        if (!isRunning())
        {
            // Returned because we asked it to. Nothing to say.
            return;
        }

        // Returned on its own while we still expected it to be working.
        // Not an exception, but not healthy either - the loop is gone and
        // nothing will capture again.
        markFailed();

        logger.LogError(
            "Capability {CapabilityId} worker stopped unexpectedly without " +
            "faulting. The capability is now Failed; the Agent stays up.",
            capabilityId);
    }
}
