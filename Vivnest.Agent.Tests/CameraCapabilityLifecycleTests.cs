using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Core.Capabilities;
using Vivnest.Capabilities.Camera;
using Vivnest.Core.Camera.Models;
using Vivnest.Core.Devices.Stores;
using DeviceType = Vivnest.Domain.Devices.DeviceType;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Tests;

// ADR-103, the lifecycle contract:
//
//   Worker starts successfully   -> Running
//   No assigned devices          -> Failed  (worker never started)
//   Unexpected exception         -> Failed
//   Unexpected completion        -> Failed
//   Normal cancellation          -> Stopped
//   Startup exception            -> Failed
//
// and critically: Failed != Agent stopped. Nothing here calls
// StopApplication, and CapabilityHostFaultIsolationTests proves the host
// survives a capability that fails.
//
// These drive the REAL CameraCapability and the REAL CameraCaptureWorker,
// with only the leaf I/O stubbed. A test against a fake worker would have
// proved the supervisor works while saying nothing about whether
// CameraCaptureWorker's own fault paths actually reach it - and reaching
// it is the entire regression.
public class CameraCapabilityLifecycleTests
{
    // 1. The healthy case, so the failure cases mean something.
    [Fact]
    public async Task AWorkerThatStartsAndKeepsRunningLeavesTheCapabilityRunning()
    {
        var h = new Harness();

        await h.Capability.StartAsync(h.Context, CancellationToken.None);

        Assert.Equal(CapabilityStatus.Running, h.Capability.Status);
    }

    // 2. The regression itself. Before this fix the capture loop could die
    //    and the capability went on reporting Running - which is exactly
    //    what happened for 4h16m on 2026-08-22.
    [Fact]
    public async Task AWorkerThatFaultsMarksTheCapabilityFailed()
    {
        var h = new Harness();
        h.Executor.ThrowOnCapture = new InvalidOperationException("capture loop died");

        await h.Capability.StartAsync(h.Context, CancellationToken.None);

        await WaitForStatusAsync(h.Capability, CapabilityStatus.Failed);

        Assert.Equal(CapabilityStatus.Failed, h.Capability.Status);
        Assert.NotEmpty(h.Log.Errors);
    }

    // 3. Returning is not throwing, but it is still death: the loop is gone
    //    and nothing will ever capture again. Provoked here by cancelling
    //    the token the worker was started with, WITHOUT going through
    //    StopAsync - so the capability still believes it is Running. That
    //    is the shape of an unexpected exit, as opposed to a shutdown.
    [Fact]
    public async Task AWorkerThatExitsWhileTheCapabilityStillExpectsItMarksItFailed()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();

        await h.Capability.StartAsync(h.Context, cts.Token);

        Assert.Equal(CapabilityStatus.Running, h.Capability.Status);

        cts.Cancel();

        await WaitForStatusAsync(h.Capability, CapabilityStatus.Failed);

        Assert.Equal(CapabilityStatus.Failed, h.Capability.Status);
        Assert.NotEmpty(h.Log.Errors);
    }

    // 4. The opposite, and the one that stops this fix from crying wolf on
    //    every shutdown: a deliberate stop ends at Stopped, never Failed.
    [Fact]
    public async Task StoppingTheCapabilityLeavesItStoppedNotFailed()
    {
        var h = new Harness();

        await h.Capability.StartAsync(h.Context, CancellationToken.None);
        await h.Capability.StopAsync(CancellationToken.None);

        Assert.Equal(CapabilityStatus.Stopped, h.Capability.Status);

        // The observer runs on a continuation, so give it every chance to
        // wrongly report a failure before believing it did not.
        await Task.Delay(150);

        Assert.Equal(CapabilityStatus.Stopped, h.Capability.Status);
        Assert.Empty(h.Log.Errors);
    }

    // 5. Cancellation during shutdown is not a failure. CameraCaptureExecutor
    //    deliberately RETHROWS OperationCanceledException when cancelled, so
    //    ExecuteTask genuinely faults on a normal stop - the observer has to
    //    tell that apart from a real fault or every clean shutdown reports
    //    Failed. (The first cut of this fix got this wrong.)
    [Fact]
    public async Task CancellationDuringShutdownIsNotAFailure()
    {
        var h = new Harness();

        // The worker has to be INSIDE a capture when the stop arrives, or
        // it just falls out of its loop and returns - which is a different
        // path and would leave this test asserting the right answer for the
        // wrong reason. It did, at first: setting an OCE to be thrown on
        // capture proved nothing, because StopAsync moved the capability to
        // Stopping before the worker had reached its first capture at all.
        h.Executor.BlockUntilCancelled = true;

        await h.Capability.StartAsync(h.Context, CancellationToken.None);
        await h.Executor.EnteredCapture;

        await h.Capability.StopAsync(CancellationToken.None);

        await Task.Delay(150);

        // The OCE has now genuinely travelled out of the executor, through
        // Task.WhenAll, out of ExecuteAsync and into the observer.
        Assert.Equal(TaskStatus.Canceled, h.Worker.ExecuteTask!.Status);

        Assert.Equal(CapabilityStatus.Stopped, h.Capability.Status);

        // Asserted on the LOG, not only on the final status. StopAsync
        // writes Stopped after the observer has run, so a wrongly-reported
        // failure would be overwritten and the status alone would look
        // fine - which is how the first version of this test passed while
        // proving nothing. An Error on every clean shutdown is the actual
        // damage, and it is visible here.
        Assert.Empty(h.Log.Errors);
    }

    // The same exception type with nothing stopping is the opposite verdict.
    // OperationCanceledException is not proof of shutdown - an HTTP timeout
    // mid-capture surfaces as TaskCanceledException too - so the observer
    // asks the capability whether it still expects the worker to be running
    // rather than trusting the exception type.
    [Fact]
    public async Task CancellationWithNothingStoppingIsAFailure()
    {
        var h = new Harness();
        h.Executor.ThrowOnCapture = new OperationCanceledException();

        await h.Capability.StartAsync(h.Context, CancellationToken.None);

        await WaitForStatusAsync(h.Capability, CapabilityStatus.Failed);

        Assert.Equal(CapabilityStatus.Failed, h.Capability.Status);
        Assert.NotEmpty(h.Log.Errors);
    }

    // 6. Zero devices. An enabled camera.capture with no cameras assigned
    //    cannot do anything at all, so Running would be a lie. The worker
    //    must never be started - not started-then-returned.
    [Fact]
    public async Task AnEnabledCapabilityWithNoCamerasFailsAndNeverStartsTheWorker()
    {
        var h = new Harness(cameras: 0);

        await h.Capability.StartAsync(h.Context, CancellationToken.None);

        Assert.Equal(CapabilityStatus.Failed, h.Capability.Status);
        Assert.Null(h.Worker.ExecuteTask);
    }

    // 7. Startup failure. StartAsync must not leave a half-started
    //    capability claiming Running.
    [Fact]
    public async Task AStartupExceptionMarksTheCapabilityFailed()
    {
        var h = new Harness();
        h.Devices.ThrowOnQuery = new InvalidOperationException("device store unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Capability.StartAsync(h.Context, CancellationToken.None));

        Assert.Equal(CapabilityStatus.Failed, h.Capability.Status);
    }

    // 8. The policy decision, asserted structurally rather than by hoping a
    //    test would notice. A failed capability must not take the Agent
    //    down: if a capture failure restarts the container, "capability
    //    failed" and "Agent failed" become the same observable event and
    //    the health model loses the distinction it exists to make.
    [Fact]
    public void NothingInTheCapabilityCanStopTheHost()
    {
        var dependencies = typeof(CameraCapability)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain("IHostApplicationLifetime", dependencies);
    }

    // =======================================================================

    private static async Task WaitForStatusAsync(
        ICapability capability, CapabilityStatus expected)
    {
        for (var i = 0; i < 200 && capability.Status != expected; i++)
            await Task.Delay(10);
    }

    private sealed class Harness
    {
        public StubDeviceRuntimeStore Devices { get; }
        public RecordingLogger Log { get; } = new();
        public StubExecutor Executor { get; } = new();
        public CameraCaptureWorker Worker { get; }
        public CameraCapability Capability { get; }
        public ICapabilityContext Context { get; }

        public Harness(int cameras = 1)
        {
            Devices = new StubDeviceRuntimeStore(cameras);

            Worker = new CameraCaptureWorker(
                new StubCaptureService(),
                Executor,
                new InMemoryRuntimeStateStore(),
                Devices,
                NullLogger<CameraCaptureWorker>.Instance);

            Capability = new CameraCapability(
                Worker,
                Devices,
                Log);

            Context = new StubContext();
        }
    }

    // Only Error matters here: a dead worker has to SAY so, and a clean
    // shutdown has to stay quiet. Everything below Error is noise for
    // these tests.
    private sealed class RecordingLogger : ILogger<CameraCapability>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    private sealed class StubContext : ICapabilityContext
    {
        public string AgentId => "agent-1";
        public string? TenantId => "tenant-1";
        public string? SiteId => "site-1";
        public IServiceProvider Services => throw new NotSupportedException();

        public RuntimeCapabilityAssignment Assignment => new()
        {
            CapabilityId = "camera.capture",
            CapabilityName = "Camera Capture",
            Enabled = true
        };
    }

    private sealed class StubDeviceRuntimeStore : IDeviceRuntimeStore
    {
        private readonly List<DeviceOptions> _devices;

        public StubDeviceRuntimeStore(int cameras)
        {
            _devices = Enumerable.Range(0, cameras)
                .Select(i => new DeviceOptions
                {
                    DeviceId = $"camera-{i}",
                    Name = $"Camera {i}",
                    Type = DeviceType.Camera,
                    Enabled = true,

                    // Long enough that nothing captures on its own: the
                    // tests drive the outcome, the clock does not.
                    LivenessInterval = TimeSpan.FromMinutes(5),
                    Schedule = new ScheduleOptions { Interval = TimeSpan.FromMinutes(5) }
                })
                .ToList();
        }

        public Exception? ThrowOnQuery { get; set; }

        public DeviceOptions GetDevice(string id, DeviceType type) =>
            _devices.First(d => d.DeviceId == id && d.Type == type);

        public IReadOnlyCollection<DeviceOptions> GetDevices(string id) =>
            _devices.Where(d => d.DeviceId == id).ToList();

        public IReadOnlyCollection<DeviceOptions> GetDevices() =>
            ThrowOnQuery != null ? throw ThrowOnQuery : _devices;
    }

    private sealed class InMemoryRuntimeStateStore : IDeviceRuntimeStateStore
    {
        private readonly Dictionary<string, DeviceRuntimeState> _states = [];

        public DeviceRuntimeState GetOrAdd(string deviceId)
        {
            if (!_states.TryGetValue(deviceId, out var state))
                _states[deviceId] = state = new DeviceRuntimeState();

            return state;
        }
    }

    // LastCaptureUtc is never set on the first tick, so the worker considers
    // it due for capture and goes straight through the executor - which is
    // where the tests inject a fault.
    private sealed class StubExecutor : ICameraCaptureExecutor
    {
        private readonly TaskCompletionSource _entered = new();

        public Exception? ThrowOnCapture { get; set; }

        // Mirrors the real CameraCaptureExecutor, which rethrows
        // OperationCanceledException when the caller cancelled it rather
        // than recording a capture failure.
        public bool BlockUntilCancelled { get; set; }

        public Task EnteredCapture => _entered.Task;

        public async Task CaptureAsync(
            DeviceOptions cameraOptions,
            DeviceRuntimeState runtime,
            CancellationToken cancellationToken,
            string? triggerReason = null)
        {
            _entered.TrySetResult();

            if (ThrowOnCapture != null)
                throw ThrowOnCapture;

            if (BlockUntilCancelled)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            runtime.LastCaptureUtc = DateTime.UtcNow;
        }
    }

    private sealed class StubCaptureService : ICameraCaptureService
    {
        public Task<CameraCaptureResult> CaptureAsync(
            DeviceOptions cameraOptions, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the executor is stubbed above it");

        public Task<bool> CheckReachabilityAsync(
            DeviceOptions cameraOptions, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
