namespace Vivnest.Core.Events;

public interface IEventDispatcher
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}
