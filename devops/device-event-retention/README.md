# Device Event retention — Table Storage cleanup

Deletes `DeviceEvent` rows (`tblDeviceEvents`) older than a configured
retention window. Companion to
[`../blob-lifecycle/README.md`](../blob-lifecycle/README.md), which handles
the actual capture images in Blob Storage — that policy doesn't touch
Table Storage at all, since Azure Table Storage has no native TTL.

## What it is

- `DeviceEventRetentionTimerFunction` (`Vivnest.Cloud.Functions/Timer`) —
  runs on `DeviceEventRetentionCronSchedule`, delegates to
  `IDeviceEventRetentionService`.
- `DeviceEventRetentionService` (`Vivnest.Cloud/Services`) — computes the
  cutoff (`UtcNow - RetentionDays`) and calls
  `IDeviceEventReader.DeleteOlderThanAsync`.
- `AzureTableDeviceEventReader.DeleteOlderThanAsync` — a full-table scan
  filtered on `OccurredAtUtc` (the business timestamp, not the Table
  Storage system `Timestamp` — that one gets bumped by
  `MarkCompletedAsync`/`MarkFailedAsync` after creation, which would make
  age unpredictable), deleting matches one at a time. Fine at this data
  volume; would need partition-aware batching to scale to a much larger
  fleet.

## Configuration

Function App settings (`local.settings.json` locally, Azure app settings
in production):

| Setting | Purpose | Current value |
|---|---|---|
| `DeviceEventRetention__Enabled` | Master on/off switch | `true` |
| `DeviceEventRetention__RetentionDays` | Age threshold, in days | `30` |
| `DeviceEventRetentionCronSchedule` | Cron schedule (flat name — see below) | `0 0 3 * * *` (daily, 3am UTC) |

**`DeviceEventRetentionCronSchedule` is deliberately not double-underscored**,
matching `HealthMonitorCronSchedule`'s existing pattern: the `%...%` Timer
Trigger placeholder resolver looks up the literal setting name, which
doesn't survive .NET's `__` → `:` conversion for environment variables.
Every other setting here uses `IOptions<T>` binding instead, which does
handle that conversion — only the cron placeholder needs the flat name.

## Keeping this in sync with the Blob Lifecycle policy

**No automatic link** — these are two separate systems (Azure Blob
Lifecycle Management vs. this application-level Table cleanup), and
building one would mean either policy-injecting the Function's config from
the blob policy or the reverse, more complexity than the actual need
justifies for a number that changes rarely. When you change the retention
window, update **both**:

1. `daysAfterModificationGreaterThan` in
   [`../blob-lifecycle/blob-lifecycle-policy.json`](../blob-lifecycle/blob-lifecycle-policy.json),
   then re-run its `create` command.
2. `DeviceEventRetention__RetentionDays` here, in the Function App
   settings.

## Verified

Manually invoked locally via the Functions host admin API
(`POST /admin/functions/DeviceEventRetentionTimerFunction`) against real
data: computed the correct cutoff, ran the query, found 0 matching rows
(correct — nothing in the table is older than 30 days yet), completed
without error.
