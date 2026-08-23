using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;
using Vivnest.Domain.Agents;

namespace Vivnest.Agent.Shell;

// The Agent's first Cloud-to-Agent consumer - polls a dedicated queue
// (agent-restart-commands) rather than a push mechanism, using the same
// Azure Storage Queue technology every other queue here already uses, just
// in the reverse direction. A separate queue per command purpose, not one
// shared queue with type-based routing: Azure Storage Queues have no
// per-consumer filtering, so a future Deploy command (consumed by a
// different, host-level component, not this process - see decision-log.md)
// needs its own queue too, not a shared one this worker would have to
// selectively ignore.
public sealed class PlatformCommandPollingWorker : QueuePollingWorkerBase<RestartCommandQueueMessage>
{
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHostApplicationLifetime _lifetime;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly ILogger<PlatformCommandPollingWorker> _logger;

    public PlatformCommandPollingWorker(
        QueueServiceClient queueServiceClient,
        IHostApplicationLifetime lifetime,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        ILogger<PlatformCommandPollingWorker> logger)
        : base(queueServiceClient, logger)
    {
        _lifetime = lifetime;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _logger = logger;
    }

    protected override string WorkerName => "Command Polling Worker";

    protected override string QueueSettingName => "Messaging:RestartCommandQueue";

    protected override string? QueueName => _messagingOptions.RestartCommandQueue;

    protected override string ThisAgentId => _agentOptions.AgentId;

    protected override string MessageKind => "Restart command";

    protected override string AgentIdOf(RestartCommandQueueMessage message) => message.AgentId;

    protected override async Task HandleAsync(
        RestartCommandQueueMessage command,
        CancellationToken cancellationToken)
    {
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
            using var http = CreateClient();

            var url = $"{_agentOptions.CloudApiBaseUrl.TrimEnd('/')}/api/agents/{_agentOptions.AgentId}/commands/{commandId}" +
                      $"?tenantId={Uri.EscapeDataString(_agentOptions.TenantId)}&siteId={Uri.EscapeDataString(_agentOptions.SiteId)}";

            var response = await http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            var detail = await response.Content.ReadFromJsonAsync<CommandStatusCheck>(
                CaseInsensitiveJsonOptions, cancellationToken);

            if (detail is null)
                return false;

            // Shared with Cloud via AgentCommandStatusExtensions rather than
            // a fourth hand-written copy of the terminal set. An unparseable
            // status counts as non-terminal, exactly as the literal set did
            // (no match -> false -> proceed with the restart).
            var isTerminal = Enum.TryParse<AgentCommandStatus>(detail.Status, out var parsed)
                && parsed.IsTerminal();

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
            using var http = CreateClient();

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

    // Presents the Agent's own scoped key (minted at registration) so the
    // command callbacks authenticate as this Agent rather than relying on
    // Cloud trusting the TenantId/SiteId in the request. Empty on an Agent
    // registered before agent keys existed; Cloud still honours those
    // while AgentAuth:RequireApiKey is false.
    private HttpClient CreateClient()
    {
        var http = new HttpClient();

        if (!string.IsNullOrWhiteSpace(_agentOptions.ApiKey))
            http.DefaultRequestHeaders.Add("x-api-key", _agentOptions.ApiKey);

        return http;
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
// not the full shape PlatformAgentCommandPollingWorker's own AgentCommandDetails
// uses, since this worker has no handler dispatch to feed.
internal sealed record CommandStatusCheck(string Status, DateTime ExpiresUtc);
