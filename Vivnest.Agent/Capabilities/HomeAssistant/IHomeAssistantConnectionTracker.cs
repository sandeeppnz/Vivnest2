namespace Vivnest.Agent.Capabilities.HomeAssistant;

public interface IHomeAssistantConnectionTracker
{
    DateTime? LastConnectedUtc { get; }

    void MarkConnected();
}
