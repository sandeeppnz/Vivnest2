namespace Vivnest.Agent.Capabilities.Bridges.HomeAssistant;

public interface IHomeAssistantConnectionTracker
{
    DateTime? LastConnectedUtc { get; }

    void MarkConnected();
}
