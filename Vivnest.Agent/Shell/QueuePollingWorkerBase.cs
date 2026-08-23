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
//     broadcast channels that every Agent polls, so each one discards the
//     others' messages. Note this filter runs *after* the delete, so two
//     Agents polling concurrently can have one consume and discard a
//     message addressed to the other - a real race, unchanged by this
//     extraction, recorded in the 2026-08 dead-code audit. Now that the
//     code lives in one place, fixing it is a one-place fix.
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
        // See the class comment - delete first, deliberately.
        await queue.DeleteMessageAsync(
            message.MessageId,
            message.PopReceipt,
            cancellationToken);

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

            return;
        }

        if (parsed is null)
        {
            _logger.LogWarning(
                "{MessageKind} message {MessageId} deserialized to null; discarding.",
                MessageKind,
                message.MessageId);

            return;
        }

        var targetAgentId = AgentIdOf(parsed);

        if (!string.Equals(targetAgentId, ThisAgentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "{MessageKind} addressed to {TargetAgentId}, not this agent ({AgentId}); discarding.",
                MessageKind,
                targetAgentId,
                ThisAgentId);

            return;
        }

        await HandleAsync(parsed, cancellationToken);
    }
}
