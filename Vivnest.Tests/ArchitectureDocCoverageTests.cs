using System.Text.RegularExpressions;

namespace Vivnest.Tests;

// Fails the build when a queue trigger, a hosted service, or a configured
// table exists in code but is named nowhere in current-architecture.md.
//
// Why this exists: on 2026-08-20 an entire feature - a new Agent worker, a
// new queue, a new Cloud function, a new table and a new notification type -
// shipped, was deployed, was verified against live Azure, and did not appear
// anywhere in the document whose stated job is to describe what exists. The
// ADR and the roadmap were updated; this file was forgotten. CLAUDE.md's
// rule is to update the docs in the same change that invalidates them, and
// a rule that depends on remembering is not a rule.
//
// It found one more the day it was written: tblDeviceSnapshotState had never
// been documented, and a careful manual pass over the same three inventories
// had missed it an hour earlier.
//
// **Scope is deliberately narrow.** This checks that a name is MENTIONED,
// not that what is written about it is true or current. It cannot tell you
// the description is stale, only that something is entirely absent. That is
// the failure mode worth automating because it is invisible - a wrong
// sentence is at least visible to a reader, a missing one is not.
//
// If a name legitimately does not belong in the architecture doc, add it to
// the exclusions below with a reason, rather than weakening the check.
public class ArchitectureDocCoverageTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Doc =
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "architecture", "current-architecture.md"));

    // Nothing is excluded today. Kept as the documented escape hatch so the
    // next person weakens this list rather than the assertions.
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal);

    [Fact]
    public void EveryQueueTriggerIsMentionedInTheArchitectureDoc()
    {
        var queues = ScanSources(@"QueueTrigger\(""([a-z0-9-]+)""");

        Assert.NotEmpty(queues); // the scan itself must not silently find nothing

        AssertAllMentioned(queues, "queue",
            "A queue trigger is a new inbound path into Cloud. If it is not in the "
            + "architecture doc, nobody reading that doc knows the path exists.");
    }

    [Fact]
    public void EveryHostedServiceIsMentionedInTheArchitectureDoc()
    {
        // Scans the whole Agent project, not Program.cs alone. The 2026-08-22
        // refactor moved every AddHostedService call into Bootstrap/*.cs and
        // this test went to zero matches - caught only by the NotEmpty guard
        // below, which is the entire reason that guard is here. A test that
        // silently checks an empty set is worse than no test.
        var workers = Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot, "Vivnest.Agent"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"AddHostedService<\s*([A-Za-z0-9_]+)\s*>")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(workers);

        AssertAllMentioned(workers, "hosted service",
            "A hosted service is something the Agent runs continuously. An undocumented "
            + "one is invisible work with its own failure modes.");
    }

    // TablesOptions property X maps to a table conventionally named tblX -
    // every entry in common-config/app settings follows that. Checking for
    // the tbl-prefixed name rather than the property name is the stricter
    // test: the doc talks about tables, not about the options class.
    [Fact]
    public void EveryConfiguredTableIsMentionedInTheArchitectureDoc()
    {
        var options = File.ReadAllText(
            Path.Combine(RepoRoot, "Vivnest.Core", "Options", "TablesOptions.cs"));

        var tables = Regex.Matches(options, @"public string ([A-Za-z0-9_]+) \{")
            .Select(m => "tbl" + m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(tables);

        AssertAllMentioned(tables, "table",
            "A table nobody documented is a store whose purpose and partitioning "
            + "have to be reverse-engineered from code.");
    }

    private static void AssertAllMentioned(
        IEnumerable<string> names, string kind, string why)
    {
        var missing = names
            .Where(n => !Excluded.Contains(n))
            .Where(n => !Doc.Contains(n, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These {kind}(s) exist in code but are named nowhere in "
            + $"docs/architecture/current-architecture.md:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Select(m => "  - " + m))
            + $"{Environment.NewLine}{Environment.NewLine}{why}{Environment.NewLine}"
            + "Document it in the same change that added it, or add it to Excluded "
            + "with a reason.");
    }

    private static HashSet<string> ScanSources(string pattern)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var regex = new Regex(pattern);

        foreach (var file in Directory.EnumerateFiles(RepoRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');

            if (normalized.Contains("/bin/") || normalized.Contains("/obj/")
                || normalized.Contains("/node_modules/"))
            {
                continue;
            }

            foreach (Match match in regex.Matches(File.ReadAllText(file)))
                found.Add(match.Groups[1].Value);
        }

        return found;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vivnest.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (Vivnest.slnx).");
    }
}
