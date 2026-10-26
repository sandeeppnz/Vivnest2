namespace Vivnest.Tests;

// ADR-128. Three consumers decline messages addressed to another agent
// (QueuePollingWorkerBase for restart + agent commands, AiClassificationWorker,
// the Updater's DeployPollingWorker). Leaving a declined message hidden for
// its visibility timeout phase-locks two agents polling on the same period:
// one never sees its own message while the other re-hides it every cycle
// (observed 2026-10-27, ExecuteCapability dequeued 48 times, never delivered).
//
// A source tripwire rather than a behavioural test: QueueClient is not
// practical to fake, and the regression this guards against is someone
// "simplifying" the release away because the pre-ADR-128 code looked
// complete without it.
public class SharedQueueReleaseTests
{
    [Theory]
    [InlineData("Vivnest.Agent/Shell/QueuePollingWorkerBase.cs")]
    [InlineData("Vivnest.Capabilities/AiClassification/AiClassificationWorker.cs")]
    [InlineData("Vivnest.Agent.Updater/DeployPollingWorker.cs")]
    public void ADeclinedForeignMessageIsReleasedAtOnce(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

        // The decline branch is the one that logs "leaving it in the queue".
        var declined = text.IndexOf("leaving it in the queue", StringComparison.Ordinal);
        Assert.True(declined >= 0, $"{relativePath} no longer has the foreign-message branch this test guards.");

        var after = text[declined..];
        var release = after.IndexOf("UpdateMessageAsync", StringComparison.Ordinal);

        Assert.True(
            release >= 0 && after[release..].Contains("visibilityTimeout: TimeSpan.Zero", StringComparison.Ordinal),
            $"{relativePath} leaves a foreign message hidden instead of releasing it (ADR-128).");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Vivnest.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
