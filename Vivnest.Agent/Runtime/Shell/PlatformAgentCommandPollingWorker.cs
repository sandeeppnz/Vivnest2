using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Vivnest.Abstraction.Agent.Commands;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Runtime.Shell;

// Decision-log.md ADR-080 - a deliberate sibling to PlatformCommandPollingWorker,
// not a rewrite of it: same poll-and-delete-before-process shape, but for
// the shared agent-commands queue (RefreshConfiguration/ApplyConfiguration/
// future ExecuteCapability) rather than the dedicated restart queue. Unlike
// PlatformCommandPollingWorker (which acts on the queue envelope alone),
// this worker fetches full command detail from Cloud before executing -
// the envelope only carries CommandId/AgentId/CommandType, see
// AgentCommandQueueMessage.
public sealed class PlatformAgentCommandPollingWorker : QueuePollingWorkerBase<AgentCommandQueueMessage>
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHostApplicationLifetime _lifetime;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;
    private readonly ILogger<PlatformAgentCommandPollingWorker> _logger;

    public PlatformAgentCommandPollingWorker(
        QueueServiceClient queueServiceClient,
        IHostApplicationLifetime lifetime,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IEnumerable<ICommandHandler> handlers,
        ILogger<PlatformAgentCommandPollingWorker> logger)
        : base(queueServiceClient, logger)
    {
        _lifetime = lifetime;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _handlers = handlers.ToDictionary(h => h.CommandType, StringComparer.Ordinal);
        _logger = logger;
    }

    protected override string WorkerName => "Agent Command Polling Worker";

    protected override string QueueSettingName => "Messaging:AgentCommandQueue";

    protected override string? QueueName => _messagingOptions.AgentCommandQueue;

    protected override string ThisAgentId => _agentOptions.AgentId;

    protected override string MessageKind => "Agent command";

    protected override string AgentIdOf(AgentCommandQueueMessage message) => message.AgentId;

    // The queue envelope carries only CommandId/AgentId/CommandType, so
    // unlike the restart worker this one fetches full command detail from
    // Cloud before doing anything with it.
    protected override Task HandleAsync(
        AgentCommandQueueMessage envelope,
        CancellationToken cancellationToken) =>
        ProcessCommandAsync(envelope.CommandId, cancellationToken);

    private async Task ProcessCommandAsync(string commandId, CancellationToken cancellationToken)
    {
        using var http = CreateClient();

        var baseUrl = _agentOptions.CloudApiBaseUrl.TrimEnd('/');

        var command = await TryFetchCommandAsync(http, baseUrl, commandId, cancellationToken);

        if (command == null)
            return;

        // Decision-log.md ADR-082 - a real gap, found during Pass 4's
        // reliability review, not live-triggered: the queue envelope
        // alone carries no status/expiry, so a command that expired (or
        // was already resolved some other way) while its message sat
        // undelivered would previously be executed blindly the moment it
        // was finally dequeued - a real capture fired, or a real restart
        // triggered, for a command Cloud already considers terminal.
        // Cloud's own status-transition guard (AgentCommandManagementService.
        // UpdateStatusAsync) only protects the *recorded* status from a
        // stale update after the fact - it was never a defense against the
        // Agent actually re-running the underlying side effect. Checked
        // here, before Received is even reported, so a discarded command
        // leaves no trace of ever being picked up.
        if (IsTerminal(command.Status))
        {
            _logger.LogInformation(
                "Command {CommandId} is already {Status}; discarding without executing.", commandId, command.Status);

            return;
        }

        if (DateTime.UtcNow > command.ExpiresUtc)
        {
            _logger.LogInformation(
                "Command {CommandId} expired at {ExpiresUtc:u}; discarding without executing.", commandId, command.ExpiresUtc);

            return;
        }

        await TryReportStatusAsync(http, baseUrl, commandId, "Received", null, null, null, cancellationToken);

        if (!_handlers.TryGetValue(command.CommandType, out var handler))
        {
            _logger.LogWarning("No command handler registered for CommandType {CommandType} (command {CommandId}).", command.CommandType, commandId);

            await TryReportStatusAsync(http, baseUrl, commandId, "Failed", null, "NO_HANDLER", $"No handler registered for {command.CommandType}.", cancellationToken);

            return;
        }

        CommandHandlerResult result;

        try
        {
            result = await handler.HandleAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command handler for {CommandType} threw while handling command {CommandId}.", command.CommandType, commandId);

            await TryReportStatusAsync(http, baseUrl, commandId, "Failed", null, "HANDLER_EXCEPTION", ex.Message, cancellationToken);

            return;
        }

        switch (result.Outcome)
        {
            case CommandHandlerOutcome.Succeeded:
                await TryReportStatusAsync(http, baseUrl, commandId, "Succeeded", result.Result, null, null, cancellationToken);
                break;

            case CommandHandlerOutcome.Failed:
                await TryReportStatusAsync(http, baseUrl, commandId, "Failed", null, result.ErrorCode, result.ErrorMessage, cancellationToken);
                break;

            case CommandHandlerOutcome.Restart:
                await TryReportStatusAsync(http, baseUrl, commandId, "Executing", null, null, null, cancellationToken);

                _logger.LogInformation(
                    "Command {CommandId} ({CommandType}) requires a restart to apply; stopping application - the container's restart policy will bring it back.",
                    commandId, command.CommandType);

                _lifetime.StopApplication();
                break;
        }
    }

    // Decision-log.md ADR-082 - this used to be a local literal set
    // ("Succeeded" or "Failed" or "Expired" or "Cancelled") mirroring the
    // Cloud-side rule by hand. It now parses into the shared
    // AgentCommandStatus and asks AgentCommandStatusExtensions.IsTerminal,
    // so Cloud and Agent read one definition instead of three copies.
    // This file still transacts status as plain strings over HTTP - that
    // convention is unchanged; only the terminal-set knowledge moved.
    //
    // An unparseable status is treated as non-terminal, exactly as the
    // literal set did (no match -> false -> proceed), so a Cloud that
    // starts returning a status this Agent build doesn't know still gets
    // the old behaviour rather than silently discarding the command.
    private static bool IsTerminal(string status) =>
        Enum.TryParse<AgentCommandStatus>(status, out var parsed) && parsed.IsTerminal();

    private async Task<AgentCommandDetails?> TryFetchCommandAsync(
        HttpClient http,
        string baseUrl,
        string commandId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{baseUrl}/api/agents/{_agentOptions.AgentId}/commands/{commandId}" +
                      $"?tenantId={Uri.EscapeDataString(_agentOptions.TenantId)}&siteId={Uri.EscapeDataString(_agentOptions.SiteId)}";

            var response = await http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Fetching command {CommandId} returned {StatusCode}.", commandId, (int)response.StatusCode);

                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<AgentCommandDetails>(json, HttpJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch command {CommandId}.", commandId);

            return null;
        }
    }

    // Reuses PlatformCommandPollingWorker.cs's own internal CommandStatusUpdateBody
    // (same namespace, same assembly) rather than declaring a second
    // identical record - unlike the Agent/Cloud process-boundary
    // duplication convention (e.g. AgentCommandDto vs. AgentCommandDetails
    // above), these two workers live in the same assembly with nothing
    // stopping a direct share.
    private async Task TryReportStatusAsync(
        HttpClient http,
        string baseUrl,
        string commandId,
        string status,
        string? result,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{baseUrl}/api/agents/{_agentOptions.AgentId}/commands/{commandId}/status";

            var response = await http.PutAsJsonAsync(
                url,
                new CommandStatusUpdateBody(_agentOptions.TenantId, _agentOptions.SiteId, status, result, errorCode, errorMessage),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Status callback ({Status}) for command {CommandId} returned {StatusCode}.",
                    status, commandId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report status {Status} for command {CommandId}.", status, commandId);
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
