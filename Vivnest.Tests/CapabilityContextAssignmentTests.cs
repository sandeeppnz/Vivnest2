using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Abstraction.Agent.Capabilities;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Tests;

// ADR-101. CapabilityHost used to select with .Where(... .Any(...)) - a
// predicate that answers "does an assignment exist?" and discards which
// one - then start every capability with a single shared ICapabilityContext
// singleton. These tests pin the two properties that made that wrong:
// each capability gets ITS OWN assignment, and the pairing is never crossed.
public class CapabilityContextAssignmentTests
{
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:AgentId"] = "agent-1",
                ["Agent:TenantId"] = "tenant-1",
                ["Agent:SiteId"] = "site-1"
            })
            .Build();

    private static RuntimeCapabilityAssignment Assignment(
        string id, params (string Key, string Value)[] settings) =>
        new()
        {
            CapabilityId = id,
            CapabilityName = id,
            Enabled = true,
            Settings = settings.ToDictionary(x => x.Key, x => x.Value)
        };

    private static CapabilityHost Host(
        IEnumerable<ICapability> capabilities,
        IEnumerable<RuntimeCapabilityAssignment> assignments) =>
        new(new CapabilityRegistry(capabilities),
            Configuration(),
            services: null!,
            new RuntimeCapabilityAssignmentStore(assignments),
            NullLogger<CapabilityHost>.Instance);

    [Fact]
    public async Task ACapabilityReceivesItsOwnAssignment()
    {
        var camera = new RecordingCapability("camera.capture");

        await Host([camera], [Assignment("camera.capture")])
            .StartAsync(CancellationToken.None);

        Assert.NotNull(camera.SeenContext);
        Assert.Equal("camera.capture", camera.SeenContext!.Assignment.CapabilityId);
        Assert.True(camera.SeenContext.Assignment.Enabled);
        Assert.Equal("agent-1", camera.SeenContext.AgentId);
    }

    [Fact]
    public async Task SettingsTravelOnTheAssignment()
    {
        var camera = new RecordingCapability("camera.capture");

        await Host([camera], [Assignment("camera.capture", ("SomeKey", "SomeValue"))])
            .StartAsync(CancellationToken.None);

        Assert.Equal("SomeValue", camera.SeenContext!.Assignment.Settings["SomeKey"]);
    }

    // The selection boundary, from the other side: Enabled=false must stop
    // the capability before StartAsync, not inside it. A capability that
    // starts and then decides it is disabled has already taken whatever
    // action startup implies - for camera.capture, a capture.
    [Fact]
    public async Task ADisabledAssignmentNeverStartsTheCapability()
    {
        var camera = new RecordingCapability("camera.capture");

        var disabled = Assignment("camera.capture") with { Enabled = false };

        await Host([camera], [disabled]).StartAsync(CancellationToken.None);

        Assert.Null(camera.SeenContext);
        Assert.Equal(CapabilityStatus.Registered, camera.Status);
    }

    // Transport fidelity: whatever Cloud resolved arrives byte-for-byte.
    // Deliberately asserts the COUNT as well as the values - a settings map
    // that silently gained or lost a key in transit would still satisfy
    // per-key assertions.
    [Fact]
    public async Task SettingsArePreservedExactly()
    {
        var camera = new RecordingCapability("camera.capture");

        await Host(
                [camera],
                [Assignment("camera.capture",
                    ("CaptureSomething", "test"),
                    ("AnotherValue", "123"))])
            .StartAsync(CancellationToken.None);

        var settings = camera.SeenContext!.Assignment.Settings;

        Assert.Equal(2, settings.Count);
        Assert.Equal("test", settings["CaptureSomething"]);
        Assert.Equal("123", settings["AnotherValue"]);
    }

    // The reason the context stopped being a singleton. With one shared
    // instance this is unrepresentable: two capabilities cannot both read
    // their own assignment off the same object.
    [Fact]
    public async Task TwoCapabilitiesGetDifferentAssignments()
    {
        var camera = new RecordingCapability("camera.capture");
        var motion = new RecordingCapability("motion.sensor");

        await Host(
                [camera, motion],
                [Assignment("camera.capture", ("Who", "camera")),
                 Assignment("motion.sensor", ("Who", "motion"))])
            .StartAsync(CancellationToken.None);

        Assert.Equal("camera.capture", camera.SeenContext!.Assignment.CapabilityId);
        Assert.Equal("motion.sensor", motion.SeenContext!.Assignment.CapabilityId);

        Assert.Equal("camera", camera.SeenContext.Assignment.Settings["Who"]);
        Assert.Equal("motion", motion.SeenContext.Assignment.Settings["Who"]);

        // Separate context instances, not one object mutated between calls.
        Assert.NotSame(camera.SeenContext, motion.SeenContext);
    }

    // The invariant, asserted for every started capability rather than
    // trusted: a crossed pairing would look like a capability behaving as
    // if configured by the wrong entry, which is near-invisible at runtime.
    [Fact]
    public async Task ManifestIdAlwaysMatchesTheAssignmentId()
    {
        var caps = new[]
        {
            new RecordingCapability("camera.capture"),
            new RecordingCapability("motion.sensor"),
            new RecordingCapability("smartplug.monitor")
        };

        await Host(
                caps,
                caps.Select(c => Assignment(c.Manifest.Id)).ToList())
            .StartAsync(CancellationToken.None);

        foreach (var c in caps)
        {
            Assert.Equal(
                c.Manifest.Id,
                c.SeenContext!.Assignment.CapabilityId);
        }
    }

    // A registered capability with no enabled assignment is not started, so
    // it never receives a context at all - the selection change must not
    // have loosened that.
    [Fact]
    public async Task AnUnassignedCapabilityIsNeverGivenAContext()
    {
        var camera = new RecordingCapability("camera.capture");
        var motion = new RecordingCapability("motion.sensor");

        await Host([camera, motion], [Assignment("camera.capture")])
            .StartAsync(CancellationToken.None);

        Assert.NotNull(camera.SeenContext);
        Assert.Null(motion.SeenContext);
    }

    private sealed class RecordingCapability(string id) : ICapability
    {
        public ICapabilityContext? SeenContext { get; private set; }

        public CapabilityStatus Status { get; private set; } = CapabilityStatus.Registered;

        public CapabilityManifest Manifest => new() { Id = id, Name = id, Version = "1.0.0" };

        public Task StartAsync(ICapabilityContext context, CancellationToken ct)
        {
            SeenContext = context;
            Status = CapabilityStatus.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            Status = CapabilityStatus.Stopped;
            return Task.CompletedTask;
        }
    }
}
