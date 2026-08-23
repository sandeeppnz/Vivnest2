using System.Text.RegularExpressions;

namespace Vivnest.Tests;

// Fails the build when an ADR number is cited somewhere in the repository
// but has no entry in decision-log.md, or when one number has two entries.
//
// Why this exists: both failure modes happened, two days apart, and neither
// was visible from the code.
//
// On 2026-08-23 ADR-091 turned out to name two unrelated decisions added in
// the same commit - the Updater's --credentialencryptionkey flag and
// tenant/site-scoped configuration blob names - so a reader following a
// reference could land on either. The smaller side was renumbered to 106.
//
// On 2026-08-24 the same thing happened again, to the number that had just
// been handed out: ADR-106 was cited for the Abstraction-into-Core
// restructure while it already meant the Updater flag. In the same pass,
// ADR-107 through ADR-110 were found to be cited in twenty places - code
// comments, three .csproj files, CLAUDE.md - with no entry written for any
// of them. The numbers had been referenced as each change landed and the
// entries never appended.
//
// Neither was caught by reading, and the second was initially misdiagnosed
// in the other direction: a check that sorted headings by line number
// reported ADR-106 as missing, because it deliberately sits out of numeric
// sequence where it belongs chronologically. Sort by number, not position.
//
// **Scope is deliberately narrow.** This checks that a cited number
// RESOLVES and that it resolves to exactly one entry. It says nothing about
// whether the entry is accurate, current, or about the subject the citation
// meant. Those need a reader. This catches the case a reader cannot see.
public class DecisionLogIntegrityTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string DecisionLog =
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "architecture", "decision-log.md"));

    private static readonly string[] ScannedExtensions =
        [".cs", ".csproj", ".md", ".ps1"];

    [Fact]
    public void EveryCitedAdrHasAnEntryInTheDecisionLog()
    {
        var entries = AdrEntryNumbers();
        var citations = AdrCitations();

        Assert.NotEmpty(entries);   // the scans themselves must not
        Assert.NotEmpty(citations); // silently find nothing

        var dangling = citations
            .Where(c => !entries.Contains(c.Key))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            dangling.Count == 0,
            "These ADR numbers are cited but have no '## ADR-NNN' entry in "
            + $"docs/architecture/decision-log.md:{Environment.NewLine}"
            + string.Join(
                Environment.NewLine,
                dangling.Select(d =>
                    $"  - ADR-{d.Key} cited in: {string.Join(", ", d.Value.Order(StringComparer.Ordinal))}"))
            + $"{Environment.NewLine}{Environment.NewLine}"
            + "Write the entry in the same change that cites it. Citing a "
            + "number as work lands and appending the entry later is exactly "
            + "how five of these went missing at once.");
    }

    [Fact]
    public void NoAdrNumberNamesTwoDecisions()
    {
        var duplicates = Regex
            .Matches(DecisionLog, @"^## ADR-(\d{3})", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "These ADR numbers have more than one entry, so a reference to "
            + $"them is ambiguous:{Environment.NewLine}"
            + string.Join(Environment.NewLine, duplicates.Select(d => "  - ADR-" + d))
            + $"{Environment.NewLine}{Environment.NewLine}"
            + "Renumber the side with fewer references, leave the entry where "
            + "it sits in the file so the log still reads in decision order, "
            + "and give both entries a note - references written before the "
            + "split may mean either. See ADR-091/ADR-106 for the precedent.");
    }

    // Number -> the files citing it. Keyed by the three-digit string so the
    // comparison never depends on parsing or zero-padding.
    private static Dictionary<string, List<string>> AdrCitations()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var regex = new Regex(@"ADR-(\d{3})");

        foreach (var file in EnumerateSources())
        {
            foreach (Match match in regex.Matches(File.ReadAllText(file)))
            {
                var number = match.Groups[1].Value;
                var relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');

                if (!found.TryGetValue(number, out var files))
                {
                    found[number] = files = [];
                }

                if (!files.Contains(relative, StringComparer.Ordinal))
                {
                    files.Add(relative);
                }
            }
        }

        return found;
    }

    private static HashSet<string> AdrEntryNumbers() =>
        Regex
            .Matches(DecisionLog, @"^## ADR-(\d{3})", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateSources() =>
        Directory
            .EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories)
            .Where(f => ScannedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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
