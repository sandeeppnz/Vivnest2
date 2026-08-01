# Architecture Decision Log

Binding rules for the current system. Unlike the roadmap docs, these aren't
meant to change often — treat a violation of one of these as a bug, not a
style preference.

## ADR-001 — Workers never persist directly

Workers (`CameraCaptureWorker`, `AgentHeartbeatWorker`,
`DeviceHeartbeatWorker`) call a dispatcher or handler and stop. They never
call a store/repository themselves.

*Verified:* confirmed in `Vivnest.Agent/Runtime/Workers/*` — every worker's
only persistence-adjacent call is `_dispatcher.PublishAsync(...)` or
`_handler.HandleAsync(...)`.

## ADR-002 — Event handlers own persistence

`IEventHandler<TEvent>` implementations
(`Vivnest.Agent/Runtime/EventHandlers/*`) are the only place that calls
`IDeviceEventWriter`, `IAgentHeartbeatWriter`, or `IDeviceHeartbeatWriter`
(named `Writer`, not `Store` — see the naming note below ADR-008).

*Verified:* confirmed — e.g. `CameraCaptureHandler.HandleAsync` is the only
caller of `IDeviceEventWriter.SaveAsync` in the capture path.

## ADR-003 — Azure Table Storage is the source of truth

Runtime state is disposable and rebuildable; Table Storage entities
(`DeviceEventEntity`, `AgentHeartbeatEntity`, `DeviceHeartbeatEntity`) are
not. If runtime state and Table Storage ever disagree, Table Storage wins.

## ADR-004 — Queue messages carry only PartitionKey and RowKey

`CameraCapturedQueueMessage`, `CameraCapturedFailedQueueMessage`,
`AgentHeartbeatQueueMessage`, `DeviceHeartbeatQueueMessage` are all
`{PartitionKey, RowKey}` — the consumer re-fetches the full entity from
Table Storage rather than trusting a payload carried on the queue.

*Verified:* confirmed across all four queue message types in
`Vivnest.Core/Queues/Models`. This is also why
[current-architecture.md](current-architecture.md)'s flow always shows
Table Storage before Queue — the queue is a pointer, not a payload.

*Consequence:* this is also exactly why the blob-container validation added
to `CameraCapturedHandler` matters — the entity fetched via that pointer is
trusted more than it should be by default, since nothing upstream
guarantees its contents match what the consumer expects.

## ADR-005 — Cloud determines final device health; the agent reports device-level changes it can see firsthand

**Revised** — the original version of this ADR conflated two different
things and got the codebase implication backwards. Corrected below.

Two distinct facts, not one:

- **The agent's own liveness** the agent genuinely cannot detect about
  itself — if the whole `Vivnest.Agent` process or its host dies, no code
  on that host can report it. This is why `AgentHeartbeat` stays
  **periodic and unconditional**: it's the cloud's only way to know the
  agent process itself is alive, and only the cloud (via absence of
  `AgentHeartbeat`) can conclude the agent is down.
- **An individual device's status** (a specific camera or sensor becoming
  unreachable) the agent *can* observe firsthand and in real time — it
  already tracks `LastError` / `LastFailureUtc` / `LastCaptureUtc` per
  device in `DeviceRuntimeState`. There's no reason to make the cloud
  re-derive this from heartbeat gaps when the agent already knows it
  directly.

*Decision:* `DeviceHeartbeat` becomes **event-driven, not periodic** — the
agent evaluates each device's status locally and only sends a
`DeviceHeartbeat` when that device's status actually changes
(online→offline, offline→online), instead of unconditionally every tick.
This cuts heartbeat volume. The cloud still owns the *final, authoritative*
online/offline determination for notification purposes — it does so by
combining two signals: (1) is `AgentHeartbeat` still recent (is the agent
alive at all?), and (2) what was the last device-level status reported. If
`AgentHeartbeat` goes stale, the cloud must treat every device on that
agent as unknown/possibly-offline regardless of the last reported device
status — silence from a live agent means "nothing changed," silence
because the agent died means something else entirely, and only the
`AgentHeartbeat` cross-check can tell those apart.

*Naming collision to watch for:* this creates two different things that
will be tempting to conflate under one name. The **agent-side**
`OfflineDetection` capability (`Vivnest.Agent/Capabilities/OfflineDetection.cs`)
evaluates a device's local status and decides whether to emit a
change-triggered `DeviceHeartbeat`. The **cloud-side** `OfflineDetectionRule`
(planned for `HealthMonitorTimerFunction`, roadmap.md Phase 3 Sprint 1)
makes the final online/offline call and decides whether to notify. They
sound alike, they are not the same component, and one doesn't obsolete the
other — see roadmap.md's Sprint 1 detail.

*Implication for the codebase:* the commented-out `DetermineStatus` method
in `DeviceHeartbeatWorker` was — before this correction — recommended for
deletion as dead code in the wrong layer. That was backwards. It already
computes exactly the kind of per-device status this ADR now calls for
(from `LastError` / `LastActivityUtc`, see ADR-010); it needs to be
**revived and reshaped** to detect a *change* from the previously reported
status (not just recompute current status every tick) and to drive
conditional sending, not deleted.

*Corollary, caught after the first cloud-side implementation shipped:*
"the cloud combines two signals" doesn't mean one function has to do it —
it means the *same evaluation logic* has to run regardless of what
triggered it. The first version only had a Timer sweep, which quietly
ignored the `DeviceHeartbeatQueueMessage` the agent was already publishing
on every status change (agent-side publishing predates this ADR). Fixed by
adding a queue-triggered function alongside the Timer, both calling the
same `IHealthMonitorService` method — Timer for catching agent silence
(the one thing only a periodic sweep can detect) and reconciliation,
Queue for near-instant reaction to an explicit change. See roadmap.md
Sprint 1 for the concrete split.

## ADR-006 — Runtime state is transient and separated from persistence

See [current-architecture.md](current-architecture.md)'s "Runtime State"
section — `DeviceRuntimeState` holds only in-memory, rebuildable fields
(`LastCaptureUtc`, `LastFailureUtc`, `LastActivityUtc`, `LastHeartbeatUtc`,
`LastBlobName`, `LastError`). Nothing durable or business-relevant is
allowed to live only in runtime state.

## ADR-007 — Camera is the first device capability, not the only one

Vivnest's target market spans multiple device types (cameras, water meters,
heat pumps, soil sensors, etc.) and verticals (home, commercial CCTV,
agriculture, industrial IoT) — see
[vivnest-runtime-overview.md](vivnest-runtime-overview.md). `DeviceType`
already reflects this (`Camera`, `HumiditySensor`, `SmokeAlarm`,
`WaterLeak`, `HeatPump`, `MotionSensor`, `DoorSensor`), but `ICamera` /
`CameraCaptureService` are the only implemented capture path, and they're
shaped entirely around "capture an image."

*Decision:* don't generalize the capture abstraction speculatively — that
would be guessing at a shape with only one real example to work from.
Generalize it **when the second device type is actually built**, using
both concrete cases (image capture vs. a scalar/periodic sensor reading) to
find the real shared interface. Treat "what does device type #2 look like"
as a deliberate design question at that point, not something to discover
halfway through an unrelated feature.

*Consequence:* code that's obviously camera-specific by name
(`CameraCaptureWorker`, `ICameraFactory`, etc.) is expected to stay
camera-specific for now — that's not a defect to fix today, it's waiting on
its second data point.

*Forward note, settled ahead of need:* when device type #2 does land,
`DeviceOptions` splits into a base (device-agnostic fields —
`DeviceId`/`Name`/`Type`/`Enabled`, plus `LivenessInterval` since that's
already conceptually generic even though only `ICamera` implements the
probe today) and per-type subtypes (`CameraOptions : DeviceOptions` getting
`Settings.Host`/`RtspUsername`/`RtspPassword`/`SnapshotInterval`; a
hypothetical `WaterMeterOptions` getting its own connection fields and its
own reading-cadence field — not reusing `SnapshotInterval`'s name, since a
meter reading isn't a snapshot). Config binding for this was decided ahead
of time too: **split config sections per type** (`Cameras: [...]`,
`WaterMeters: [...]`, each a strongly-typed list bound independently, merged
by `DeviceRegistry` into one `IReadOnlyCollection<DeviceOptions>`) rather
than one polymorphic `Devices` list needing a custom type-discriminated
binder, or a loosely-typed settings bag. Chosen because it needs no custom
binder code and mirrors how `Tables`/`Messaging` config is already split by
concern — and because this session already hit two real config bugs from
loose typing (unquoted JSON booleans in `local.settings.json`, the
`%HealthMonitor__CronSchedule%` resolution failure), which is reason enough
to keep the second device type's config strongly typed from day one rather
than repeat that mistake. `DeviceRuntimeState`, `IOfflineDetection`,
`DeviceHeartbeatWorker`, and the entire Cloud-side health/notification
pipeline need zero changes when this happens — none of them reference
`ICamera`.

*Worked example, thought-experiment only, no code written:* walking the
above through a concrete `SmokeAlarm` reachable via a local Zigbee/Z-Wave
hub (not a real integration — chosen deliberately to pressure-test the plan
with a device that isn't RTSP-shaped) surfaced two things the field-split
above didn't anticipate:

- **Liveness isn't always a live check.** `ICamera.IsReachableAsync()`
  exists because nothing else knows an RTSP camera's status — the agent has
  to ask directly. A Zigbee/Z-Wave hub already tracks per-device
  online/last-seen status as part of managing its own mesh. A `SmokeAlarm`
  probe would just read a field the hub already exposes
  (`GET /devices/{id}` → `lastSeen`), not perform a network check of its
  own. This confirms `IsReachableAsync()` belongs on `ICamera` specifically
  rather than a shared `IDevice`, for a stronger reason than "different
  protocol": the *category* of operation differs (active probe vs. cached
  status read), not just its implementation.
- **Polling doesn't fit every device.** `CameraCaptureWorker` works because
  a camera has nothing to say until asked. A smoke alarm is the opposite —
  silent until it has something urgent to report, which then needs to be
  heard immediately, not on the next `LivenessInterval` tick. That's
  push/event-driven (a hub webhook or MQTT subscription), not poll-driven.
  So device type #2 landing wouldn't only add a `DeviceOptions` subtype —
  it would likely require a worker *shape* the codebase doesn't have yet: a
  long-lived listener, not a `Task.Delay` loop like every current worker
  uses. That's a bigger finding than the field-split above accounted for,
  and a genuine reason the real second device type still needs to inform
  this design directly rather than trusting this note as final.

## ADR-008 — Multi-tenancy is a day-one constraint, not a later migration

`TenantId` / `SiteId` / `AgentId` are already on every domain event and
heartbeat (`DeviceEvent`, `AgentHeartbeat`, `DeviceHeartbeat`), because the
commercial target (managing many customers' independent sites, not just one
household) was anticipated from the start.

*Decision:* the REST API and dashboard (roadmap.md Phase 3, Sprints 4-5),
when built, must be tenant-scoped from their first version — every query
filtered by tenant, no endpoint that can return data across tenants without
explicit intent. Retrofitting tenant isolation into an API that was built
single-tenant is a much bigger job than building it in from the start,
and there's no forcing function to catch the mistake later (nothing in the
current architecture rejects a cross-tenant query — it has to be enforced
at the API layer deliberately).

## ADR-009 — Per-entity data-access types are named `Writer`/`Reader`, not `Store`/`Repository`

Every persisted entity type that both the Agent and Cloud sides touch
(`AgentHeartbeat`, `DeviceHeartbeat`, `DeviceEvent`) has two independent
data-access types: an Agent-side one that creates the data
(`AgentHeartbeatWriter`, `DeviceHeartbeatWriter`, `AzureTableDeviceEventWriter`
in `Vivnest.Infrastructure`) and a Cloud-side one that only reads it plus
makes narrow, targeted status updates (`AzureTableAgentHeartbeatReader`,
`AzureTableDeviceHeartbeatReader`, `AzureTableDeviceEventReader` in
`Vivnest.Cloud`). `Vivnest.Cloud` and `Vivnest.Infrastructure` deliberately
don't reference each other, so these can never be the same type — but they
were briefly named as if they could be (`...Store` on one side, `...Repository`
on the other), which is a real problem the moment the Cloud-side type gets
renamed toward consistency: `IAgentHeartbeatRepository` (Cloud) and a
renamed `IAgentHeartbeatStore` → `IAgentHeartbeatRepository` (Agent) would
share an identical name across two different namespaces with two
completely different method signatures — discoverable only by checking
which `using` is in scope. Caught before it was built, not after.

*Decision:* name these types after the actual behavioral split —
**`Writer`** for the Agent side (owns creation, does the full
save/replace), **`Reader`** for the Cloud side (never creates rows, only
observes and narrowly updates). This makes the real distinction visible in
the name instead of hiding it behind two arbitrary synonyms that happen to
mean the same thing. Applies to `IAgentHeartbeatWriter`/`IAgentHeartbeatReader`,
`IDeviceHeartbeatWriter`/`IDeviceHeartbeatReader`, and
`IDeviceEventWriter`/`IDeviceEventReader`. Any new entity type that gets a
data-access type on both sides should follow the same pattern.

## ADR-010 — Liveness, capture, and notification are three independent cadences, not one

Sprint 2 ("Scheduled Snapshot") started from a false premise: that
`CameraCaptureWorker`'s existing `LivenessInterval` loop already was the
scheduled-snapshot feature. Tracing the pipeline showed `LivenessInterval`
was silently overloading three unrelated concerns onto one interval and one
unconditional forward-to-Telegram pipeline:

1. **Liveness** — is the camera reachable at all.
2. **Snapshot capture** — when a full frame actually gets grabbed, uploaded
   to blob, and persisted as a `DeviceEvent`.
3. **Telegram notification** — when a captured snapshot actually reaches
   the user as a photo message.

A short `LivenessInterval` (good for liveness) meant a Telegram photo every
few minutes whether the user wanted one or not, because all three concerns
fired on the same tick. *Decision:* split them, each owned where the
relevant data/authority already lives:

- **Liveness → Agent, lightweight, on `LivenessInterval`.** `ICamera`
  gained `IsReachableAsync()` — for `RtspCamera`, a raw TCP connect to the
  RTSP port with a short timeout, no `ffmpeg` process, no frame decode.
  `CameraCaptureWorker` runs this on every `LivenessInterval` tick when a
  full capture isn't due, updating `DeviceRuntimeState.LastActivityUtc`.
  `OfflineDetection.Evaluate` switched from reading `LastCaptureUtc` to
  `LastActivityUtc` — liveness accuracy no longer depends on how often a
  full snapshot happens. `LastError` stays owned solely by the capture
  path (a probe failure doesn't set it — the staleness math on
  `LastActivityUtc` surfaces a dead camera as `Warning` on its own).
- **Snapshot capture → Agent, on `DeviceOptions.SnapshotInterval`.**
  `CameraCaptureWorker`'s per-tick decision: if `SnapshotInterval` has
  elapsed since `LastCaptureUtc` (or it's unset/zero), do the real
  `CaptureAsync` (ffmpeg + blob upload + `DeviceEvent`); otherwise just
  probe. Zero/unset `SnapshotInterval` collapses back to "capture every
  `LivenessInterval` tick," matching pre-existing behavior with no config
  migration needed. `CameraCaptureHandler` (Agent) forwards **every**
  capture's `DeviceEvent` to the `camera-captured` queue unconditionally —
  no agent-side notification throttling. An earlier version of this
  decision gated the queue publish agent-side; reverted in favor of the
  point below once it became clear notification cadence needed to be
  changeable without redeploying the agent, and needed to stay purely
  event-driven to avoid ever re-sending a stale image.
- **Telegram notification → Cloud, on `SnapshotNotificationOptions.MinInterval`.**
  `CameraCapturedHandler` (Cloud) gained a `NotificationState`-style dedup
  gate — new `IDeviceSnapshotStateReader` / `tblDeviceSnapshotState` track
  `LastNotifiedUtc` per device — checked on every arriving capture event
  before dispatching to Telegram. This only works safely because it's
  strictly event-driven, never a Cloud-side poll/timer: the gate decides
  whether to forward *this* newly-arrived capture, it never reaches back
  to resend a previous one, so there's no way to send the same photo
  twice regardless of how `SnapshotInterval` and `MinInterval` relate to
  each other.

*Generalizes for free, mostly:* `DeviceRuntimeState.LastActivityUtc`,
`OfflineDetection.Evaluate`, and `DeviceHeartbeatWorker` never reference
`ICamera` — a second device type gets offline detection for free just by
having its own worker update `LastActivityUtc`. What doesn't generalize is
the probe *mechanism itself* — a TCP connect means nothing to a Modbus
water meter — so `IsReachableAsync()` stays on `ICamera` rather than a
premature shared `IDevice` interface, per ADR-007's rule of thumb. Extract
that shared shape when a second device type actually needs one.
