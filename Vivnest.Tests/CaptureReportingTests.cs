using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vivnest.Capabilities.Camera;
using Vivnest.Capabilities.MotionSensor;
using Vivnest.Core.Devices.Camera.Models;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Events;
using Vivnest.Core.Devices.MotionSensor.Models;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Runtime.State;

namespace Vivnest.Tests;

// Three defects from the 2026-08-24 review of Vivnest.Capabilities, all
// about what a capability REPORTS rather than what it does.
public class CaptureReportingTests
{
    // ---- a handler failure is not a capture failure ----------------------

    // The bug: CameraCaptureExecutor published
    // CameraCaptureCompletedEvent inside its own catch, so a handler that
    // threw - CameraCaptureHandler rethrows when the DeviceEvent cannot be
    // persisted - was recorded as a capture failure. The photo had already
    // been taken and uploaded; a storage problem was reported as a camera
    // problem.
    [Fact]
    public async Task AHandlerThatThrowsDoesNotBecomeACaptureFailure()
    {
        var dispatcher = new RecordingDispatcher
        {
            ThrowOn = typeof(CameraCaptureCompletedEvent)
        };

        var log = new RecordingLogger<CameraCaptureExecutor>();
        var runtime = new DeviceRuntimeState();

        var executor = new CameraCaptureExecutor(
            new StubCaptureService(),
            dispatcher,
            Options.Create(new AgentOptions { AgentId = "agent-1" }),
            log);

        await executor.CaptureAsync(Camera(), runtime, CancellationToken.None);

        // The failure event is the thing that reaches Cloud as Critical and
        // makes the device look broken.
        Assert.Empty(dispatcher.Events.OfType<CameraCaptureFailedEvent>());

        // And the capture's own state must still read as the success it was.
        Assert.Null(runtime.LastError);
        Assert.Null(runtime.LastFailureUtc);
        Assert.NotNull(runtime.LastCaptureUtc);
    }

    // "Not a capture failure" must not mean "not reported at all".
    [Fact]
    public async Task ThatHandlerFailureIsStillLogged()
    {
        var dispatcher = new RecordingDispatcher
        {
            ThrowOn = typeof(CameraCaptureCompletedEvent)
        };

        var log = new RecordingLogger<CameraCaptureExecutor>();

        var executor = new CameraCaptureExecutor(
            new StubCaptureService(),
            dispatcher,
            Options.Create(new AgentOptions { AgentId = "agent-1" }),
            log);

        await executor.CaptureAsync(Camera(), new DeviceRuntimeState(), CancellationToken.None);

        Assert.NotEmpty(log.Errors);
    }

    // A capture that genuinely fails must still report a failure - the fix
    // above must not have swallowed the real path too.
    [Fact]
    public async Task ARealCaptureFailureStillPublishesTheFailureEvent()
    {
        var dispatcher = new RecordingDispatcher();
        var runtime = new DeviceRuntimeState();

        var executor = new CameraCaptureExecutor(
            new StubCaptureService { Fail = true },
            dispatcher,
            Options.Create(new AgentOptions { AgentId = "agent-1" }),
            new RecordingLogger<CameraCaptureExecutor>());

        await executor.CaptureAsync(Camera(), runtime, CancellationToken.None);

        Assert.Single(dispatcher.Events.OfType<CameraCaptureFailedEvent>());
        Assert.NotNull(runtime.LastError);
    }

    // ---- the payload shape -----------------------------------------------

    // The bug: these two handlers assigned Data =
    // JsonSerializer.Serialize(...), a string, while DeviceEvent.Data is
    // object? and AzureTableDeviceEventWriter serialises it - so the stored
    // payload was a JSON string CONTAINING JSON. Asserted the way a real
    // consumer reads it, since that is what would have broken:
    // DeviceEventQueueHandler parses payloads exactly like this.
    [Fact]
    public async Task ACameraCaptureFailurePayloadIsAnObjectNotAnEncodedString()
    {
        var writer = new CapturingDeviceEventWriter();

        var handler = new CameraCaptureFailedHandler(
            writer,
            new NullQueuePublisher(),
            Options.Create(new MessagingOptions()),
            Options.Create(new AgentOptions { AgentId = "agent-1" }),
            NullLogger<CameraCaptureFailedHandler>.Instance);

        await handler.HandleAsync(
            new CameraCaptureFailedEvent(
                new CameraCaptureFailureData(
                    "agent-1", "camera-0", DateTime.UtcNow, "RTSP_TIMEOUT", "no route to host")),
            CancellationToken.None);

        AssertPayloadReadsAsAnObject(writer.Saved!, "RTSP_TIMEOUT");
    }

    [Fact]
    public async Task AMotionSensorFailurePayloadIsAnObjectNotAnEncodedString()
    {
        var writer = new CapturingDeviceEventWriter();

        var handler = new MotionSensorReadingFailedHandler(
            writer,
            Options.Create(new AgentOptions { AgentId = "agent-1" }),
            NullLogger<MotionSensorReadingFailedHandler>.Instance);

        await handler.HandleAsync(
            new MotionSensorReadingFailedEvent(
                new MotionSensorReadingFailureData(
                    "agent-1", "sensor-0", DateTime.UtcNow, "HUB_UNREACHABLE", "control_child failed")),
            CancellationToken.None);

        AssertPayloadReadsAsAnObject(writer.Saved!, "HUB_UNREACHABLE");
    }

    // ---- an absent capability is not a capability ------------------------

    // The bug: ClassifyCapability.SinkCleanliness is the zero value, so a
    // message whose Capability field was missing deserialised to it and the
    // High-type agent quietly ran a sink-cleanliness classification instead
    // of discarding the message. Capability is nullable now, so absent
    // reads as absent and falls to the worker's discard branch.
    [Fact]
    public void AClassifyMessageWithNoCapabilityDeserialisesToNull()
    {
        var json = """
            {"AgentId":"ai-1","OriginAgentId":"pi-1","OriginTenantId":"t","OriginSiteId":"s",
             "DeviceId":"camera-0","BlobContainer":"c","BlobName":"b.jpg",
             "CapturedAtUtc":"2026-08-24T00:00:00Z","IssuedAtUtc":"2026-08-24T00:00:00Z"}
            """;

        var message = JsonSerializer.Deserialize<ClassifyCaptureQueueMessage>(json);

        Assert.NotNull(message);
        Assert.Null(message.Capability);
    }

    // The wire values must not move. AzureQueuePublisher serialises with no
    // JsonSerializerOptions, so these travel as numbers - renumbering them
    // to make room for an "Unknown" member would misroute every message
    // already sitting on classify-requests or classify-commands.
    [Theory]
    [InlineData(0, ClassifyCapability.SinkCleanliness)]
    [InlineData(1, ClassifyCapability.ObjectDetection)]
    public void TheCapabilityWireValuesAreStable(int wire, ClassifyCapability expected)
    {
        Assert.Equal(expected, (ClassifyCapability)wire);
        Assert.Equal(wire.ToString(), JsonSerializer.Serialize(expected));
    }

    // =======================================================================

    private static void AssertPayloadReadsAsAnObject(DeviceEvent saved, string expectedErrorCode)
    {
        // Exactly what AzureTableDeviceEventWriter stores.
        var payload = JsonSerializer.Serialize(saved.Data);

        using var doc = JsonDocument.Parse(payload);

        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        Assert.Equal(
            expectedErrorCode,
            doc.RootElement.GetProperty("ErrorCode").GetString());
    }

    private static DeviceOptions Camera() => new()
    {
        DeviceId = "camera-0",
        Name = "Camera 0",
        Type = DeviceType.Camera,
        Enabled = true
    };

    private sealed class StubCaptureService : ICameraCaptureService
    {
        public bool Fail { get; set; }

        public Task<CameraCaptureResult> CaptureAsync(
            DeviceOptions cameraOptions, CancellationToken cancellationToken) =>
            Task.FromResult(Fail
                ? new CameraCaptureResult
                {
                    Success = false,
                    DeviceId = cameraOptions.DeviceId,
                    CapturedAtUtc = DateTime.UtcNow,
                    Error = "camera unreachable"
                }
                : new CameraCaptureResult
                {
                    Success = true,
                    DeviceId = cameraOptions.DeviceId,
                    CapturedAtUtc = DateTime.UtcNow,
                    BlobName = "b.jpg",
                    BlobContainer = "c"
                });

        public Task<bool> CheckReachabilityAsync(
            DeviceOptions cameraOptions, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class RecordingDispatcher : IEventDispatcher
    {
        public List<object> Events { get; } = [];
        public Type? ThrowOn { get; set; }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default)
        {
            Events.Add(@event!);

            if (ThrowOn == typeof(TEvent))
            {
                throw new AggregateException(
                    $"One or more handlers failed while handling {typeof(TEvent).Name}.",
                    new InvalidOperationException("the table is unavailable"));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CapturingDeviceEventWriter : IDeviceEventWriter
    {
        public DeviceEvent? Saved { get; private set; }

        public Task<DeviceEventEntity?> SaveAsync(
            DeviceEvent deviceEvent,
            CancellationToken cancellationToken = default)
        {
            Saved = deviceEvent;

            return Task.FromResult<DeviceEventEntity?>(new DeviceEventEntity
            {
                PartitionKey = "tenant-1|site-1",
                RowKey = "row-1",
                TenantId = "tenant-1",
                SiteId = "site-1",
                AgentId = "agent-1"
            });
        }
    }

    private sealed class NullQueuePublisher : IQueuePublisher
    {
        public Task PublishAsync<T>(
            string queueName, T message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
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
}
