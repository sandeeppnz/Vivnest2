using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Runtime;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Shared;

namespace Vivnest.Agent.Shell;

// Sprint 8 - drains Error-level log signals and turns each into an
// AgentEvent row plus an "agent-events" queue message, exactly the shape
// device events already use (persist, then publish {PartitionKey, RowKey},
// per ADR-004).
//
// Structurally a sibling of PlatformLogShippingWorker, which drains the
// other buffer the same logger provider fills. That one ships the whole
// log for a human to read later; this one raises the errors for something
// to react to now.
//
// No throttling happens here. The Agent reports what it sees; the Cloud
// decides what is worth telling a person about (AgentAlertThrottle). Doing
// it Agent-side would mean each agent independently guessing at a fleet
// policy, and would lose the events from the table entirely rather than
// just suppressing the notification.
public sealed class PlatformErrorEventWorker : BackgroundService
{
    private readonly IAgentErrorSignalBuffer _buffer;
    private readonly IAgentEventWriter _agentEvents;
    private readonly IQueuePublisher _queuePublisher;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly ILogger<PlatformErrorEventWorker> _logger;

    private static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(15);

    public PlatformErrorEventWorker(
        IAgentErrorSignalBuffer buffer,
        IAgentEventWriter agentEvents,
        IQueuePublisher queuePublisher,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        ILogger<PlatformErrorEventWorker> logger)
    {
        _buffer = buffer;
        _agentEvents = agentEvents;
        _queuePublisher = queuePublisher;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_messagingOptions.AgentEventQueue))
        {
            _logger.LogInformation(
                "Messaging:AgentEventQueue is not configured; error events will not be published.");

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never LogError here. An Error from this worker would land
                // straight back in the buffer it just drained, and a
                // persistent storage failure would then feed itself
                // forever. Warning is below the provider's Error trigger.
                _logger.LogWarning(ex, "Error-event drain failed; will retry on the next interval.");
            }

            try
            {
                await Task.Delay(DrainInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var signals = _buffer.DrainAll();

        if (signals.Count == 0)
            return;

        foreach (var signal in signals)
        {
            var agentEvent = new AgentEvent
            {
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                AgentId = _agentOptions.AgentId,
                EventId = Guid.NewGuid(),
                EventType = AgentEventTypes.ErrorLogged,
                OccurredAtUtc = DateTime.UtcNow,
                // EventSeverity has no Error member; Critical is the most
                // severe of Information/Warning/Critical and is the honest
                // mapping for an Error-level log call.
                Severity = EventSeverity.Critical,
                Data = new
                {
                    signal.Category,
                    signal.Message,
                    signal.Exception
                }
            };

            var entity = await _agentEvents.SaveAsync(agentEvent, cancellationToken);

            if (entity is null)
            {
                _logger.LogWarning("Unable to persist an ErrorLogged AgentEvent; dropping it.");
                continue;
            }

            await _queuePublisher.PublishAsync(
                _messagingOptions.AgentEventQueue,
                new AgentEventQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);
        }
    }
}
