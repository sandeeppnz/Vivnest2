using Microsoft.Extensions.DependencyInjection;

namespace Vivnest.Agent.Runtime.Dispatching;

public class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly IServiceProvider _provider;

    public CapabilityDispatcher(IServiceProvider provider)
    {
        _provider = provider;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
    {
        var handlers =
            _provider.GetServices<ICapabilityHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(
                @event,
                cancellationToken);
        }
    }
}