using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;

namespace Vivnest.Agent.Shell;

// The Cloud-to-Agent queue consumers all had the same skeleton written out
// by hand: guard on the queue name being configured, create-if-not-exists,
// poll every 15s with maxMessages 10, delete each message *before*
// processing it, deserialize, null-check, then discard anything not
// addressed to this Agent.
//
// That skeleton is load-bearing, not incidental, which is why it is worth
// having once:
//
//   - Delete-before-process is a deliberate non-retrying design (ADR-024).
//     A malformed message crash-looping the worker forever is worse than
//     occasionally losing one command, since there is no poison-queue
//     handling here the way Azure Functions' queue triggers have.
//   - The AgentId filter is load-bearing too: these queues are shared
//     broadcast channels that every Agent polls, so each one skips the
//     others' messages. The filter runs BEFORE the delete (ADR-123): a
//     message addressed to another Agent is left in the queue - it goes
//     invisible for the receive's visibility timeout, then reappears for
//     its real addressee. The original delete-then-filter order let two
//     Agents polling concurrently have one consume and DISCARD a message
//     addressed to the other - a race recorded in the 2026-08 dead-code
//     audit as theoretical, then hit for real the day a second agent
//     joined the site (the Capture agent ate the AI agent's
//     RefreshConfiguration until it expired). Delete-before-process still
//     holds for messages addressed to THIS agent, and for unparseable
//     ones - the poison-protection rationale is untouched.
//
// Vivnest.Agent.Updater's DeployPollingWorker is a third copy of this same
// shape and deliberately still stands apart: it lives in its own assembly
// and Vivnest.Core carries no Microsoft.Extensions.Hosting reference, so
// there is nowhere both could share a BackgroundService base from without
// adding that package to a project the Cloud stack also consumes.
public abstract class QueuePollingWorkerBase<TMessage> : BackgroundService
    where TMessage : class
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly ILogger _logger;

    protected QueuePollingWorkerBase(QueueServiceClient queueServiceClient, ILogger logger)
    {
        _queueServiceClient = queueServiceClient;
        _logger = logger;
    }

    /// <summary>Human-readable worker name, used verbatim in log messages.</summary>
    protected abstract string WorkerName { get; }

    /// <summary>Configuration key quoted when <see cref="QueueName"/> is unset.</summary>
    protected abstract string QueueSettingName { get; }

    /// <summary>Resolved queue name; null/blank means "nothing to poll".</summary>
    protected abstract string? QueueName { get; }

    /// <summary>This Agent's own runtime id, compared against each message.</summary>
    protected abstract string ThisAgentId { get; }

    /// <summary>Noun used in per-message log lines ("restart command").</summary>
    protected abstract string MessageKind { get; }

    protected virtual TimeSpan PollInterval => TimeSpan.FromSeconds(15);

    // How long a received message stays invisible before the queue offers
    // it again - the window in which THIS agent decides "mine or not".
    // Short, so a message this agent leaves for another one reappears
    // quickly (ADR-123).
    private static readonly TimeSpan ReceiveVisibilityTimeout = TimeSpan.FromSeconds(10);

    // A foreign message whose addressee never claims it (a decommissioned
    // agent, a typo'd id) would otherwise reappear forever. At the cap it
    // is discarded like the pre-ADR-123 behavior - by then every live
    // agent has seen and declined it many times over.
    private const long ForeignMessageDequeueCap = 100;

    protected abstract string AgentIdOf(TMessage message);

    protected abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueName = QueueName;

        if (string.IsNullOrWhiteSpace(queueName))
        {
            _logger.LogWarning(
                "{QueueSettingName} not configured; {WorkerName} has nothing to poll.",
                QueueSettingName,
                WorkerName);

            return;
        }

        var queue = _queueServiceClient.GetQueueClient(queueName);

        await queue.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        _logger.LogInformation(
            "{WorkerName} started, polling {Queue} every {Interval}.",
            WorkerName,
            queueName,
            PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await queue.ReceiveMessagesAsync(
                    maxMessages: 10,
                    visibilityTimeout: ReceiveVisibilityTimeout,
                    cancellationToken: stoppingToken);

                foreach (var message in response.Value)
                {
                    await DispatchAsync(queue, message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{WorkerName} tick failed.", WorkerName);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task DispatchAsync(
        QueueClient queue,
        AzureQueueMessage message,
        CancellationToken cancellationToken)
    {
        TMessage? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<TMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Unable to deserialize {MessageKind} message {MessageId}; discarding.",
                MessageKind,
                message.MessageId);

            await DeleteAsync(queue, message, cancellationToken);
            return;
        }

        if (parsed is null)
        {
            _logger.LogWarning(
                "{MessageKind} message {MessageId} deserialized to null; discarding.",
                MessageKind,
                message.MessageId);

            await DeleteAsync(queue, message, cancellationToken);
            return;
        }

        var targetAgentId = AgentIdOf(parsed);

        if (!string.Equals(targetAgentId, ThisAgentId, StringComparison.Ordinal))
        {
            // ADR-123 - NOT ours, NOT deleted: it reappears after the
            // visibility timeout for the agent it is addressed to.
            if (message.DequeueCount >= ForeignMessageDequeueCap)
            {
                _logger.LogWarning(
                    "{MessageKind} addressed to {TargetAgentId} has bounced {DequeueCount} times unclaimed; discarding.",
                    MessageKind,
                    targetAgentId,
                    message.DequeueCount);

                await DeleteAsync(queue, message, cancellationToken);
                return;
            }

            _logger.LogDebug(
                "{MessageKind} addressed to {TargetAgentId}, not this agent ({AgentId}); leaving it in the queue.",
                MessageKind,
                targetAgentId,
                ThisAgentId);

            return;
        }

        // Ours - delete BEFORE processing, deliberately (see class comment).
        await DeleteAsync(queue, message, cancellationToken);
        await HandleAsync(parsed, cancellationToken);
    }

    private static Task DeleteAsync(
        QueueClient queue,
        AzureQueueMessage message,
        CancellationToken cancellationToken) =>
        queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
}
