# Model Registry — design (ADR-124)

**Status:** accepted 2026-08-30 · **Builds on:** ADR-064/066 (split ROI
projection), ADR-069 (immutable config versions), ADR-085 (no plaintext
secrets in blobs), ADR-122 (models mount), ADR-123 (shared-queue fix)

## Why

The first High-agent install (2026-08-30) proved the model-file story is
operator folklore, the exact failure class ADR-119/120/121 eliminated
for catalogue/config/publish:

1. Model files were copied by hand into a magic host folder.
2. The sink model silently needed TWO files (`.onnx` + external-data
   `.onnx.data`) — discovered only when ONNX Runtime failed at
   inference time.
3. `ModelPath` is a free-text container path on the capability
   assignment; a typo fails at runtime, and the classifier caches the
   failure until a container restart.
4. Replacing a file in-place changes behavior invisibly — no versioning,
   no rollback, no "which model produced this verdict."
5. The dashboard cannot see what models exist or whether an agent has
   them.

## What this is (and is not)

A **model registry**: named models with immutable, versioned **file
sets**, uploaded through the dashboard, referenced from capability
assignments by id, and fetched by agents automatically.

Explicit non-goals, per the second-real-consumer rule:

- **No "Services" entity.** "Who executes" is already modeled — the
  High-type agent, addressed per-capability via `ExecutingAgentId`
  (ADR-036). `CapabilityType.Service` also already means something else.
  If a second executor kind ever exists, Models slots under it cleanly.
- **No training/eval/experiment tracking, no A/B or staged rollout, no
  network model-serving.** Agents pull files; edge-first.

## Concepts

**Model** — a named catalogue entry ("Sink Cleanliness Classifier").
Tenant-scoped: models are trained on the tenant's own imagery, so they
are tenant data, not global reference data like the capability
catalogue.

**ModelVersion** — an immutable, monotonically numbered (`1, 2, …`)
**file set**:

- One **primary** file — exactly one `.onnx` per version, enforced at
  upload. This is the file the inference session opens.
- Zero or more **companion** files (e.g. `.onnx.data`). ONNX Runtime
  resolves external data relative to the primary file's directory, so
  keeping the set together in one versioned folder makes the two-file
  gotcha structurally impossible.
- Per file: name, size, **SHA-256** (computed server-side at upload —
  the registry is the authority; agents verify against it after
  download).
- `Status`: `Active` | `Retired`. Versions are never edited or deleted;
  retiring hides a version from default resolution without breaking
  history (same "Retired, not deleted" doctrine as DeviceStatus).
- Free-text `Notes` ("trained on the August batch").

## Storage

- `tblModels` — PartitionKey `{tenantId}|{siteId}`, RowKey `ModelId`
  (GUID). Name, Description, Status, timestamps.
- `tblModelVersions` — PartitionKey `{tenantId}|{siteId}|{modelId}`,
  RowKey = zero-padded version (`D6`, matching config-version rows).
  Status, Notes, UploadedUtc, and the file set serialized as JSON
  (`[{Name, SizeBytes, Sha256, IsPrimary}]`).
- Blob container `models`, layout `models/{modelId}/v{N}/{fileName}`.
  Immutable once the version row exists — a re-upload is a new version,
  never an overwrite (ADR-069's rule, applied to artifacts).
- Both table names get canonical defaults in `TablesOptions` (ADR-120).

## Upload

Through the Functions API (`POST models-admin/{modelId}/versions`,
multipart form), developer-gated like every admin route. The version
number is assigned server-side (max existing + 1); blobs are written
first, hashed as they stream, and the version row is written last — a
crashed upload leaves orphan blobs but never a row pointing at missing
files (rows are the source of truth; orphans are harmless).

Practical cap ~25 MB/file (ASP.NET Core default body limits; today's
largest model is 13 MB). The documented escape hatch, when a model
outgrows that: cloud mints a **write SAS** and the browser uploads
directly to blob storage — deferred until a real model needs it, since
it drags in storage-account CORS configuration.

## Referencing & pinning

ROI capability assignments (Object Detection, Sink Cleanliness) carry:

- `ModelId` — required going forward (replaces `ModelPath`).
- `ModelVersion` — optional pin. Absent means **latest Active**.

**Resolution happens at publish time**, not assignment time or run
time: `ModelReferenceResolver` runs just before projection (the two
`Project()` call sites in the agent/device configuration projectors)
and augments the assignment's settings with the resolved
`ModelVersion` + file manifest. The published agent config therefore
snapshots the exact version — so:

- Activating a new version does nothing until a publish (deliberate —
  publish is the one gesture that changes runtime behavior, ADR-121).
- Rolling back a config version rolls back the model reference with it.
- Every classification is attributable to a model version via the
  config version that was live.

A dangling reference (unknown ModelId, no Active version, pinned
version missing) is a projection **Warning** — it blocks publish with a
human-readable reason, exactly like a missing `ConfidenceThreshold`
does today. Publish-time is the last moment the mistake is cheap.

**Wire shape**: the projected per-device model settings remain a flat
`Dictionary<string, string>` (the existing agent contract —
`AiDeviceClassificationEntryDto` and the options binder both assume
it). New keys: `ModelId`, `ModelVersion`, and `ModelFiles` — the file
manifest as a JSON **string** (`[{"Name","SizeBytes","Sha256","IsPrimary"}]`),
parsed by the agent's fetcher, not the config binder. A typed nested
shape was rejected: it would fork the wire contract for one field.

**Legacy**: `ModelPath` keeps working (projector requires ModelId OR
ModelPath) so existing assignments don't break mid-migration; it emits
a deprecation Warning-free pass now, and gets removed once no
assignment uses it.

## Agent-side fetch

`ModelProvisioner` (High-type agent), invoked lazily on the first
classify per (ModelId, ModelVersion) — not at startup, so a fetch
problem on one model never delays the host or the other model:

1. Cache dir: `{BaseDirectory}/models-cache/{modelId}/v{N}/` — inside
   the container, ephemeral. Lost on redeploy, re-downloaded on first
   use (~20 MB; acceptable). The ADR-122 `ModelsPath` host mount stays
   only as the legacy `ModelPath` root and disappears with it.
2. For each manifest file: if present with matching size + SHA-256,
   keep; else download from `models/{modelId}/v{N}/{name}` via the
   blob client the agent already has, then verify the hash. Mismatch →
   delete + one retry; still bad → fail this attempt.
3. Failure emits an **AgentEvent** ("model {name} v{N} fetch failed:
   …") and skips that classification — and is retried on the next
   classify message. Fetch failures are never cached permanently (the
   restart-to-recover trap from the install). The inference session
   cache stays, but its key is the resolved versioned path, so a new
   version is a new key — no restart needed to pick one up.

`EffectiveModelPath` on the two `*ModelOptions`: the cache path to the
primary file when `ModelId` is set, else the legacy `ModelPath`.

## Dashboard

Admin → **Models**: list models with their versions (number, status,
files, sizes, hashes, notes), create model, upload version
(multi-file), activate/retire a version, copy ModelId. The assignment
editor keeps its schema-driven form — `ModelId` is a text field seeded
into the two ROI capability schemas (a picker is a later nicety).

## Migration of the live system

1. Deploy; create "Object Detection (YOLOv8n)" and "Sink Cleanliness
   Classifier", upload today's files from `C:\vivnest\models` as v1 of
   each.
2. Update the two camera assignments: drop `ModelPath`, set `ModelId`.
3. Publish all & refresh; confirm fetch + verdicts; then the host
   models folder and its mount are dead weight to remove with the
   legacy path.
