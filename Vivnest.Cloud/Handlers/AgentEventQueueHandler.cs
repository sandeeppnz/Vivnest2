using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Options;
using Vivnest.Cloud.Services;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Handlers;

// Sprint 8 - the "agent-events" consumer, structurally identical to
// DeviceEventQueueHandler: refetch the entity, branch on its EventType.
//
// v1 deliberately has NO LLM in the loop. The roadmap's design put one here
// to turn a raw stack trace into a triage summary, and that is genuinely
// the expensive, non-deterministic part of the idea. Forwarding the error
// as-is closes most of the gap - you find out the agent is failing, which
// today nothing tells you - and leaves the triage step to be added once
// there is real traffic to judge it against.
public sealed class AgentEventQueueHandler : IAgentEventQueueHandler
{
    private readonly IAgentEventReader _agentEvents;
    private readonly IAgentAlertThrottle _throttle;
    private readonly INotificationDispatcher _notifications;
    private readonly OperationalAlertOptions _options;
    private readonly ILogger<AgentEventQueueHandler> _logger;

    public AgentEventQueueHandler(
        IAgentEventReader agentEvents,
        IAgentAlertThrottle throttle,
        INotificationDispatcher notifications,
        IOptions<OperationalAlertOptions> options,
        ILogger<AgentEventQueueHandler> logger)
    {
        _agentEvents = agentEvents;
        _throttle = throttle;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(
        AgentEventQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        var entity = await _agentEvents.GetAsync(
            message.PartitionKey, message.RowKey, cancellationToken);

        if (entity is null)
        {
            _logger.LogWarning(
                "AgentEvent not found {PartitionKey}/{RowKey}",
                message.PartitionKey, message.RowKey);

            return;
        }

        if (!string.Equals(entity.EventType, AgentEventTypes.ErrorLogged, StringComparison.Ordinal))
        {
            // Every other agent event type already has its own path (config
            // publish audit rows, offline/recovered transitions). Same
            // "add a case when someone actually needs it" rule
            // DeviceEventQueueHandler follows.
            return;
        }

        if (!_options.Enabled)
            return;

        var payload = ParsePayload(entity.Payload);

        if (payload is null)
        {
            _logger.LogWarning(
                "AgentEvent {PartitionKey}/{RowKey} is ErrorLogged but its payload did not parse; skipping.",
                message.PartitionKey, message.RowKey);

            return;
        }

        var signature = AgentAlertThrottle.ComputeSignature(payload.Category, payload.Message);

        var shouldNotify = await _throttle.ShouldNotifyAsync(
            new SiteScope(entity.TenantId, entity.SiteId),
            entity.AgentId,
            signature,
            payload.Message,
            DateTime.UtcNow,
            cancellationToken);

        if (!shouldNotify)
            return;

        await _notifications.DispatchAsync(
            new Notification
            {
                Type = NotificationTypes.AgentErrorLogged,
                Title = $"Agent error: {entity.AgentId}",
                Message = BuildMessage(entity, payload),
                Priority = NotificationPriority.Urgent,
                OccurredAtUtc = entity.OccurredAtUtc
            },
            cancellationToken);
    }

    private static string BuildMessage(AgentEventEntity entity, ErrorLoggedPayload payload)
    {
        var lines = new List<string>
        {
            $"Agent {entity.AgentId} logged an error at {entity.OccurredAtUtc:yyyy-MM-dd HH:mm:ss}Z.",
            "",
            payload.Category,
            payload.Message
        };

        if (!string.IsNullOrWhiteSpace(payload.Exception))
        {
            // First line only. The full stack trace is already in
            // agent-logs/{agentId}.txt (ADR-027), and a Telegram message is
            // the wrong place to read one.
            var firstLine = payload.Exception.Split('\n')[0].Trim();

            lines.Add("");
            lines.Add(firstLine);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static ErrorLoggedPayload? ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ErrorLoggedPayload>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// The shape the Agent writes into AgentEventEntity.Payload for an
// ErrorLogged event. Declared here rather than shared with the Agent
// because Vivnest.Cloud does not reference Vivnest.Agent - the same
// one-way dependency every other Agent-to-Cloud payload already crosses by
// agreeing on JSON rather than on a type.
public sealed record ErrorLoggedPayload(string Category, string Message, string? Exception);
