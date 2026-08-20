using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Tests;

// RetargetImageVersion exists because Install and Move were the only ways
// to change an installation's desired image version, and both retire the
// current installation and mint a new InstallationId. The Updater stores
// its InstallationId at registration and posts deploy-complete against it,
// so using Move for a version change leaves a running agent reporting to a
// decommissioned row.
//
// These tests pin the two properties that make it safe for that job:
// identity is preserved, and lifecycle is untouched.
public class AgentInstallationTests
{
    private static AgentInstallation Installed(string? imageVersion = "1.0.0") =>
        new("tenant-1", "site-1", "agent-1", "machine-1", "container-1", "vivnest-agent", imageVersion);

    [Fact]
    public void RetargetingTheVersionChangesOnlyTheVersion()
    {
        var installation = Installed();
        var id = installation.InstallationId;
        var installedUtc = installation.InstalledUtc;
        var status = installation.Status; // Pending until first heartbeat

        installation.RetargetImageVersion("1.1.1");

        Assert.Equal("1.1.1", installation.ImageVersion);

        // The whole point: the Updater's stored InstallationId must keep
        // resolving, and the lifecycle must be untouched - whatever it
        // happened to be. Asserting "unchanged" rather than a specific
        // status, because retargeting a version has no business caring
        // which stage of its life the installation is in.
        Assert.Equal(id, installation.InstallationId);
        Assert.Equal(status, installation.Status);
        Assert.Null(installation.RemovedUtc);
        Assert.Equal(installedUtc, installation.InstalledUtc);
        Assert.Equal("machine-1", installation.MachineId);
        Assert.Equal("container-1", installation.ContainerId);
    }

    [Fact]
    public void RetargetingStampsUpdatedUtc()
    {
        var installation = Installed();
        var before = installation.UpdatedUtc;

        installation.RetargetImageVersion("1.1.1");

        Assert.True(installation.UpdatedUtc >= before);
    }

    // Clearing it is meaningful, not an error: a null ImageVersion is how
    // an installation says "no specific desired version", which is what
    // every installation looked like before semver tagging existed, and
    // what AgentDeployer treats as ":latest".
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankClearsTheDesiredVersionRatherThanStoringWhitespace(string? blank)
    {
        var installation = Installed();

        installation.RetargetImageVersion(blank);

        Assert.Null(installation.ImageVersion);
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        var installation = Installed();

        installation.RetargetImageVersion("  1.1.1  ");

        Assert.Equal("1.1.1", installation.ImageVersion);
    }

    // Decommission is still terminal and still does what it did - this
    // method must not have quietly become a second way to reactivate.
    [Fact]
    public void RetargetingADecommissionedInstallationDoesNotReviveIt()
    {
        var installation = Installed();
        installation.Decommission();

        installation.RetargetImageVersion("1.1.1");

        Assert.Equal(AgentInstallationStatus.Decommissioned, installation.Status);
        Assert.NotNull(installation.RemovedUtc);
    }
}
