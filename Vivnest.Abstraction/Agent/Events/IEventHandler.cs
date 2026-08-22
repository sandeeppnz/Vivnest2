namespace Vivnest.Abstraction.Agent.Events;

public interface IEventHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
