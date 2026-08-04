namespace Vivnest.Agent.Capabilities.Bridges.HomeAssistant;

public interface IHomeAssistantCommandSender
{
    Task CallServiceAsync(
        string domain,
        string service,
        string entityId,
        CancellationToken cancellationToken = default);

    Task<string?> GetStateAsync(
        string entityId,
        CancellationToken cancellationToken = default);
}
