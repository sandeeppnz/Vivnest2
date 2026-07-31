namespace Vivnest.Agent.Interfaces;

public interface IEventHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
