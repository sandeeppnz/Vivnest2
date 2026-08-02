# Blob Lifecycle Management — capture image retention

Deletes camera capture images from Blob Storage once they're older than 30
days, so storage doesn't grow unbounded. Native Azure feature — no code,
no Function, runs automatically on Azure's own schedule.

## What it does

`blob-lifecycle-policy.json` defines one rule, `delete-old-photos`:
- Scope: blobs under the `photos/` prefix (matches `StorageOptions.BlobContainer`)
- Action: delete any block blob whose **last-modified time** is more than
  30 days ago

## How to apply it

```bash
az storage account management-policy create \
  --account-name <storage-account-name> \
  --resource-group <resource-group-name> \
  --policy devops/blob-lifecycle/blob-lifecycle-policy.json
```

Applied to the current production storage account with:

```bash
az storage account management-policy create \
  --account-name stvivnestagentdev \
  --resource-group rg-vivnest-dev \
  --policy devops/blob-lifecycle/blob-lifecycle-policy.json
```

## How to verify it's active

```bash
az storage account management-policy show \
  --account-name <storage-account-name> \
  --resource-group <resource-group-name>
```

## How to change the retention window

Edit `daysAfterModificationGreaterThan` in the policy JSON, then re-run the
`create` command above (it replaces the existing policy — a storage
account can only have one management policy, with multiple rules inside
it if needed later).

## Things worth knowing

- **Not instant.** Azure evaluates lifecycle rules roughly once every 24
  hours, not the moment a blob crosses 30 days old or the moment this
  policy is applied. Give it a day to catch up.
- **Soft delete is enabled on this storage account** (7-day retention,
  `allowPermanentDelete: false`) — a blob this policy "deletes" isn't
  actually gone for ~7 more days, and **you're billed for it during that
  window at the same rate as active data**. Effective retention is closer
  to 30 + 7 = ~37 days, not a hard 30. To restore something within that
  window: `az storage blob undelete`.
- **This only deletes the blob, not its metadata row.** The companion
  piece — deleting the matching `DeviceEvent` rows in Table Storage — is
  now built: see [`../device-event-retention/README.md`](../device-event-retention/README.md).
  Without it, a row whose blob has been deleted here shows a broken
  thumbnail in the dashboard rather than a crash, but it doesn't clean
  itself up.
- **`prefixMatch` assumes the container is literally named `photos`** (per
  `StorageOptions.BlobContainer` in `appsettings.json`/Function App
  settings). If that ever changes, update `prefixMatch` in the policy JSON
  to match.
- **Changing the retention window here?** Update
  `DeviceEventRetention__RetentionDays` in the Function App settings to
  the same number — see the companion README linked above. No automatic
  link between the two, has to be kept in sync by hand.
