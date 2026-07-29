namespace Vivnest.Agent.Runtime.Dispatching;

public interface ICapabilityDispatcher
{
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}