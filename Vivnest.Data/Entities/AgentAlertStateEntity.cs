using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Sprint 8 - one row per (agent, error signature), plus one reserved row
// per agent holding the hourly ceiling counter.
//
// Deliberately its own table rather than fields on AgentHeartbeatEntity,
// which is where the comparable NotificationState dedup lives: that one
// tracks a single boolean-ish transition per agent, whereas this needs an
// unbounded, self-pruning set of signatures. Putting them on the heartbeat
// row would mean either a growing blob column or losing the per-signature
// distinction that makes this useful.
public sealed class AgentAlertStateEntity : BaseEntity, ITableEntity
{
    // == "{TenantId}|{SiteId}|{RuntimeAgentId}".
    public string PartitionKey { get; set; } = default!;

    // == the error signature, or CeilingRowKey for the counter row.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    // Both default to UnixEpoch rather than default(DateTime), and that is
    // load-bearing, not tidiness. Each row type only sets one of these -
    // a signature row leaves WindowStartedUtc alone, a ceiling row leaves
    // LastNotifiedUtc alone - and default(DateTime) is 0001-01-01 with
    // Kind.Unspecified, which Azure Tables rejects outright:
    // "DateTime ... has a Kind of Unspecified. Azure SDK requires it to be
    // UTC." DateTime.UnixEpoch is Kind.Utc, so an unset field serialises
    // cleanly instead of failing the whole write.
    //
    // Found in production, not in tests: the in-memory fake accepted any
    // DateTime, so every unit test passed while every real write threw.

    // Signature rows: when this signature last produced a notification.
    // Ceiling row: unused.
    public DateTime LastNotifiedUtc { get; set; } = DateTime.UnixEpoch;

    // Ceiling row only: the start of the current counting window, and how
    // many notifications have been sent within it.
    public DateTime WindowStartedUtc { get; set; } = DateTime.UnixEpoch;

    public int WindowCount { get; set; }

    // Signature rows only - kept purely so a human reading the table can
    // tell what a signature hash actually was.
    public string SampleMessage { get; set; } = string.Empty;

    // The reserved RowKey for an agent's ceiling counter. "__" prefix
    // cannot collide with a signature, which is always lowercase hex.
    public const string CeilingRowKey = "__ceiling";
}
