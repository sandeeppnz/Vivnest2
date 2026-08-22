namespace Vivnest.Agent.Runtime.Dispatching;

public interface IEventDispatcher
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}
