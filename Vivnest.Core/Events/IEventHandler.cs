namespace Vivnest.Core.Events;

public interface IEventHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
