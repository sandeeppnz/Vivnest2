namespace Vivnest.Agent.Interfaces;

public interface ICapabilityHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
