using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Abstraction.Agent.Capabilities;
using Vivnest.Abstraction.Agent.Commands;
using Vivnest.Abstraction.Agent.Events;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Agent.Runtime.Commands;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Agent.Tests;

// ADR-102, Command Routing 1.8. The handler no longer knows any capability
// by name: it resolves one from the registry by the runtime key the command
// carries, checks it is running, and checks it advertises something
// invokable - then hands off to the EXISTING DeviceTriggeredEvent path.
//
// That last part is the one worth guarding. CameraCaptureExecutor was
// extracted precisely so CameraCaptureWorker and CaptureOnTriggerHandler
// would not drift into two capture implementations; routing that invoked a
// capability directly would recreate the split this codebase already paid
// to remove.
public class ExecuteCapabilityRoutingTests
{
    private const string DeviceId = "device-1";

    // ---- unknown capability ---------------------------------------------
    [Fact]
    public async Task AnUnknownCapabilityIsRejectedAndPublishesNothing()
    {
        var h = new Harness(new FakeCapability("camera.capture", CapabilityStatus.Running));

        var result = await h.ExecuteAsync("does.not.exist");

        Assert.Equal("CAPABILITY_NOT_FOUND", result.ErrorCode);
        Assert.Empty(h.Dispatcher.Published);
    }

    // ---- registered but not running --------------------------------------
    // Distinct from NOT_FOUND on purpose: "I don't have that" and "I have it
    // but it isn't running" send an operator to different places.
    [Theory]
    [InlineData(CapabilityStatus.Registered)]
    [InlineData(CapabilityStatus.Stopped)]
    [InlineData(CapabilityStatus.Failed)]
    public async Task ACapabilityThatIsNotRunningIsRejected(CapabilityStatus status)
    {
        var h = new Harness(new FakeCapability("camera.capture", status));

        var result = await h.ExecuteAsync("camera.capture");

        Assert.Equal("CAPABILITY_UNAVAILABLE", result.ErrorCode);
        Assert.Empty(h.Dispatcher.Published);
    }

    // ---- declares nothing invokable --------------------------------------
    // The closest check this protocol supports. ExecuteCapabilityRequest
    // carries no command name, so the runtime cannot tell camera.capture
    // from camera.delete within one capability - it can only ask whether
    // the capability advertises anything at all.
    [Fact]
    public async Task ACapabilityDeclaringNoCommandsIsRejected()
    {
        var h = new Harness(
            new FakeCapability("camera.capture", CapabilityStatus.Running, commands: []));

        var result = await h.ExecuteAsync("camera.capture");

        Assert.Equal("CAPABILITY_NOT_EXECUTABLE", result.ErrorCode);
        Assert.Empty(h.Dispatcher.Published);
    }

    // ---- ordering ---------------------------------------------------------
    // Both invalid: the capability error must win. Otherwise a command
    // naming a capability this Agent does not have reports something about
    // cameras, which sends the reader looking in the wrong place entirely.
    [Fact]
    public async Task CapabilityValidationRunsBeforeDeviceValidation()
    {
        var h = new Harness(new FakeCapability("camera.capture", CapabilityStatus.Running));
        h.Devices.Known.Clear();

        var result = await h.ExecuteAsync("does.not.exist", deviceId: "also.does.not.exist");

        Assert.Equal("CAPABILITY_NOT_FOUND", result.ErrorCode);
        Assert.NotEqual("DEVICE_NOT_FOUND", result.ErrorCode);
    }

    // ---- the happy path ---------------------------------------------------
    [Fact]
    public async Task AValidCommandPublishesExactlyOneDeviceTriggeredEvent()
    {
        var h = new Harness(new FakeCapability("camera.capture", CapabilityStatus.Running));

        var result = await h.ExecuteAsync("camera.capture");

        Assert.Null(result.ErrorCode);

        var published = Assert.Single(h.Dispatcher.Published);
        Assert.Equal(DeviceId, published.DeviceId);
        Assert.Equal("Command", published.Reason);
    }

    // ---- routing must not execute -----------------------------------------
    // Structural, and deliberately so: the guarantee is about what the
    // handler is ALLOWED to depend on, not about what one code path happened
    // to do. If someone injects a capture executor or service to "just run
    // it here", this fails immediately.
    [Fact]
    public void TheHandlerCannotExecuteACaptureItself()
    {
        var dependencies = typeof(ExecuteCapabilityCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain("ICameraCaptureExecutor", dependencies);
        Assert.DoesNotContain("ICameraCaptureService", dependencies);
        Assert.DoesNotContain("CameraCaptureWorker", dependencies);
    }

    // ---- capability independence ------------------------------------------
    // The old handler branched on a hard-coded ImageCapture literal. The
    // same routing code must now serve a capability it has never heard of -
    // this one is not camera-related at all and needs no production change.
    [Fact]
    public async Task TheSameRoutingServesAnyCapability()
    {
        var h = new Harness(
            new FakeCapability("motion.sensor", CapabilityStatus.Running),
            new FakeCapability("smartplug.monitor", CapabilityStatus.Running));

        Assert.Null((await h.ExecuteAsync("motion.sensor")).ErrorCode);
        Assert.Null((await h.ExecuteAsync("smartplug.monitor")).ErrorCode);
        Assert.Equal(2, h.Dispatcher.Published.Count);
    }

    // =======================================================================

    private sealed class Harness
    {
        public RecordingDispatcher Dispatcher { get; } = new();
        public StubDeviceRuntimeStore Devices { get; } = new();
        public ExecuteCapabilityCommandHandler Handler { get; }

        public Harness(params ICapability[] capabilities)
        {
            Handler = new ExecuteCapabilityCommandHandler(
                Devices,
                new CapabilityRegistry(capabilities),
                Dispatcher,
                NullLogger<ExecuteCapabilityCommandHandler>.Instance);
        }

        public Task<CommandHandlerResult> ExecuteAsync(
            string capabilityKey, string deviceId = DeviceId) =>
            Handler.HandleAsync(
                new AgentCommandDetails(
                    CommandId: "command-1",
                    TargetAgentId: "agent-1",
                    TargetDeviceId: deviceId,
                    CapabilityKey: capabilityKey,
                    CommandType: AgentCommandTypes.ExecuteCapability,
                    Payload: null,
                    Status: "Dispatched",
                    ExpiresUtc: DateTime.UtcNow.AddMinutes(5)),
                CancellationToken.None);
    }

    private sealed class FakeCapability(
        string id,
        CapabilityStatus status,
        IReadOnlyCollection<CapabilityCommandDescriptor>? commands = null) : ICapability
    {
        public CapabilityStatus Status => status;

        public CapabilityManifest Manifest => new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            Commands = commands ?? [new CapabilityCommandDescriptor(id, "1.0")]
        };

        public Task StartAsync(ICapabilityContext context, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingDispatcher : IEventDispatcher
    {
        public List<DeviceTriggeredEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is DeviceTriggeredEvent triggered)
                Published.Add(triggered);

            return Task.CompletedTask;
        }
    }

    private sealed class StubDeviceRuntimeStore : IDeviceRuntimeStore
    {
        public Dictionary<string, DeviceOptions> Known { get; } = new()
        {
            [DeviceId] = new DeviceOptions { DeviceId = DeviceId, Name = "Kitchen Camera" }
        };

        public DeviceOptions GetDevice(string deviceId, Core.Enums.DeviceType type) =>
            Known.TryGetValue(deviceId, out var d)
                ? d
                : throw new KeyNotFoundException(deviceId);

        public IReadOnlyCollection<DeviceOptions> GetDevices(string id) =>
            Known.TryGetValue(id, out var d) ? [d] : [];

        public IReadOnlyCollection<DeviceOptions> GetDevices() => Known.Values.ToList();
    }
}
