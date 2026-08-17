using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Services;

// Decision-log.md ADR-079 - a standalone sweep, deliberately not
// piggybacked onto HealthMonitorService's per-tick loop (that class has
// its own tightly scoped job already). Full, unpartitioned table scan -
// same shape HealthMonitorService.RunAsync's own GetAllAsync calls
// already use, an accepted cost at this codebase's current scale.
public sealed class CommandExpiryService : ICommandExpiryService
{
    private readonly IAgentCommandStore _commands;
    private readonly CommandExpiryOptions _options;
    private readonly ILogger<CommandExpiryService> _logger;

    public CommandExpiryService(
        IAgentCommandStore commands,
        IOptions<CommandExpiryOptions> options,
        ILogger<CommandExpiryService> logger)
    {
        _commands = commands;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Command expiry disabled.");
            return;
        }

        var all = await _commands.GetAllAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var expiredCount = 0;

        foreach (var entity in all)
        {
            if (!Enum.TryParse<AgentCommandStatus>(entity.Status, out var status)
                || status.IsTerminal()
                || entity.ExpiresUtc > now)
            {
                continue;
            }

            var command = AgentCommand.Rehydrate(
                entity.TenantId,
                entity.SiteId,
                entity.RowKey,
                entity.AgentId,
                entity.TargetDeviceId,
                entity.CapabilityId,
                entity.CommandType,
                status,
                entity.Payload,
                entity.Result,
                entity.ErrorCode,
                entity.ErrorMessage,
                entity.RequestedBy,
                entity.CreatedUtc,
                entity.DispatchedUtc,
                entity.ReceivedUtc,
                entity.StartedUtc,
                entity.CompletedUtc,
                entity.ExpiresUtc);

            command.MarkExpired();

            entity.Status = command.Status.ToString();
            entity.CompletedUtc = command.CompletedUtc;

            await _commands.UpdateAsync(entity, cancellationToken);
            expiredCount++;
        }

        _logger.LogInformation("Expired {Count} command(s).", expiredCount);
    }
}
