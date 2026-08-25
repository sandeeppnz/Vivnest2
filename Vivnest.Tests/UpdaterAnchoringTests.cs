namespace Vivnest.Tests;

// ADR-117. The Updater's file anchoring is a rule the code must enforce,
// not an install instruction to remember: a Scheduled Task created
// without "Start in" (the schtasks default) launches with
// CWD=C:\Windows\System32, and the CWD-anchored writers this guards
// against would put the secret-bearing appsettings.json THERE while
// AgentDeployer mounted the stale copy next to the exe.
//
// A source tripwire rather than a behavioural test, deliberately: the
// behaviour ("writes land beside the executable") depends on where the
// test host itself runs, but the rule ("nothing consults the working
// directory") is checkable exactly.
public class UpdaterAnchoringTests
{
    [Fact]
    public void NothingInTheUpdaterUsesTheWorkingDirectory()
    {
        var offenders = new List<string>();
        var root = FindRepoRoot();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(root, "Vivnest.Agent.Updater"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var code = File.ReadAllLines(file)
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));

            if (string.Join('\n', code).Contains("GetCurrentDirectory", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These Updater files consult the working directory:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => "  - " + o))
            + Environment.NewLine
            + "Anchor on AppContext.BaseDirectory instead (ADR-117).");
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
