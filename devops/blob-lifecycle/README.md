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
- **This only deletes the blob, not its metadata row.** The
  `DeviceEvent`/`DeviceEventEntity` row in Table Storage (`tblDeviceEvents`)
  that references a deleted capture is untouched — Azure Table Storage has
  no native TTL/expiration the way this Blob feature does. Once a blob is
  deleted here, that row's image link in the dashboard will start
  pointing at a blob that no longer exists (a broken thumbnail, not a
  crash). A companion cleanup — a Timer-triggered Cloud Function deleting
  old `DeviceEvent` rows, same shape as `HealthMonitorTimerFunction` — is
  still needed to fully close this out and hasn't been built yet.
- **`prefixMatch` assumes the container is literally named `photos`** (per
  `StorageOptions.BlobContainer` in `appsettings.json`/Function App
  settings). If that ever changes, update `prefixMatch` in the policy JSON
  to match.
