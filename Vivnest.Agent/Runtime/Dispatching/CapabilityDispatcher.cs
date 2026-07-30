using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vivnest.Agent.Interfaces;

namespace Vivnest.Agent.Runtime.Dispatching;

public class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<CapabilityDispatcher> _logger;

    public CapabilityDispatcher(IServiceProvider provider, ILogger<CapabilityDispatcher> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
    {
        var handlers =
            _provider.GetServices<ICapabilityHandler<TEvent>>();

        List<Exception>? exceptions = null;

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(
                        @event,
                        cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while handling event {EventType} with handler {HandlerType}.",
                    typeof(TEvent).Name,
                    handler.GetType().Name);

                (exceptions ??= new List<Exception>()).Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException(
                $"One or more handlers failed while handling {typeof(TEvent).Name}.",
                exceptions);
        }
    }
}