using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vivnest.Agent.Runtime.Dispatching;

public class EventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<EventDispatcher> _logger;

    public EventDispatcher(IServiceProvider provider, ILogger<EventDispatcher> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
    {
        var handlers =
            _provider.GetServices<IEventHandler<TEvent>>();

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
