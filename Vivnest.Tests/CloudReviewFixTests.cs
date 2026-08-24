using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.CapabilityProjection;
using Vivnest.Capabilities.Bridges.TapoHub;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Hubs;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;
using Vivnest.Runtime.State;
using Vivnest.Cloud.Entities;

namespace Vivnest.Tests;

// Defects from the 2026-08-24 review of Vivnest.Cloud (plus one Agent-side
// half, the hub alerting gap, whose fix lives in Vivnest.Capabilities).
public class CloudReviewFixTests
{
    // ---- range bounds are invariant, and built in exactly one place ------

    // The write side (EventRowKey.For) was fixed for this long ago, with an
    // essay; both READERS still hand-formatted the same string with a
    // culture-sensitive ToString. Under th-TH the default calendar is
    // Buddhist, so 2026 renders as 2569 - a range bound matching no row
    // ever written. Asserted under that exact culture.
    [Fact]
    public void RangeBoundsUseTheGregorianYearUnderABuddhistCalendarCulture()
    {
        RunUnder(new CultureInfo("th-TH"), () =>
        {
            var bound = EventRowKey.RangeBound(new DateTime(2026, 8, 24, 1, 2, 3, DateTimeKind.Utc));

            Assert.StartsWith("20260824", bound);
        });
    }

    // The bound must range-match real rows: a For() key of the same instant
    // starts with exactly the RangeBound of that instant.
    [Fact]
    public void ARangeBoundPrefixesTheRowKeysItMustMatch()
    {
        var at = new DateTime(2026, 8, 24, 1, 2, 3, 456, DateTimeKind.Utc);

        Assert.StartsWith(EventRowKey.RangeBound(at), EventRowKey.For(at, Guid.NewGuid()));
    }

    // The tripwire: nothing outside EventRowKey may hand-write the format.
    // Both readers had, verbatim - which is the drift EventRowKey's own
    // comment predicts.
    [Fact]
    public void NothingOutsideEventRowKeyHandWritesTheTimestampFormat()
    {
        var offenders = new List<string>();
        var root = FindRepoRoot();

        foreach (var project in Directory.EnumerateDirectories(root, "Vivnest.*"))
        {
            foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.EndsWith("EventRowKey.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                // Comment lines are stripped: the readers legitimately
                // DESCRIBE the format in prose next to their RangeBound
                // calls; only code that builds the string is a violation.
                // The needle is concatenated so this file never matches
                // itself.
                var code = File.ReadAllLines(file)
                    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));

                if (string.Join('\n', code).Contains("yyyyMMdd" + "HHmmssfff", StringComparison.Ordinal))
                    offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These files hand-write EventRowKey's timestamp format:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => "  - " + o))
            + Environment.NewLine
            + "Use EventRowKey.For / EventRowKey.RangeBound instead.");
    }

    // ---- malformed Settings fails the assignment, not the publish --------

    // The agent projector documents the policy ("Malformed JSON fails the
    // ASSIGNMENT, not the projection"); the capability projectors sat on
    // both publish paths and threw instead - one hand-edited row killed
    // every publish that touched it.
    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    public void AProjectorGivenMalformedSettingsWarnsInsteadOfThrowing(string settings)
    {
        var result = new ObjectDetectionRuntimeProjector().Project(
            Assignment(settings), Device(), "runtime-agent-1");

        Assert.NotEmpty(result.Warnings);
        Assert.Null(result.DeviceEntry);
        Assert.Null(result.AgentEntry);
    }

    [Fact]
    public void TheImageCaptureProjectorToleratesMalformedSettingsToo()
    {
        var result = new ImageCaptureRuntimeProjector().Project(
            Assignment("{not json"), Device(), null);

        Assert.NotEmpty(result.Warnings);
        Assert.Null(result.DeviceEntry);
    }

    // ---- Cloud-side validation speaks the same dialect as the Agent ------

    // The Agent parses wire numerics invariantly (the Core fix). A Cloud
    // validator running under de-DE used to accept "1,5" - which the Agent
    // then rejects - and read "1.5" while validating with the host's group
    // separator. Both directions pinned.
    [Fact]
    public void ValidationAcceptsDotDecimalsAndRejectsCommaDecimalsUnderAnyCulture()
    {
        RunUnder(new CultureInfo("de-DE"), () =>
        {
            var valid = new ImageCaptureRuntimeProjector().Project(
                Assignment("""
                    {"ScheduleIntervalSeconds":"1.5","BurstIntervalSeconds":"30","BurstDurationSeconds":"600"}
                    """),
                Device(),
                null);

            Assert.Empty(valid.Warnings);

            var commaDecimal = new ImageCaptureRuntimeProjector().Project(
                Assignment("""
                    {"ScheduleIntervalSeconds":"1,5","BurstIntervalSeconds":"30","BurstDurationSeconds":"600"}
                    """),
                Device(),
                null);

            Assert.NotEmpty(commaDecimal.Warnings);
        });
    }

    // ---- ADR-114: a dead hub finally reports an error --------------------

    [Fact]
    public async Task AHubUnreachableForThreeProbesReportsAnErrorAndRecoveryClearsIt()
    {
        var checker = new ScriptedReachability();
        var runtimeStates = new InMemoryRuntimeStateStore();

        var worker = new TapoHubLivenessWorker(
            checker,
            runtimeStates,
            new SingleHubStore(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TapoHubLivenessWorker>.Instance);

        using var cts = new CancellationTokenSource();

        checker.Reachable = false;

        await worker.StartAsync(cts.Token);

        var runtime = runtimeStates.GetOrAdd("hub-0");

        // The checker GATES each probe, so the loop cannot outrun the
        // assertions - the first cut of this test polled a free-running
        // 1ms loop and asserted "after 2 probes, no error yet", which
        // raced probe 3 under a loaded test host and failed only in the
        // full parallel run. With the gate, probe 3 cannot begin until
        // this test releases it, so "two failures are not enough" is a
        // fact, not a timing bet. Waits stay bounded so a dead loop
        // fails rather than hangs.
        checker.ReleaseProbe();
        checker.ReleaseProbe();
        await WaitUntilAsync(() => checker.Probes >= 2);
        Assert.Null(runtime.LastError);

        checker.ReleaseProbe();
        await WaitUntilAsync(() => runtime.LastError != null);
        Assert.Contains("unreachable", runtime.LastError, StringComparison.OrdinalIgnoreCase);

        // Recovery clears it - the hub has no other LastError writer, so
        // one good probe is proof.
        checker.Reachable = true;
        checker.ReleaseProbe();
        await WaitUntilAsync(() => runtime.LastError == null);

        await cts.CancelAsync();

        try
        {
            await worker.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- ADR-115: the configuration hash needs the key -------------------

    [Fact]
    public void TheConfigurationHashIsKeyedDeterministicAndKeyDependent()
    {
        var keyA = new byte[32];
        var keyB = new byte[32];
        keyB[0] = 1;

        var content = new { Name = "camera-0", Password = "hunter2" };

        var first = RuntimeConfigurationWriter<AgentConfigurationEntity>.ComputeHash(content, keyA);
        var second = RuntimeConfigurationWriter<AgentConfigurationEntity>.ComputeHash(content, keyA);
        var otherKey = RuntimeConfigurationWriter<AgentConfigurationEntity>.ComputeHash(content, keyB);

        // Deterministic per key - change detection still works.
        Assert.Equal(first, second);

        // But uncomputable without the key - no more offline dictionary
        // oracle against the plaintext credential in the hashed content.
        Assert.NotEqual(first, otherKey);
    }

    // =======================================================================

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not reached within 10s.");

            await Task.Delay(10);
        }
    }

    private static void RunUnder(CultureInfo culture, Action test)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;

            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Vivnest.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static DeviceCapabilityEntity Assignment(string settingsJson) =>
        new()
        {
            PartitionKey = "tenant-1|site-1",
            RowKey = "assignment-1",
            TenantId = "tenant-1",
            SiteId = "site-1",
            DeviceId = "device-1",
            CapabilityId = "cap-1",
            Status = "Active",
            Enabled = true,
            ExecutingAgentId = "admin-agent-1",
            Settings = settingsJson
        };

    private static DeviceRegistryEntity Device() =>
        new()
        {
            PartitionKey = "tenant-1|site-1",
            RowKey = "device-1",
            TenantId = "tenant-1",
            SiteId = "site-1",
            Name = "Kitchen Camera",
            RuntimeDeviceId = "runtime-device-1"
        };

    private sealed class ScriptedReachability : ITapoHubReachabilityChecker
    {
        private readonly SemaphoreSlim _gate = new(0);

        public volatile bool Reachable;
        public int Probes;

        public void ReleaseProbe() => _gate.Release();

        public async Task<bool> CheckReachabilityAsync(
            DeviceOptions hub, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);

            Interlocked.Increment(ref Probes);

            return Reachable;
        }
    }

    private sealed class SingleHubStore : IDeviceRuntimeStore
    {
        private readonly List<DeviceOptions> _devices =
        [
            new()
            {
                DeviceId = "hub-0",
                Name = "Hub 0",
                Type = DeviceType.Hub,
                Enabled = true,
                LivenessInterval = TimeSpan.FromMilliseconds(1)
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
            lock (_states)
            {
                if (!_states.TryGetValue(deviceId, out var state))
                    _states[deviceId] = state = new DeviceRuntimeState();

                return state;
            }
        }
    }
}
