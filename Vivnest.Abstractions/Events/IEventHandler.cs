namespace Vivnest.Abstractions.Events;

public interface IEventHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
