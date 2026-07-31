namespace Vivnest.Agent.Interfaces;

public interface IEventDispatcher
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}
