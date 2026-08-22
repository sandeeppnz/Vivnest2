namespace Vivnest.Agent.Runtime.Shell;

public interface IAgentLogBuffer
{
    void Add(string line);

    IReadOnlyList<string> Snapshot();
}
