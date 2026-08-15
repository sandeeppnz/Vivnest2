using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;

namespace Vivnest.Agent.Runtime.Shell;

// The Agent's first Cloud-to-Agent consumer - polls a dedicated queue
// (agent-restart-commands) rather than a push mechanism, using the same
// Azure Storage Queue technology every other queue here already uses, just
// in the reverse direction. A separate queue per command purpose, not one
// shared queue with type-based routing: Azure Storage Queues have no
// per-consumer filtering, so a future Deploy command (consumed by a
// different, host-level component, not this process - see decision-log.md)
// needs its own queue too, not a shared one this worker would have to
// selectively ignore.
public sealed class CommandPollingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly QueueServiceClient _queueServiceClient;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly ILogger<CommandPollingWorker> _logger;

    public CommandPollingWorker(
        QueueServiceClient queueServiceClient,
        IHostApplicationLifetime lifetime,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        ILogger<CommandPollingWorker> logger)
    {
        _queueServiceClient = queueServiceClient;
        _lifetime = lifetime;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_messagingOptions.RestartCommandQueue))
        {
            _logger.LogWarning(
                "Messaging:RestartCommandQueue not configured; Command Polling Worker has nothing to poll.");

            return;
        }

        var queue = _queueServiceClient.GetQueueClient(_messagingOptions.RestartCommandQueue);

        await queue.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Command Polling Worker started, polling {Queue} every {Interval}.",
            _messagingOptions.RestartCommandQueue,
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
                    await HandleMessageAsync(queue, message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command Polling Worker tick failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task HandleMessageAsync(
        QueueClient queue,
        AzureQueueMessage message,
        CancellationToken cancellationToken)
    {
        // Delete first, not after processing - a simple, non-retrying
        // design. Occasionally losing a restart request to a rare
        // transient error is a much smaller problem than a malformed
        // message crash-looping this worker forever (there's no poison
        // queue handling here, unlike Azure Functions' queue triggers).
        await queue.DeleteMessageAsync(
            message.MessageId,
            message.PopReceipt,
            cancellationToken);

        RestartCommandQueueMessage? command;

        try
        {
            command = JsonSerializer.Deserialize<RestartCommandQueueMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Unable to deserialize restart command message {MessageId}; discarding.",
                message.MessageId);

            return;
        }

        if (command is null)
        {
            _logger.LogWarning(
                "Restart command message {MessageId} deserialized to null; discarding.",
                message.MessageId);

            return;
        }

        if (!string.Equals(command.AgentId, _agentOptions.AgentId, StringComparison.Ordinal))
        {
            // Not addressed to this agent - load-bearing now that a
            // Low-type and a High-type agent can both poll this same
            // restart queue (ADR-035). Each discards the other's restart
            // commands here rather than acting on them.
            _logger.LogWarning(
                "Restart command addressed to {TargetAgentId}, not this agent ({AgentId}); discarding.",
                command.AgentId,
                _agentOptions.AgentId);

            return;
        }

        // Decision-log.md ADR-082 - a real gap, found during Pass 4's
        // reliability review, not live-triggered: this worker never
        // checked whether Cloud still considers the command live before
        // restarting - a stale message (already Expired, or resolved some
        // other way) sitting in the queue while this Agent was offline
        // would still trigger a real, unexpected restart the moment the
        // container finally came back online and drained its backlog.
        // Best-effort like the Received callback below - a failed check
        // fails open (restarts anyway) rather than getting stuck, since a
        // missed check is a much smaller problem than never restarting
        // when genuinely asked to.
        if (!string.IsNullOrWhiteSpace(command.CommandId) &&
            await TryIsAlreadyResolvedAsync(command.CommandId, cancellationToken))
        {
            _logger.LogInformation(
                "Restart command {CommandId} is already resolved or expired; discarding without restarting.",
                command.CommandId);

            return;
        }

        _logger.LogInformation(
            "Restart command received (issued {IssuedAtUtc}); stopping application - the container's restart policy will bring it back.",
            command.IssuedAtUtc);

        // Decision-log.md ADR-079 - best-effort, matching
        // Vivnest.Agent.Updater's own TryReportDeployCompleteAsync
        // convention: a real network round-trip before this process
        // exits, but a failure here must never block the restart itself.
        // Succeeded is confirmed a different way regardless (Cloud-side
        // heartbeat-StartedUtc correlation), so a missed Received report
        // just means that one checkpoint never shows up - not a stuck
        // command.
        if (!string.IsNullOrWhiteSpace(command.CommandId))
        {
            await TryReportReceivedAsync(command.CommandId, cancellationToken);
        }

        _lifetime.StopApplication();
    }

    private async Task<bool> TryIsAlreadyResolvedAsync(string commandId, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient();

            var url = $"{_agentOptions.CloudApiBaseUrl.TrimEnd('/')}/api/agents/{_agentOptions.AgentId}/commands/{commandId}" +
                      $"?tenantId={Uri.EscapeDataString(_agentOptions.TenantId)}&siteId={Uri.EscapeDataString(_agentOptions.SiteId)}";

            var response = await http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            var detail = await response.Content.ReadFromJsonAsync<CommandStatusCheck>(
                CaseInsensitiveJsonOptions, cancellationToken);

            if (detail is null)
                return false;

            var isTerminal = detail.Status is "Succeeded" or "Failed" or "Expired" or "Cancelled";

            return isTerminal || DateTime.UtcNow > detail.ExpiresUtc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check status for command {CommandId} before restarting; proceeding with restart.", commandId);

            return false;
        }
    }

    private async Task TryReportReceivedAsync(string commandId, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient();

            var url = $"{_agentOptions.CloudApiBaseUrl.TrimEnd('/')}/api/agents/{_agentOptions.AgentId}/commands/{commandId}/status";

            var response = await http.PutAsJsonAsync(
                url,
                new CommandStatusUpdateBody(_agentOptions.TenantId, _agentOptions.SiteId, "Received", null, null, null),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Received-status callback for command {CommandId} returned {StatusCode}.",
                    commandId,
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report Received for command {CommandId}.", commandId);
        }
    }
}

// Decision-log.md ADR-079 - mirrors Vivnest.Agent.Updater's own
// ReportDeployCompleteBody: a small, local record for one HTTP call's
// body, not a shared Vivnest.Core contract type - same convention that
// codebase already established for Agent/Updater-to-Cloud callbacks.
internal sealed record CommandStatusUpdateBody(
    string TenantId,
    string SiteId,
    string Status,
    string? Result,
    string? ErrorCode,
    string? ErrorMessage);

// Decision-log.md ADR-082 - the minimal slice of AgentCommandDto this
// worker needs for its pre-restart resolved/expired check; deliberately
// not the full shape AgentCommandPollingWorker's own AgentCommandDetails
// uses, since this worker has no handler dispatch to feed.
internal sealed record CommandStatusCheck(string Status, DateTime ExpiresUtc);
