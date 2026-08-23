namespace Vivnest.Core.Runtime;

public interface IAgentLogBuffer
{
    void Add(string line);

    IReadOnlyList<string> Snapshot();
}
