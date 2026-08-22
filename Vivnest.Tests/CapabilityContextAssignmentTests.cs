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
