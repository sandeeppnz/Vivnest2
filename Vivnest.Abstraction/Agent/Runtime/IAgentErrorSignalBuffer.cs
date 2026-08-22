namespace Vivnest.Agent.Runtime.Shell;

public interface IAgentErrorSignalBuffer
{
    void Add(AgentErrorSignal signal);

    IReadOnlyList<AgentErrorSignal> DrainAll();
}
