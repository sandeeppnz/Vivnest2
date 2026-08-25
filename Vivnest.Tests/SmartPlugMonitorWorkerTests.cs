using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vivnest.Capabilities.SmartPlug;
using Vivnest.Core.Events;
using Vivnest.Core.Options;
using Vivnest.Core.Devices.SmartPlug.Models;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;
using Vivnest.Runtime.State;

namespace Vivnest.Tests;

// Two defects found reviewing Vivnest.Capabilities on 2026-08-24, both in
// SmartPlugMonitorWorker.ReadAsync, and both cases where the sibling worker
// for a different device type already did the right thing.
//
// These drive the REAL SmartPlugMonitorWorker with only the leaf I/O
// stubbed, for the same reason CameraCapabilityLifecycleTests does: a test
// against a fake worker would prove the pattern and say nothing about
// whether this worker actually follows it.
public class SmartPlugMonitorWorkerTests
{
    // ---- the phantom transition ------------------------------------------

    // The bug: lastKnownIsOn starts null, so `isOn != lastKnownIsOn` was
    // true on the first successful read after EVERY restart. That published
    // a PowerStateChanged for a plug that had not changed, which Cloud
    // turned into a "turned on"/"turned off" Telegram notification
    // (DeviceEventQueueHandler.HandlePowerStateChangedAsync sends one
    // unconditionally). MotionSensorMonitorWorker guards exactly this and
    // cites ADR-016; this worker did not.
    [Fact]
    public async Task TheFirstReadAfterARestartPublishesNoStateChange()
    {
        await using var h = new Harness();
        h.Service.Readings = [On, On, On];

        await h.RunUntilReadsAsync(3);

        Assert.Empty(h.Dispatcher.Events.OfType<SmartPlugPowerStateChangedEvent>());
    }

    // The baseline must still be RECORDED, not just left null - otherwise
    // the guard would suppress the first genuine change too.
    [Fact]
    public async Task AGenuineFlipAfterTheBaselinePublishesExactlyOnce()
    {
        await using var h = new Harness();
        h.Service.Readings = [On, On, Off, Off];

        await h.RunUntilReadsAsync(4);

        var changes = h.Dispatcher.Events
            .OfType<SmartPlugPowerStateChangedEvent>()
            .ToList();

        Assert.Single(changes);
        Assert.False(changes[0].IsOn);
    }

    // A read that fails carries no state at all, so it must not be mistaken
    // for a transition or reset the baseline - the plug is unreachable, not
    // switched off.
    [Fact]
    public async Task AFailedReadIsNotATransition()
    {
        await using var h = new Harness();
        h.Service.Readings = [On, Failed, On];

        await h.RunUntilReadsAsync(3);

        Assert.Empty(h.Dispatcher.Events.OfType<SmartPlugPowerStateChangedEvent>());
        Assert.Single(h.Dispatcher.Events.OfType<SmartPlugReadingFailedEvent>());
    }

    // ---- fault isolation on the routine path ------------------------------

    // The bug: this was the only publish in the class not wrapped in a
    // try/catch, and it is the one that runs every tick.
    // SmartPlugReadingHandler rethrows when persistence fails and
    // EventDispatcher rethrows as AggregateException, so one transient table
    // write faulted ExecuteAsync through Task.WhenAll and stopped monitoring
    // for every plug on the Agent until it was restarted.
    [Fact]
    public async Task AHandlerThatThrowsOnEveryReadingDoesNotStopTheLoop()
    {
        await using var h = new Harness();
        h.Service.Readings = [On, On, On, On];
        h.Dispatcher.ThrowOn = typeof(SmartPlugReadingCompletedEvent);

        await h.RunUntilReadsAsync(4);

        // The loop survived all four ticks rather than dying on the first.
        Assert.Equal(4, h.Service.Reads);

        // And it faulted nothing: this is the assertion that would have
        // failed before the fix.
        Assert.Null(h.Worker.ExecuteTask!.Exception);
    }

    // The failure must still be visible - "does not stop the loop" is only
    // correct if it is not also silent.
    [Fact]
    public async Task ThatFailureIsStillReported()
    {
        await using var h = new Harness();
        h.Service.Readings = [On, On];
        h.Dispatcher.ThrowOn = typeof(SmartPlugReadingCompletedEvent);

        await h.RunUntilReadsAsync(2);

        Assert.NotEmpty(h.Log.Errors);
    }

    // =======================================================================

    private static SmartPlugReadingResult On => Reading(isOn: true);
    private static SmartPlugReadingResult Off => Reading(isOn: false);

    private static SmartPlugReadingResult Failed => new()
    {
        Success = false,
        DeviceId = "plug-0",
        ReadAtUtc = DateTime.UtcNow,
        Error = "unreachable",
        ErrorCode = "TIMEOUT"
    };

    private static SmartPlugReadingResult Reading(bool isOn) => new()
    {
        Success = true,
        DeviceId = "plug-0",
        ReadAtUtc = DateTime.UtcNow,
        State = new SmartPlugState { IsOn = isOn }
    };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public FakeMonitorService Service { get; } = new();
        public RecordingDispatcher Dispatcher { get; } = new();
        public RecordingLogger Log { get; } = new();
        public SmartPlugMonitorWorker Worker { get; }

        public Harness()
        {
            Worker = new SmartPlugMonitorWorker(
                Service,
                Dispatcher,
                new InMemoryRuntimeStateStore(),
                new StubDeviceRuntimeStore(),
                Options.Create(new AgentOptions { AgentId = "agent-1" }),
                Log);
        }

        // Runs the real loop until it has read `count` times, then stops it
        // and waits for it to unwind. The device's Schedule.Interval is zero
        // so every tick is due for a full read, and LivenessInterval is tiny
        // so the ticks are not the thing under test.
        public async Task RunUntilReadsAsync(int count)
        {
            Service.Target = count;

            await Worker.StartAsync(_cts.Token);

            // Bounded deliberately. A worker whose loop has died never
            // reaches the target, and an unbounded await would hang the
            // suite instead of failing it - which is exactly what happened
            // when the fault-isolation fix was mutated out to check this
            // test could catch it. A hang in CI is worse than a red test:
            // it reports nothing at all. The timeout is a liveness guard,
            // not a timing assertion; the ticks are 1ms apart, so a
            // healthy loop reaches any of these targets in milliseconds.
            var finished = await Task.WhenAny(
                Service.Reached,
                Task.Delay(TimeSpan.FromSeconds(10)));

            await _cts.CancelAsync();

            Assert.True(
                finished == Service.Reached,
                $"The worker read {Service.Reads} time(s) of {count} before "
                + "stopping - its loop died rather than surviving the tick. "
                + $"Worker fault: {Worker.ExecuteTask?.Exception?.GetBaseException().Message ?? "none"}");

            try
            {
                await Worker.ExecuteTask!;
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_cts.IsCancellationRequested)
                await _cts.CancelAsync();

            _cts.Dispose();
        }
    }

    private sealed class FakeMonitorService : ISmartPlugMonitorService
    {
        private readonly TaskCompletionSource _reached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SmartPlugReadingResult> Readings { get; set; } = [];
        public int Target { get; set; } = int.MaxValue;
        public int Reads { get; private set; }

        public Task Reached => _reached.Task;

        public Task<SmartPlugReadingResult> ReadAsync(
            DeviceOptions plugOptions,
            CancellationToken cancellationToken)
        {
            // Clamped rather than wrapped: a worker that keeps ticking past
            // the scripted readings repeats the last one, which is a steady
            // state and cannot manufacture a transition.
            var result = Readings[Math.Min(Reads, Readings.Count - 1)];

            Reads++;

            if (Reads >= Target)
                _reached.TrySetResult();

            return Task.FromResult(result);
        }

        public Task<bool> CheckReachabilityAsync(
            DeviceOptions plugOptions,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class RecordingDispatcher : IEventDispatcher
    {
        public List<object> Events { get; } = [];

        // Mirrors the real EventDispatcher, which logs and then rethrows as
        // AggregateException once any handler has failed.
        public Type? ThrowOn { get; set; }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default)
        {
            lock (Events)
            {
                Events.Add(@event!);
            }

            if (ThrowOn == typeof(TEvent))
            {
                throw new AggregateException(
                    $"One or more handlers failed while handling {typeof(TEvent).Name}.",
                    new InvalidOperationException("the table is unavailable"));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<SmartPlugMonitorWorker>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Error)
            {
                lock (Errors)
                {
                    Errors.Add(formatter(state, exception));
                }
            }
        }
    }

    private sealed class StubDeviceRuntimeStore : IDeviceRuntimeStore
    {
        private readonly List<DeviceOptions> _devices =
        [
            new()
            {
                DeviceId = "plug-0",
                Name = "Plug 0",
                Type = DeviceType.SmartPlug,
                Enabled = true,

                LivenessInterval = TimeSpan.FromMilliseconds(1),

                // Zero means "every liveness tick is a full read", so the
                // reading path - the one under test - runs on every tick.
                Schedule = new ScheduleOptions { Interval = TimeSpan.Zero }
            }
        ];

        public DeviceOptions GetDevice(string id, DeviceType type) =>
            _devices.First(d => d.DeviceId == id && d.Type == type);

        public IReadOnlyCollection<DeviceOptions> GetDevices(string id) =>
            _devices.Where(d => d.DeviceId == id).ToList();

        public IReadOnlyCollection<DeviceOptions> GetDevices() => _devices;
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
}
