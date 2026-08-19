using Vivnest.Core.Enums;

namespace Vivnest.Tests;

// The terminal/non-terminal split used to exist in four hand-written
// copies - AgentCommandManagementService (gates the duplicate-callback
// no-op), CommandExpiryService (as the exact complement, its own HashSet),
// and both Agent polling workers as string literals. Each failed
// differently if one were missed: Cloud would re-apply a resolved command,
// the timer would expire it out from under itself, and the Agent would
// re-execute it.
public class AgentCommandStatusTests
{
    [Theory]
    [InlineData(AgentCommandStatus.Succeeded)]
    [InlineData(AgentCommandStatus.Failed)]
    [InlineData(AgentCommandStatus.Expired)]
    [InlineData(AgentCommandStatus.Cancelled)]
    public void ResolvedStatusesAreTerminal(AgentCommandStatus status)
    {
        Assert.True(status.IsTerminal());
    }

    [Theory]
    [InlineData(AgentCommandStatus.Pending)]
    [InlineData(AgentCommandStatus.Dispatched)]
    [InlineData(AgentCommandStatus.Received)]
    [InlineData(AgentCommandStatus.Executing)]
    public void InFlightStatusesAreNotTerminal(AgentCommandStatus status)
    {
        Assert.False(status.IsTerminal());
    }

    // CommandExpiryService only sweeps non-terminal commands. Expressing it
    // as the complement means adding a status can never leave it in both
    // sets or neither, which the two independent hand-written lists could.
    [Fact]
    public void EveryStatusIsExactlyOneOfTerminalOrExpirable()
    {
        foreach (var status in Enum.GetValues<AgentCommandStatus>())
        {
            var terminal = status.IsTerminal();
            var expirable = !status.IsTerminal();

            Assert.NotEqual(terminal, expirable);
        }
    }
}
