namespace Vivnest.Abstractions.Events;

public interface IEventDispatcher
{
    Task DispatchAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}
