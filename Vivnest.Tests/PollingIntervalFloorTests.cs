using System.Text.RegularExpressions;
using Vivnest.Agent.Configuration;
using Vivnest.Core.Options;

namespace Vivnest.Tests;

// An interval that binds to TimeSpan.Zero is the worst kind of
// misconfiguration: it produces no exception and no log line, and the two
// ways this codebase sleeps react to it differently, both badly.
//
//   Task.Delay(TimeSpan.Zero)       returns immediately - the loop becomes
//                                   a hot spin that pins a core, re-probes
//                                   the device continuously and writes to
//                                   storage as fast as it is allowed to.
//                                   On a Raspberry Pi that is a thermal
//                                   and cost event.
//
//   new PeriodicTimer(TimeSpan.Zero) throws ArgumentOutOfRangeException,
//                                   which faults the BackgroundService and
//                                   stops the entire host on .NET 8.
//
// Three of these settings have no default at all, so simply omitting them
// from a published config is enough. TapoHubLivenessWorker carried an
// inline guard against this from the start and no other loop did.
public class PollingIntervalFloorTests
{
    // ---- the floors themselves -------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void ADeviceWithNoLivenessIntervalFallsBackRatherThanSpinning(int seconds)
    {
        var device = new DeviceOptions
        {
            DeviceId = "device-0",
            LivenessInterval = TimeSpan.FromSeconds(seconds)
        };

        Assert.Equal(DeviceOptions.DefaultLivenessInterval, device.EffectiveLivenessInterval);
        Assert.True(device.EffectiveLivenessInterval > TimeSpan.Zero);
    }

    [Fact]
    public void AConfiguredLivenessIntervalIsUsedUnchanged()
    {
        var device = new DeviceOptions
        {
            DeviceId = "device-0",
            LivenessInterval = TimeSpan.FromSeconds(45)
        };

        Assert.Equal(TimeSpan.FromSeconds(45), device.EffectiveLivenessInterval);
    }

    [Fact]
    public void EveryAgentIntervalHasAPositiveFloorAndPassesRealValuesThrough()
    {
        // Unset - the case that reaches PeriodicTimer and stops the host.
        Assert.True(new AgentHeartbeatOptions().EffectiveHeartbeatInterval > TimeSpan.Zero);
        Assert.True(new DeviceHeartbeatOptions().EffectiveHeartbeatInterval > TimeSpan.Zero);

        // These two have defaults, so only an explicit zero can reach it.
        Assert.True(
            new AgentMetricsOptions { Interval = TimeSpan.Zero }.EffectiveInterval > TimeSpan.Zero);
        Assert.True(
            new AgentLogShippingOptions { FlushInterval = TimeSpan.Zero }.EffectiveFlushInterval > TimeSpan.Zero);

        // A real value must survive untouched, or the floor is a bug of its own.
        Assert.Equal(
            TimeSpan.FromSeconds(20),
            new AgentHeartbeatOptions { HeartbeatInterval = TimeSpan.FromSeconds(20) }
                .EffectiveHeartbeatInterval);

        Assert.Equal(
            TimeSpan.FromSeconds(20),
            new DeviceHeartbeatOptions { HeartbeatInterval = TimeSpan.FromSeconds(20) }
                .EffectiveHeartbeatInterval);
    }

    // ---- and that the loops actually use them ----------------------------

    // The floors are only worth anything if every loop goes through them,
    // and the next worker someone writes is the one at risk. This is a
    // tripwire, not a proof: it catches a delay taken straight from a raw
    // interval property, which is the exact shape all four loops had
    // before 2026-08-24. It cannot catch a loop that launders the raw
    // value through a local first.
    [Fact]
    public void NoLoopSleepsOnARawIntervalProperty()
    {
        var offenders = new List<string>();

        var raw = new Regex(
            @"(?:Task\.Delay\(|new PeriodicTimer\()\s*\n?\s*[_\w.]*\.(?:LivenessInterval|HeartbeatInterval|FlushInterval|PollInterval)\b");

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            if (raw.IsMatch(text))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These files sleep on a raw interval that can be zero:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => "  - " + o))
            + Environment.NewLine + Environment.NewLine
            + "Use the Effective* property instead. A zero interval either spins "
            + "the loop at 100% CPU (Task.Delay) or stops the whole host "
            + "(PeriodicTimer), and three of these settings have no default.");
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = FindRepoRoot();

        // Vivnest.Agent.Updater is scanned too since ADR-117 - its
        // Deploy:PollInterval had exactly this bug class while sitting
        // outside this net.
        foreach (var project in new[] { "Vivnest.Agent", "Vivnest.Capabilities", "Vivnest.Agent.Updater" })
        {
            var directory = Path.Combine(root, project);

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
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
}
