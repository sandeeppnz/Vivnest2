namespace Vivnest.Core.Runtime;

public interface IAgentErrorSignalBuffer
{
    void Add(AgentErrorSignal signal);

    IReadOnlyList<AgentErrorSignal> DrainAll();
}
