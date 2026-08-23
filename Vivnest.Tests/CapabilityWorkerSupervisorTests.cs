using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Tests;

// ADR-103. A capability starts its BackgroundService by hand, which leaves
// ExecuteAsync unobserved - a fault in it is swallowed entirely: no log, no
// status change, no restart, and the capability still reporting Running.
//
// That is not hypothetical. On 2026-08-22 CameraCaptureWorker stopped at
// 22:26:29 and stayed stopped for over four hours while the Agent published
// healthy heartbeats every minute. The platform workers, still registered
// with AddHostedService, resumed across the same gap - the framework was
// observing those and this one had nobody watching it.
public class CapabilityWorkerSupervisorTests
{
    // The whole point: a fault must become visible and must stop the host,
    // which is what AddHostedService's default StopHost behaviour used to do
    // for us before capabilities started their own workers.
    [Fact]
    public async Task AFaultingWorkerMarksTheCapabilityFailedAndStopsTheHost()
    {
        var worker = new FaultingWorker();
        var lifetime = new RecordingLifetime();
        var failed = false;

        await worker.StartAsync(CancellationToken.None);

        CapabilityWorkerSupervisor.Observe(
            worker, "camera.capture", NullLogger.Instance,
            markFailed: () => failed = true, lifetime);

        await worker.Faulted;
        await WaitForAsync(() => lifetime.StopRequested);

        Assert.True(failed);
        Assert.True(lifetime.StopRequested);
    }

    // A worker that returns normally is NOT a fault. CameraCaptureWorker
    // returns immediately when no cameras are configured, and restarting the
    // Agent over that would be a boot loop.
    [Fact]
    public async Task AWorkerThatCompletesNormallyDoesNotStopTheHost()
    {
        var worker = new CompletingWorker();
        var lifetime = new RecordingLifetime();
        var failed = false;

        await worker.StartAsync(CancellationToken.None);

        CapabilityWorkerSupervisor.Observe(
            worker, "camera.capture", NullLogger.Instance,
            markFailed: () => failed = true, lifetime);

        await worker.Finished;

        Assert.False(failed);
        Assert.False(lifetime.StopRequested);
    }

    // Observing before StartAsync has run must not throw - ExecuteTask is
    // null then, and a capability that failed to start should report its own
    // error rather than an ArgumentNullException from the supervisor.
    [Fact]
    public void ObservingAWorkerThatWasNeverStartedIsSafe()
    {
        var lifetime = new RecordingLifetime();

        CapabilityWorkerSupervisor.Observe(
            new CompletingWorker(), "camera.capture", NullLogger.Instance,
            markFailed: () => Assert.Fail("must not be called"), lifetime);

        Assert.False(lifetime.StopRequested);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);
    }

    private sealed class FaultingWorker : BackgroundService
    {
        private readonly TaskCompletionSource _faulted = new();

        public Task Faulted => _faulted.Task;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yield first, so StartAsync returns and the fault happens
            // afterwards - exactly the shape that goes unobserved.
            await Task.Yield();

            _faulted.SetResult();

            throw new InvalidOperationException("capture loop died");
        }
    }

    private sealed class CompletingWorker : BackgroundService
    {
        private readonly TaskCompletionSource _finished = new();

        public Task Finished => _finished.Task;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            _finished.SetResult();
        }
    }

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested = true;
    }
}
