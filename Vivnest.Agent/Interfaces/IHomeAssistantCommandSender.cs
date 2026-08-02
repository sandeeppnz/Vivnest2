namespace Vivnest.Agent.Interfaces;

public interface IHomeAssistantCommandSender
{
    Task CallServiceAsync(
        string domain,
        string service,
        string entityId,
        CancellationToken cancellationToken = default);
}
