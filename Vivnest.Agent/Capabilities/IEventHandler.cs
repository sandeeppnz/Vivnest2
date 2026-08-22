namespace Vivnest.Agent.Capabilities;

public interface IEventHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
