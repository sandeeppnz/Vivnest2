namespace Vivnest.Agent.Interfaces;

public interface ICapabilityDispatcher
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}