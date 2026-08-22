namespace Vivnest.Abstraction.Agent.Capabilities;

public interface ICapabilityContext
{
    string AgentId { get; }

    IServiceProvider Services { get; }
}