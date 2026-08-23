using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Sites;

namespace Vivnest.Cloud.Services;

// Sprint 8 - decides whether one Agent error should actually become a
// notification. This is the whole reason the feature was blocked before it
// was built: without it, a crash-looping worker turns one fault into
// hundreds of identical Telegram messages, and the alert that matters
// arrives buried in the noise.
//
// Two independent gates, because they fail differently:
//
//   1. Per (agent, signature) cooldown - suppresses the SAME fault
//      repeating. Keyed on the signature rather than the agent so that a
//      noisy subsystem cannot mask a genuinely different error on the same
//      agent, which a purely per-agent cooldown would do.
//   2. Per-agent hourly ceiling - the backstop for what the cooldown
//      structurally cannot catch: many DISTINCT errors at once, every one
//      a new signature, every one therefore allowed through.
//
// Both are checked; either can veto. The ceiling is only consumed when a
// notification is actually going out, so suppressed duplicates do not eat
// the budget.
public sealed class AgentAlertThrottle : IAgentAlertThrottle
{
    // Anything that varies per occurrence but not per fault: GUIDs,
    // timestamps, and bare numbers. Without this, "capture 41 failed" and
    // "capture 42 failed" are different signatures and the cooldown never
    // engages - which is precisely the crash-loop case it exists for.
    private static readonly Regex Volatile = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
        + @"|\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?Z?"
        + @"|\d+",
        RegexOptions.Compiled);

    private readonly IAgentAlertStateStore _store;
    private readonly OperationalAlertOptions _options;
    private readonly ILogger<AgentAlertThrottle> _logger;

    public AgentAlertThrottle(
        IAgentAlertStateStore store,
        IOptions<OperationalAlertOptions> options,
        ILogger<AgentAlertThrottle> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    // Stable across restarts and across processes - a plain string hash
    // would not be, since .NET randomises it per process, and this value is
    // persisted as a RowKey.
    public static string ComputeSignature(string category, string message)
    {
        var normalized = Volatile.Replace(message ?? "", "#");
        var bytes = Encoding.UTF8.GetBytes($"{category}|{normalized}");

        return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
    }

    public async Task<bool> ShouldNotifyAsync(
        SiteScope scope,
        string runtimeAgentId,
        string signature,
        string sampleMessage,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = $"{scope.PartitionKey}|{runtimeAgentId}";

        var existing = await _store.GetAsync(partitionKey, signature, cancellationToken);

        if (existing != null && nowUtc - existing.LastNotifiedUtc < _options.CooldownPerSignature)
        {
            _logger.LogDebug(
                "Agent {RuntimeAgentId} error {Signature} suppressed: within the {Cooldown} cooldown.",
                runtimeAgentId, signature, _options.CooldownPerSignature);

            return false;
        }

        if (!await TryConsumeCeilingAsync(scope, partitionKey, runtimeAgentId, nowUtc, cancellationToken))
            return false;

        await _store.UpsertAsync(
            new AgentAlertStateEntity
            {
                PartitionKey = partitionKey,
                RowKey = signature,
                TenantId = scope.TenantId,
                SiteId = scope.SiteId,
                LastNotifiedUtc = nowUtc,
                SampleMessage = Truncate(sampleMessage, 512),
                ETag = existing?.ETag ?? default
            },
            cancellationToken);

        return true;
    }

    // A fixed window, not a sliding one. A sliding window would need the
    // timestamp of every notification in the last hour; a fixed window
    // needs two fields. The cost is that a burst can straddle a boundary
    // and send up to 2x the limit across two adjacent hours - acceptable
    // for something whose job is "stop hundreds", not "meter precisely".
    private async Task<bool> TryConsumeCeilingAsync(
        SiteScope scope,
        string partitionKey,
        string runtimeAgentId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ceiling = await _store.GetAsync(
            partitionKey, AgentAlertStateEntity.CeilingRowKey, cancellationToken);

        var windowStart = ceiling?.WindowStartedUtc ?? nowUtc;
        var count = ceiling?.WindowCount ?? 0;

        if (ceiling == null || nowUtc - windowStart >= TimeSpan.FromHours(1))
        {
            windowStart = nowUtc;
            count = 0;
        }

        if (count >= _options.MaxNotificationsPerAgentPerHour)
        {
            // Logged at Warning because being at the ceiling is itself
            // operational news: it means this agent is failing in many
            // distinct ways at once, which is usually worse than the
            // individual errors being dropped.
            _logger.LogWarning(
                "Agent {RuntimeAgentId} has hit the alert ceiling ({Max}/hour); suppressing further error notifications until {WindowEnd}.",
                runtimeAgentId, _options.MaxNotificationsPerAgentPerHour, windowStart.AddHours(1));

            return false;
        }

        await _store.UpsertAsync(
            new AgentAlertStateEntity
            {
                PartitionKey = partitionKey,
                RowKey = AgentAlertStateEntity.CeilingRowKey,
                TenantId = scope.TenantId,
                SiteId = scope.SiteId,
                WindowStartedUtc = windowStart,
                WindowCount = count + 1,
                ETag = ceiling?.ETag ?? default
            },
            cancellationToken);

        return true;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? "" : value[..max];
}
