namespace Vivnest.Agent.Interfaces;

public interface IHomeAssistantConnectionTracker
{
    DateTime? LastConnectedUtc { get; }

    void MarkConnected();
}
