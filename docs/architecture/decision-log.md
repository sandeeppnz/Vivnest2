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

*Follow-up: the agent itself now gets a direct offline/online
notification, not just the per-device cascade.* A user asked whether
"agent offline" produces a Telegram alert at all — until now it only did
so indirectly: `DetermineFinalStatus` treats every device on a stale agent
as offline, so an agent outage surfaced as N separate `DeviceOffline`
messages (one per device), never a single "Agent X is offline" message,
and recovery was the same — no "Agent X is back online" message, just
each device's own recovery notification once it next reported healthy.
Investigating turned up something already half-built: `AgentHeartbeatHandler`
(`Vivnest.Agent/Runtime/EventHandlers`) has published every heartbeat tick
to an `agent-heartbeats` queue since this ADR's original implementation —
nothing ever consumed it, the same "queue exists, nothing reads it" state
`device-events` was in before ADR-004's handler was built.

Fixed by extending the same Timer+Queue split this ADR already
established, applied to agents instead of devices — deliberately *not* a
new mechanism: `HealthMonitorService.RunAsync` (Timer sweep) now also
evaluates every agent for the offline transition (silence-based, the one
thing only a periodic sweep can catch — an agent can't self-report going
offline, only recovery), and a new `AgentHeartbeatChangedFunction`
(queue-triggered on `agent-heartbeats`, mirroring
`DeviceHeartbeatChangedFunction`) reacts near-instantly to a heartbeat
arriving while the agent was still marked offline — that's the recovery
case. Reused rather than duplicated: `IOfflineDetectionRule`/
`IRecoveryDetectionRule` (already generic over `DeviceHeartbeatStatus`/
`DeviceNotificationState`, no agent-specific type needed — an agent's
status is just a synthesized Online/Offline), and `DeviceNotificationState`
itself for agent notification state. `AgentHeartbeatEntity` gained the
same three fields `DeviceHeartbeatEntity` already had
(`NotificationState`, `LastOfflineNotificationUtc`, `LastRecoveredUtc`) —
agents had never needed them before because nothing evaluated agent-level
transitions directly. Two new `NotificationTypes`: `AgentOffline`,
`AgentRecovered`. The per-device cascade notifications are unchanged and
still fire alongside these — this adds the missing single "Agent X is
offline/back online" message, it doesn't replace the per-device ones.

*Second follow-up: the agent-level check and the device-cascade check
split onto two different staleness thresholds, not one shared one.*
Initially both reused the same `AgentStaleMultiplier`-buffered check
(`HeartbeatInterval × AgentStaleMultiplier`, 3x by default). Revisited
after testing showed the multiplier's real purpose is specifically to
protect the device cascade: a false positive there means every device on
the agent flips to Offline, one notification each — a false positive on
the single direct agent-level notification is just one spurious message
that self-corrects on the very next heartbeat, cheap enough not to need
the same buffer. Split into two methods in `HealthMonitorService`:
`IsAgentStaleForDeviceCascade` (unchanged, still `×AgentStaleMultiplier`,
feeds `DetermineFinalStatus`) and `IsAgentOffline` (new, bare
`HeartbeatInterval`, no multiplier, feeds `EvaluateAgentAndNotifyAsync`).
`AgentQueryService.ToDto` (the `/agents` API's `Status`/`StatusSinceUtc`)
switched from the multiplied check to the bare one to match — it needs to
agree with whichever check actually produced `LastRecoveredUtc`, not with
the cascade check, since the dashboard is showing the agent's own status,
not a device's.

*Third follow-up: the same buffer-vs-no-buffer question, one level down —
per-device now, not just per-agent.* Agent-side `OfflineDetection.Evaluate`
(this ADR's original agent-side half) had a hardcoded 2x buffer on
`DeviceOptions.LivenessInterval` before marking a device `Warning` — fine
as a global default, but some devices genuinely warrant tighter detection
than others (the reasoning mirrors the agent-level split two entries up:
not every device's false-positive is equally cheap to tolerate). Made
configurable per-device rather than a single global setting:
`DeviceOptions.WarningMultiplier` (`double`, default `3.0`, replacing the
hardcoded 2x), threaded through `IOfflineDetection.Evaluate`'s new third
parameter. Set to `1` (no buffer) per-device in `Devices[]` for anything
where fast detection matters more than avoiding an occasional false
positive from a single delayed liveness probe.

*Fourth follow-up: a device could show (and notify) recovered before it
had actually reported anything, once the agent came back.* Asked directly:
"if the agent is online after a possible recovery, what should the
recovery state of a device be until it's reported online?" Tracing the
code found the cascade (agent stale → every device shown Offline) is
purely computed at read time, never written back to
`DeviceHeartbeatEntity.Status` — so the instant the agent's heartbeat is
fresh again, `Status` falls through to whatever the device last reported
*before* the outage, almost always "Online," even though that specific
device hasn't proven anything since the agent came back. A device that's
genuinely still broken (camera unreachable, plug unplugged) would show,
and notify, "recovered" purely because the agent process returned.

Fixed by adding a third outcome, `Unknown`, for exactly that gap: if the
agent has a recorded `LastRecoveredUtc` and the device's own
`LastHeartbeatUtc` predates it, the device hasn't reported anything since
the recovery, so its old `Status` isn't trusted — shown/notified as
`Unknown` (not optimistically Online, not pessimistically Offline) until
the device reports something dated after the recovery. No new
notification type needed: `Unknown` isn't Offline or Online, so neither
`IOfflineDetectionRule` nor `IRecoveryDetectionRule` fires for it — it's
silent until the device's own report resolves it one way or the other.

Also fixed in the same change: this status logic (`DetermineFinalStatus`)
was duplicated near-verbatim in both `HealthMonitorService` (drives
notifications) and `DeviceQueryService` (drives the `/devices` API/
dashboard) — exactly the kind of duplication that lets two call sites
silently disagree over time. Extracted into one shared
`IDeviceStatusResolver`/`DeviceStatusResolver`
(`Vivnest.Cloud/Interfaces`, `Vivnest.Cloud/Rules`), injected into both.

*Fifth follow-up, closing the two gaps flagged above.* Requirement stated
plainly: agent offline/online should notify immediately, no delay (already
true - see the second follow-up above); devices should still notify
independently when *only* a device dies and the agent is fine; but an
agent outage shouldn't also fire a `DeviceOffline` per device, since the
`AgentOffline` message already implies all of them; and device recovery
notifications need to survive several arriving in a burst without Telegram
throwing a rate-limit error.

Redundant per-device notifications suppressed at the source: `DeviceStatusResult`
(`Vivnest.Cloud/Interfaces/IDeviceStatusResolver.cs`) now carries an
`AgentCascade` flag alongside `Status` - true exactly when the Offline
came from the agent-staleness cascade rather than the device's own report.
`HealthMonitorService.EvaluateAndNotifyAsync` returns early on
`AgentCascade` without touching `NotificationState` at all, so a device
that was healthy before the agent died fires nothing (correctly - nothing
new happened, the agent alert already said so), and a device that was
already `OfflineNotified` before the agent also died stays that way,
so its *own* eventual genuine recovery still notifies correctly later,
independent of the agent's.

Telegram burst-of-recoveries handled with actual retry/backoff, not
throttling: `TelegramService.PostWithRetryAsync` (`Vivnest.Cloud/Services`)
now retries up to 3 times on HTTP 429, honoring Telegram's own
`parameters.retry_after` from the response body when present (falling
back to a fixed 2s delay if it's missing) - the correct way to handle
Telegram's flood control per its own API contract, rather than
guessing a send-rate cap upfront. Covers `SendMessageAsync` and
`SendPhotoAsync(byte[])` (the two paths `TelegramNotificationChannel`
actually calls); the unused `SendPhotoAsync(Stream)` overload was left
as-is. This was flagged as recommendation #2 much earlier in this
session's work (a 429 leaving `NotificationState` unchanged could
silently stall the offline→recovery chain) and stayed unimplemented
until now.

*Sixth follow-up: the same "Status since" dashboard column added for
agents got added for devices too, once `DeviceStatusResult` already
existed to hang it off.* `StatusSinceUtc` (`DateTime?` - unlike the
agent version, not always resolvable) computed alongside `Status`/
`AgentCascade` in the same `Determine` call: agent-cascade Offline uses
`agent.LastHeartbeatUtc` (the agent's own last confirmed-alive moment,
since that's actually what made this device untrustworthy, not anything
the device itself did); the post-recovery `Unknown` gap has no honest
answer, so `null`; otherwise `device.LastHeartbeatUtc` doubles as "since
when," for free - `DeviceHeartbeatWorker`/`HomeAssistantLivenessTracker`
only write a new row when status actually changes, so that timestamp
already *is* the transition time, no separate bookkeeping needed. Also
added in the same pass: `DeviceSummaryDto.HeartbeatInterval`, sourced
from `DeviceHeartbeatEntity.ExpectedLivenessInterval` - a field that's
been round-tripped since ADR-005 but never read by anything, until now.

*Seventh follow-up: a critical, previously-undiscovered bug found while
extending `AgentHeartbeatWriter` for the HA-connection-cascade work below
- agent/device heartbeat writes were silently erasing the exact
notification bookkeeping this whole ADR chain depends on.*
`AzureTableStore<T>.UpsertAsync` always uses `TableUpdateMode.Replace`,
which overwrites the *entire* row with whatever the C# object provides -
any server-side property not included gets wiped. `AgentHeartbeatWriter.SaveAsync`
built a fresh `AgentHeartbeatEntity` from scratch on every single
heartbeat tick (every `AgentHeartbeat:HeartbeatInterval`, unconditionally)
and never included `NotificationState`/`LastOfflineNotificationUtc`/
`LastRecoveredUtc` - those are Cloud-only fields the agent's domain model
doesn't even have. So the moment `HealthMonitorService` set
`NotificationState = OfflineNotified`, the agent's very next heartbeat
write (within a minute) silently reset it back to null - meaning
`ShouldNotifyRecovery` could never see the state it needs to fire, ever.
This is exactly what querying `tblAgentHeartbeat` showed a few turns
earlier: no `NotificationState` property at all, despite the offline/
recovery logic having run. `DeviceHeartbeatWriter.SaveAsync` had the
identical shape of bug, just triggered less often (only on real device
status changes, not every tick, since that path is already event-driven -
still a real bug, just narrower blast radius).

Fixed by having both writers read the existing row first and carry the
three Cloud-owned fields forward, rather than trusting whatever the
agent-side domain object (which never populates them) provides - one
extra Table read per heartbeat write, negligible at current volume.
Verified as a genuine root cause, not a guess: this exact bug is what
made the agent-level offline/recovery notification feature (the second
follow-up above) unreliable since it was built, and would have
undermined the HA-connection-cascade suppression logic below the same
way if left unfixed while extending the same writers for
`HomeAssistantLastConnectedUtc`.

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
concern — and because this session already hit a real config bug from loose
typing (unquoted JSON booleans in `local.settings.json`), which is reason
enough to keep the second device type's config strongly typed from day one
rather than repeat that mistake. `DeviceRuntimeState`, `IOfflineDetection`,
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

*This session's rule of thumb reached the frontend too, not just the
backend.* `Vivnest.Dashboard`'s device detail page has a `CaptureGallery`
component gated behind `device.deviceType === "Camera"` — a plain
conditional, not a `DeviceType → Component[]` registry or plugin system.
Same reasoning as everywhere else in this ADR: there's still only one
device type with type-specific UI, so a registry would be guessing at how
a second type's UI needs differ before any second type exists to ask. The
gate costs nothing and directly prevents a real near-term bug (an empty
photo gallery on a device that has no photos); the registry would be
solving a problem nobody has yet. Revisit when a second device type
actually has its own type-specific section to show (e.g. a "Readings"
chart for a water meter) — with two real examples in hand, not before.

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

## ADR-011 — `TimeSpan` table entity properties must be stored as strings, never as `TimeSpan`

`Azure.Data.Tables` 12.11.0 writes `TimeSpan` entity properties as ISO-8601
duration strings (e.g. `PT1M` for one minute) but its own strongly-typed
deserializer can't read that format back — `TimeSpan.Parse("PT1M")` throws
`FormatException`, which the SDK swallows per-property rather than
propagating, silently defaulting the property to `TimeSpan.Zero` on every
read. Every *other* property on the same entity deserializes correctly;
only `TimeSpan` breaks, silently, with no error surfaced anywhere.

*Found via:* the Agents dashboard tab showing every agent's heartbeat
interval as zero. Traced by writing a real heartbeat, then inspecting the
raw stored value directly (a throwaway console app using `TableEntity`,
bypassing the strongly-typed model) — confirmed the stored value was the
correct `PT1M`, and confirmed `TimeSpan.Parse("PT1M")` throws directly, so
this wasn't stale data or a display bug, it was the SDK's own read path.

*Real impact, not just cosmetic:* `HealthMonitorService.DetermineFinalStatus`
reads `AgentHeartbeatEntity.HeartbeatInterval` directly (no mapping layer
between Cloud's Reader and this check) to compute
`HeartbeatInterval * AgentStaleMultiplier` as the agent-staleness threshold.
Since that read has always silently returned zero, this calculation has
always fallen through to the flat 5-minute fallback instead — meaning
`AgentStaleMultiplier` and the configured `AgentHeartbeat.HeartbeatInterval`
have never actually influenced agent-staleness detection, for as long as
this code has existed. Three properties across two entities were affected
(`AgentHeartbeatEntity.HeartbeatInterval`,
`DeviceHeartbeatEntity.ExpectedLivenessInterval`/`ExpectedHeartbeatInterval`)
— the latter two had no reader yet, so no behavioral impact surfaced from
them, but they were an identical landmine waiting for a consumer.

*Decision:* no `TimeSpan`-typed property is allowed on a table entity class
in `Vivnest.Core.DataStores.Entities`. Store as `string`
(`TimeSpan.ToString()`) instead, converted at the read/write boundary via
`Vivnest.Core.Storage.TableTimeSpan.ToStorageString()`/`.Parse()` — the
latter uses `TimeSpan.TryParse` with a safe zero fallback rather than a
throwing `Parse`, both to handle old rows still holding the broken
ISO-8601 format (self-healing the next time that row is genuinely
rewritten, since every Writer upserts the full entity) and so a single bad
value can't take down a whole request. Domain models
(`Vivnest.Core.Domain.*`) keep real `TimeSpan` properties — only the
Table Storage boundary needs this workaround, and it should stay
contained there, not leak into anything strongly typed elsewhere.

## ADR-012 — REST API auth is two-tier (tenant key vs. host key), and permissions are a plain bool until a second dimension is real

The REST API (roadmap.md Sprint 4) needed an auth answer before any of its
four read endpoints could be built, and later needed a second answer once
the dashboard needed to be shared with flatmates without building user
registration.

**Tier 1 — tenant-scoped API keys, for reading data.** `ApiKeyEntity`
(`tblApiKeys`) maps a SHA-256 hash of a randomly generated key to a
`{TenantId, SiteId, Enabled, DevicesOnly}` row. Every read endpoint
(`/devices`, `/agents`, `/devices/{id}/events`, `/devices/{id}/captures`,
`/whoami`) requires a valid, enabled key via the `x-api-key` header,
resolved by `IApiKeyAuthenticator` into a `TenantContext`. This is
deliberately a bearer-token model, not real user accounts — see below for
why that's the right size for the actual audience (a handful of trusted
flatmates, not the public).

**Tier 2 — the Function host key, for managing keys.** `POST /apikeys`,
`GET /apikeys`, and `POST /apikeys/{keyId}/revoke` are gated by
`AuthorizationLevel.Function` (an Azure Functions host key) instead of the
tenant scheme — minting, listing, or revoking keys is an operator-only
action. A caller holding one tenant's read key must not be able to see or
kill every other key for that tenant; using the same tenant scheme for both
would allow exactly that.

**Decision, explicitly rejected: full user registration.** Considered and
declined when the actual need surfaced ("give flatmates access to
photos"). Registration/login/password-reset infrastructure solves
self-service signup for people you don't know — this is a fixed, small,
trusted household group. Building it would be the same premature
generalization ADR-007 and ADR-010 already reject elsewhere in this
codebase, just applied to auth instead of device capture.

**Decision: permissions are `bool DevicesOnly`, not a `Role` enum or
general permission model.** There is exactly one real distinction to make
today — can this key see the Agents tab (system/operator internals) or
not. A `Role`/permissions system would mean inventing categories with zero
second requirement to inform their shape (per-device scoping? read vs.
write? nobody has asked for either). Enforced server-side on every gated
endpoint (`AgentsFunction` returns 403 for a `DevicesOnly` key, not just a
hidden dashboard tab — hiding UI without a server-side check would let
anyone call the API directly and see it anyway). `TenantContext` carries
`DevicesOnly` end to end so any future endpoint can check it the same way.
Revisit only if a second, orthogonal permission dimension becomes real —
not for a second imagined tier of the same dimension.

**`KeyId` is a separate, non-secret handle from the key material itself.**
`ApiKeyEntity.KeyId` (a GUID) exists purely so `GET /apikeys` and
`POST /apikeys/{keyId}/revoke` never need to expose or accept the actual
key hash — `ApiKeySummary` (the list response) deliberately omits it
entirely. Looked up via a full-table scan filtered on `KeyId`
(`AzureTableStore<T>.QueryAsync`), not a second partition-keyed index —
fine at the scale of a handful of admin-managed keys, not worth a real
secondary-index design for.

**`DevicesOnly` defaults to `false` (unrestricted), not `true`
(restricted), for a specific backward-compatibility reason:** Azure Table
Storage returns the CLR default for any property absent from a stored row,
and every key created before this field existed has no `DevicesOnly`
column at all. A default of `false` means those pre-existing keys keep
their original full access after this change ships, with no migration
needed — the alternative (default `true`) would have silently downgraded
every existing key's access the moment this shipped. `KeyId` didn't get
the same grace: keys created before that field existed have no `KeyId` at
all and genuinely can't be looked up or revoked through the new endpoints
— reissuing is the only fix, not a gap worth building a migration for at
this key count.

## ADR-013 — the REST API's device status must re-derive the final status, not echo the last-reported one

Found via the dashboard: an agent process going silent showed correctly as
Offline on the Agents tab, but every device under that agent still showed
Online.

**Root cause.** `DeviceHeartbeatEntity.Status` is the device's own
last-*reported* status — written by the agent, event-driven, only on an
actual status change (ADR-005). When the agent process itself dies, nothing
updates that row, because the agent that would notice and report "this
device is now unreachable" is the same process that's no longer running.
`HealthMonitorService.DetermineFinalStatus` already knew this and layers an
agent-staleness check on top before deciding the *authoritative* status
(if the owning agent's heartbeat has gone stale, every device it owns is
Offline/Unknown regardless of what it last self-reported) — but that logic
only ran on the notification path. `DeviceQueryService.ToDto` (the REST
read path backing `/devices` and `/devices/{id}`) returned
`entity.Status` verbatim, never applying the same override. Same class of
bug `AgentQueryService` had already avoided — its `ToDto` comment
explicitly says it mirrors `DetermineFinalStatus` for exactly this
reason — `DeviceQueryService` just didn't get the same treatment when it
was first built (Sprint 4).

**Fix:** `DeviceQueryService` now takes `IAgentHeartbeatReader` and
`IOptions<HealthMonitorOptions>` and computes the same final status
(`DetermineFinalStatus`, duplicated rather than extracted — see below) for
every device it returns, fetching the owning agent's heartbeat alongside
the device's.

**Duplicated logic, not extracted to a shared helper — deliberately, for
now.** The same agent-staleness formula now exists in three places
(`HealthMonitorService`, `AgentQueryService`, `DeviceQueryService`).
`AgentQueryService` already established the precedent of duplicating
rather than sharing when this exact question came up in Sprint 5; this
follows the same call for consistency. Worth extracting into one helper
if a fourth consumer needs it, or if the three copies ever drift — not
before.

## ADR-014 — Cloud side deploys as a plain Azure Function App and Static Web App, not containers

Considered containerizing `Vivnest.Cloud.Functions` (Azure Functions
supports custom containers on Premium/Dedicated plans or Azure Container
Apps) when deployment came up. Declined: Consumption plan — the correct
tier for this traffic level (a handful of household users) — doesn't
support custom containers at all, so containerizing would have forced a
move to a paid Premium/Container Apps plan (~$150+/mo minimum) plus new
tooling this project doesn't otherwise need (a Dockerfile, an image
registry, a build/push step), for zero present benefit. Same
"don't generalize ahead of a real need" call as ADR-007/ADR-010/ADR-012,
applied to deployment shape instead of code shape.

**The Agent is a different question, deliberately left open.**
Containerizing `Vivnest.Agent` doesn't have the same cost objection — it'd
just need Docker on whatever host runs it — and the target architecture
(JOURNEY.md Stage 5b / roadmap.md Phase 6B) already names containerized
agents as where this is headed. Not done yet because there's no second
host to deploy it to (no Raspberry Pi or dedicated box in hand at time of
writing) — worth doing once that hardware exists, not before.

**Deployed, concretely:** `Vivnest.Cloud.Functions` → Azure Function App
`vivnestcloudprod` (resource group `rg-vivnest-dev`, New Zealand North).
`Vivnest.Dashboard` → Azure Static Web App `vivnest-dashboard` (East
Asia — the closest region Static Web Apps is actually offered in; it
isn't available in New Zealand North), deployed via the SWA CLI's
token-based `swa deploy` rather than the GitHub Actions-linked flow —
no CI pipeline exists for this repo yet and one manual `swa deploy` per
dashboard change is an acceptable cost until that stops being true.

## ADR-015 — the second device type (SmartPlug) does not reuse `ICamera`, confirming ADR-007's prediction

Stage 2 (JOURNEY.md) landed: a TP-Link Kasa smart plug (HS110, on the
local network as `plug-001`) is now a real, working second device type,
not just a config label. This directly tested the question ADR-007 left
open — does a second device type generalize onto `ICamera`, or does it
need its own shape?

**It needed its own shape.** `ISmartPlug` (`GetStateAsync()` returning
on/off + power/voltage/current + brand/model/firmware, `IsReachableAsync()`
for liveness) shares no code with `ICamera`
(`CaptureAsync()` returning an image stream). Forcing a plug through
`ICamera` would have meant a `CaptureAsync()` that doesn't capture
anything image-like — the wrong abstraction, not a simplification. Built
instead: `ISmartPlug`/`ISmartPlugFactory` (`Vivnest.Core/SmartPlug`),
`KasaSmartPlug`/`SmartPlugFactory` (`Vivnest.Infrastructure/SmartPlug`),
`ISmartPlugMonitorService`/`SmartPlugMonitorService` (Agent orchestration,
mirrors `ICameraCaptureService`/`CameraCaptureService`), and
`SmartPlugMonitorWorker` (mirrors `CameraCaptureWorker`'s liveness/capture
split from ADR-010 — same cadence pattern, applied to a second device type
for the first time).

**What carried over unchanged, for free:** `DeviceHeartbeatWorker`,
`OfflineDetection`, `IDeviceEventWriter`, and the Cloud-side read API all
operated on generic `DeviceRuntimeState`/`DeviceEvent` fields already —
none of them needed a single line changed for a second device type to
start flowing through them. This is exactly the split
[current-architecture.md](current-architecture.md) predicted: the
*capture* layer is device-specific, everything downstream isn't. New
`DeviceEventTypes.PowerReading` constant, same "just a string constant,
no schema change" pattern the unused sensor event types already
demonstrated.

**Protocol choice: native C#, not a Python sidecar.** The Tapo camera's
motion-detection investigation (same session) needed a Python `pytapo`
subprocess because the newer Tapo/KLAP protocol is HTTPS-based, TLS/cloud-token
auth, and only really has a maintained implementation in Python. This
plug uses the older, unrelated Kasa protocol — plain TCP on port 9999,
XOR-obfuscated JSON, no TLS, no auth — simple and stable enough to
implement directly in C# (`KasaProtocolClient`) with no external process
or library. Verified directly against the real device
(`kasa --host ... --json state`) before writing any C#, same
verify-before-building discipline used throughout this session, and the
exact response field names (`sw_ver`, `hw_ver`, `model`, `mac`,
`voltage_mv`, `current_ma`, `power_mw`, `total_wh`) were taken from that
real response, not guessed.

**No Cloud-side queue publish for a successful reading** (unlike
`CameraCaptureHandler`, which publishes to `CameraCapturedQueue` for
Telegram delivery) — a routine power reading needs no Cloud-side
processing today, only dashboard visibility via the existing read API.
`SmartPlugReadingFailedHandler` still publishes to `DeviceEventQueue` on
failure, matching `CameraCaptureFailedHandler`'s existing precedent, even
though nothing currently consumes that queue Cloud-side either.

**Added afterward: a distinct, change-triggered `PowerStateChanged` event**
(`DeviceEventTypes.PowerStateChanged`), separate from the routine
`PowerReading` stream — every scheduled read already carried `IsOn` in its
payload, but nothing distinguished "the switch actually flipped" from "the
switch is still whatever it was." `SmartPlugMonitorWorker` now tracks the
last-known `IsOn` per device for the lifetime of its monitor loop (a plain
local variable threaded through the loop, deliberately not a new field on
the shared `DeviceRuntimeState` — this is a SmartPlug-specific concept,
same reasoning ADR-007 already applies to keeping `ICamera`/`ISmartPlug`
from sharing state that only makes sense for one of them) and publishes
`SmartPlugPowerStateChangedEvent` only on an actual transition — including
the very first reading (`null → On`/`Off`), matching
`DeviceHeartbeatWorker`'s existing `null → Unknown` behavior for the same
kind of "first observation counts as a change" reasoning. `ref` parameters
don't work across `async` method boundaries in C#, so the tracked value is
threaded through as an explicit return value from `ReadAsync` rather than
a `ref bool?` parameter.

**Added afterward again: a real Telegram alert on power-state change,
and the first real consumer of the `device-events` queue.**
`SmartPlugPowerStateChangedHandler` now publishes a `DeviceEventQueueMessage`
(`Vivnest.Core/Queues/Models` — deliberately generic, no type-specific
fields, since ADR-004 already established queue messages only ever carry
`{PartitionKey, RowKey}` and the Cloud side refetches the entity) to
`MessagingOptions.DeviceEventQueue` ("device-events") after persisting.
That queue already existed and already had one publisher
(`CameraCaptureFailedHandler`, for capture failures) but no Cloud-side
consumer at all — nothing processed messages landing on it. Built the
first one: `DeviceEventQueueFunction` (queue-triggered) →
`IDeviceEventQueueHandler`/`DeviceEventQueueHandler` (`Vivnest.Cloud`),
which refetches the `DeviceEventEntity` and switches on its `EventType`.
Only `PowerStateChanged` is actually handled — it parses the `{IsOn}`
payload and dispatches a Telegram notification via the existing
`INotificationDispatcher` (`NotificationTypes.SmartPlugPowerStateChanged`,
new constant). Any other event type landing on this queue (including the
pre-existing, previously-inert `CameraCaptureFailed` messages) hits a
default case that logs and no-ops — deliberately not building a
notification for that too just because the router now exists; same
"second real consumer" rule of thumb as everywhere else. Verified live
end to end against the real device and the real Telegram bot/chat
(temporarily enabling `Telegram__Enabled` locally for the test, then
reverting it) — confirmed HTTP 200 from Telegram's API and the message
actually arriving.

## ADR-016 — Home Assistant integration built for real (Sprint 6 Phase 1+2); a device reachable multiple ways keeps one `DeviceId`

**What was built**, verified against a real HS110 smart plug and a real HA
instance (`ghcr.io/home-assistant/home-assistant:stable` in Docker), not
just compiled: `HomeAssistantWorker` (`Vivnest.Agent/Runtime/Workers`) — a
`BackgroundService` that opens a persistent `ClientWebSocket` to HA's
`/api/websocket`, authenticates with a long-lived access token, subscribes
to `state_changed`, and dispatches a `HomeAssistantStateChangedEvent` for
every entity in an explicit `HomeAssistant:Entities` allowlist
(`HomeAssistantOptions`/`HomeAssistantEntityOptions`, `Vivnest.Core/Options`)
— matching the existing `Devices[]` array's explicit-config style, not
automatic discovery of everything HA knows about.
`HomeAssistantStateChangedHandler` persists a `DeviceEvent` and publishes to
the existing `DeviceEventQueue`, the same generic `{PartitionKey, RowKey}`
shape ADR-004 already established — no Cloud-side code needed, and because
`DeviceEventQueueFunction` already handles `PowerStateChanged` by sending a
Telegram notification (added for the direct-Kasa path, ADR-015 above), an
HA-sourced toggle produces the same Telegram alert for free. Outbound:
`IHomeAssistantCommandSender`/`HomeAssistantCommandSender`
(`Vivnest.Agent/Services`), a typed `HttpClient` calling HA's REST
`/api/services/<domain>/<service>` — verified with a temporary manual call
(`CallServiceAsync("switch", "turn_off", ...)`, removed after confirming the
physical relay actually flipped).

This is a different outcome from the pytapo-direct/ONVIF attempts documented
in roadmap.md Phase 4 Sprints 6-7: those were built, tested against the real
Tapo C120, blocked by a TP-Link firmware bug, and reverted. This build
targets a **different device** (the HS110 smart plug, via HA's
`python-kasa`-based `tplink` integration, unaffected by the Tapo firmware
bug) and stays in the tree. Sprint 6's original motion-detection goal is
still open — no `MotionDetectedEvent`/`MotionCaptureHandler` exists yet, and
none of this was tested against a motion sensor, since none is on hand —
but the generic HA bridge (inbound state + outbound control) it depends on
is now real, not just designed.

**A device reachable more than one way keeps a single `DeviceId` —
connection method is a data source, not a separate device.** The HS110 is
reachable both directly (`SmartPlugMonitorWorker` polling the Kasa
protocol, `DeviceId: plug-001`) and via HA (`HomeAssistantWorker`,
subscribed to `switch.tplinksmartplug`). Both were briefly given different
`DeviceId`s (`plug-001` / `plug-001-ha`) to avoid an assumed collision —
checked instead of assumed, and there isn't one: `DeviceEvent` is an
append-only log with no single-writer assumption (ADR-004's generic queue
message already implies this), and `ICaptureStatusStore`/`DeviceRuntimeState`
— the one store that *could* collide — is only ever touched by
`SmartPlugMonitorWorker`'s own loop, never by
`HomeAssistantStateChangedHandler`. Unified back to the same `DeviceId`
(`plug-001`) once confirmed safe: both sources now feed the same device's
event timeline, and "current state" is naturally "whichever event is most
recent," regardless of which path reported it. For a device reachable
*only* via HA (no native protocol implemented in Vivnest, e.g. a future
Zigbee sensor), nothing changes — its `DeviceId` exists solely in
`HomeAssistant:Entities`, no `Devices[]` entry, exactly as Sprint 6
originally anticipated (roadmap.md: "an HA-sourced device gets its own
`DeviceHeartbeatEntity` row").

Deliberately **not** merged into one config schema (`Devices[]` and
`HomeAssistant:Entities` stay two separate arrays that happen to agree on
`DeviceId` when they describe the same device) — that would mean turning
`DeviceSettings` into a tagged union to express "how to reach this device,"
a real design cost for something exactly one device (the HS110) needs
today. Same "second real consumer" rule of thumb as ADR-007/015: revisit if
a second device ever needs dual-path config.

**Long-lived WebSocket connections need an explicit keepalive, or they can
go silently stale with no exception raised.** First implementation
connected, authenticated, and subscribed successfully, but after ~3 minutes
idle, a real toggle event pushed by HA never arrived — no error, no close
frame, `ReceiveAsync` just never returned. Isolated with a minimal Python
probe (`websocket-client`) hitting HA directly: HA pushed the event
instantly to a fresh connection, proving the bug was client-side, not
HA-side. Root cause understood as idle long-lived TCP connections silently
going stale through Docker Desktop's WSL2 port-forwarding layer (the
initial handshake round-trips are fast enough to always work; a connection
sitting untouched for minutes is what exposes it). Fixed with what HA's
WebSocket API is explicitly designed for: a periodic `{"type":"ping"}` sent
every 20s (`SendPeriodicPingsAsync`) to keep the path warm, plus a 45s
receive timeout that forces a reconnect if the connection is ever genuinely
stuck — verified by reproducing the original failure, applying the fix, and
confirming a toggle after the connection had been idle past the old failure
window came through cleanly.

**Added afterward: explicit per-path toggles, and native polling turned off
for the plug — HA is now its only active source.** Having both
`SmartPlugMonitorWorker` (native) and `HomeAssistantWorker` (HA) actively
covering `plug-001` at the same time was never a deliberate design (unlike
the shared-`DeviceId` decision above, which *was* deliberate) — it was
just how it ended up after both were built and verified independently.
Once noticed, the policy adopted: **a device is covered by exactly one
active path at a time — HA if HA already covers it well, native only when
HA doesn't (the Tapo camera, per its firmware bug) — never both.** Rather
than delete either implementation to enforce this (both are real, working,
and the native Kasa client is the better source for some data — see
below), added a symmetric `Enabled` flag to both sides instead:
`DeviceOptions.Enabled` already existed and already worked; added the
matching `HomeAssistantEntityOptions.Enabled` (`HomeAssistantWorker`
checks it alongside the existing entity-allowlist match). `plug-001` is
now `Enabled: false` in `Devices[]` and `Enabled: true` in
`HomeAssistant:Entities` — native code stays in the tree, untouched, ready
to flip back with a config change alone, no redeploy of logic. This is
explicitly framed as a *precedent for future "custom integrations"*: a
native Vivnest capability and an HA-sourced path for the same device are
expected to coexist in the codebase long-term, with config choosing which
is *active*, not which *exists*.

**Follow-up bug found in production, root-caused and fixed: HA-sourced
devices had no liveness mechanism of their own, so their status silently
rode on `AgentHeartbeat` staleness alone.** After the toggle above went
live, a user report ("the plug is never attached to a socket either, how
is the agent determining it's functional") led to querying
`tblDeviceHeartbeat` directly: `plug-001`'s `LastHeartbeatUtc` was frozen
15+ hours in the past — from before the native→HA toggle — while its
`Status` field still read `Online`, and kept generating correct-looking
recovery notifications anyway. Root cause: `DeviceHeartbeatWorker` (the
only writer of `DeviceHeartbeatEntity.Status`) iterates `Devices[]`
(`IDeviceRuntimeStore.GetDevices()`), which HA-sourced devices are either
absent from or disabled in — nothing was writing device-level heartbeats
for them at all. Cloud's `HealthMonitorService.DetermineFinalStatus`
(`Vivnest.Cloud/Services/HealthMonitorService.cs`) was and is correct: it
trusts `AgentHeartbeat` staleness only as a cascade-to-offline fallback
(ADR-005) and otherwise trusts whatever `device.Status` last says — the
bug was that nothing ever updated that field for HA-backed devices after
the toggle, so it froze at whatever it happened to be.

Fixed by giving HA-sourced devices a real liveness signal, reusing the
existing `DeviceHeartbeatGeneratedEvent`/`DeviceHeartbeatHandler` pipeline
rather than building a parallel one: a new
`IHomeAssistantLivenessTracker`/`HomeAssistantLivenessTracker`
(`Vivnest.Agent/Services`) treats every `state_changed` for a mapped,
enabled entity as evidence of reachability, and HA's own generic
`state == "unavailable"` (how HA represents "can't currently reach this
entity," regardless of domain) as the offline signal — no
device-type-specific sensor needed. `HomeAssistantStateChangedHandler`
calls it before persisting the `DeviceEvent`, isolated in its own
try/catch so a heartbeat-publish failure can't fault the WebSocket read
loop over a secondary concern. Because push-based liveness only updates on
an actual HA event, a status could otherwise still freeze across an agent
restart or a dropped/reconnected WebSocket with no entity change in
between — closed by having `HomeAssistantWorker.SyncLivenessAsync` call
the same tracker with a one-off REST state read
(`IHomeAssistantCommandSender.GetStateAsync`, new) for every mapped entity
on every successful (re)connect. The sync deliberately calls the tracker
directly rather than going through the full
`HomeAssistantStateChangedEvent` pipeline — going through the full
pipeline would re-persist a `DeviceEvent` and re-fire a Telegram
notification on every reconnect even when nothing actually changed.

Extracting `IHomeAssistantLivenessTracker` out of
`HomeAssistantStateChangedHandler` (rather than inlining the same logic
twice) followed the same "second real consumer" rule used elsewhere in
this ADR: the sync path is a second real caller needing the identical
liveness logic without the DeviceEvent/notification side effects, not a
speculative abstraction.

Remaining gap at the time, later closed below: this fix closes the "stale
status frozen forever" bug, but there was still no signal for "the
agent's WebSocket connection to HA itself is down but HA is otherwise
fine" — `plug-001`'s apparent status would stay whatever it last was
during an extended reconnect loop, the same class of problem
`AgentHeartbeat` staleness solves for native devices, just not yet built
for the HA connection itself. Closed by the HA-connection-cascade entry
further down.

**Follow-up bug: every container restart falsely notified "Device
plug-001 is back online," regardless of whether anything actually
changed.** `HomeAssistantLivenessTracker.ReportAsync`'s change-detection
compares against `DeviceRuntimeState.LastReportedStatus`, which is
in-memory only and always `null` right after a restart. On startup,
`HomeAssistantWorker.SyncLivenessAsync` immediately polls HA and reports
the plug's current state (almost always "on" -> Online) - `null != Online`
reads as a transition, so a heartbeat publishes as if the device just
recovered, on every single restart. If `NotificationState` happened to be
`OfflineNotified` from any earlier point, Cloud reads that heartbeat as a
genuine recovery and sends the Telegram message. Native devices are
accidentally immune to this: `OfflineDetection.Evaluate` starts a fresh
runtime at `Unknown` (no `LastActivityUtc` yet), which neither
notification rule reacts to - `HomeAssistantLivenessTracker` skipped that
because it computes a real Online/Offline status on the very first call.
Fixed by not treating "first observation this process, and it's healthy"
as a transition - but a first observation of Offline still reports
normally, since a device that's already down when the agent starts is
genuinely worth knowing about, restart or not.

**Follow-up: the "HA connection down but agent fine" gap flagged above,
closed - a real bug this time, not a hypothetical.** Root-caused live,
not from first principles: a user physically unplugged `plug-001` and it
kept showing Online. Direct Table Storage queries showed `tblDeviceEvents`
had *zero rows ever* for `plug-001` and `DeviceHeartbeatEntity.LastHeartbeatUtc`
was frozen from before any of this session's HA testing - the WebSocket
had never once delivered an event. The agent's own logs then showed why:
`HomeAssistantWorker` was retrying `Connection refused (localhost:8123)`
in a loop - `appsettings.json`'s `HomeAssistant:BaseUrl` was still
`http://localhost:8123/` (correct for local `dotnet run`), but inside a
Docker container `localhost` means the container itself, not the host
running HA - the exact `host.docker.internal` gotcha from ADR-014,
reproduced because the `docker run` command on this particular host
hadn't included the override this time.

That misconfiguration was fixable per-host, but it exposed the deeper gap
this ADR already knew about: Cloud had *no way to know* the HA connection
was down at all. Fixed with three pieces:

1. `IHomeAssistantConnectionTracker`/`HomeAssistantConnectionTracker`
   (`Vivnest.Agent/Services`) - a small in-memory bridge.
   `HomeAssistantWorker` marks it on every successful subscribe *and* on
   every received frame (not just real events - a pong proves the
   connection is alive too), `AgentHeartbeatWorker` reads it into a new
   `AgentHeartbeat.HomeAssistantLastConnectedUtc` on every tick. The two
   workers don't otherwise share state; this is the only bridge between them.
2. `DeviceHeartbeatSource` (`Native`/`HomeAssistant`, new enum) on
   `DeviceHeartbeat`/`DeviceHeartbeatEntity` - Cloud previously had no way
   to know which devices depend on HA at all. Set at the two write sites:
   `DeviceHeartbeatWorker` writes `Native`, `HomeAssistantLivenessTracker`
   writes `HomeAssistant`. Missing/unparseable `Source` (existing rows
   predating this field) defaults to `Native` - conservative, since
   wrongly subjecting an actually-native device to the HA cascade would be
   the worse mistake of the two.
3. `DeviceStatusResolver.Determine` gained a second cascade, structurally
   identical to the agent one but one level down: for a `HomeAssistant`-sourced
   device, if `agent.HomeAssistantLastConnectedUtc` is null (never
   connected) or older than the new `HealthMonitorOptions.HomeAssistantConnectionStaleAfter`
   (flat `TimeSpan`, default 3 minutes - not a multiplier, since there's no
   natural "HA heartbeat interval" to multiply the way agent/device
   heartbeats have one), the device reports `Unknown` with a new
   `HomeAssistantCascade` flag, mirroring `AgentCascade` exactly:
   `HealthMonitorService` skips notification for it the same way, for the
   same reason (this device's badness is explained by something else
   already-diagnosable, not by the device itself).

`DeviceStatusResult` grew a fourth field (`HomeAssistantCascade`) to carry
this - both call sites (`HealthMonitorService`, `DeviceQueryService`)
already went through the shared resolver from the "Fourth follow-up"
entry above, so no duplicated logic to keep in sync this time.

**Backlog, deliberately not built yet: reconstruct the full `PowerReading`
(power/voltage/current/total consumption/brand/model/firmware) from HA,
not just the on/off `PowerStateChanged` toggle.** Checked directly against
the running HA instance's `/api/states`: HA's `tplink` integration already
exposes `sensor.tplinksmartplug_current_consumption` (W),
`_voltage` (V), `_current` (A), and `_total_consumption` (kWh) as separate
entities — plus data the native client doesn't have at all (daily/monthly
consumption, LED state, cloud-connection status). Brand/model/firmware
aren't in any entity's state, though — HA keeps that in its Device
Registry, a different API (`/api/config/device_registry/...`) nothing
here calls yet. The real blocker isn't data availability, it's that HA
pushes a `state_changed` event on every fluctuation (commonly every 5-10s
for Kasa power sensors), while `PowerReading` is deliberately a throttled,
scheduled snapshot (`SnapshotInterval`) — today's 1-entity-→-1-event
`HomeAssistantWorker` design has no mechanism to combine several entities
into one periodic reading the way `SmartPlugMonitorService` does natively.
Needs: multi-entity aggregation with its own throttle, plus a Device
Registry lookup for the static metadata. Deferred, not because it isn't
useful, but because native already provides all of this cleanly today and
nothing currently needs the HA-sourced version yet.

## ADR-017 — Capture gallery loads day-by-day, hour-of-the-day's-worth at a time, not the whole 30-day window up front

**Problem, reported directly, not anticipated:** with enough captures
accumulated, the dashboard's gallery got slow to load. Traced to
`DeviceQueryService.ToDto(entity, includeImageUrl: true)`: every capture
in the requested window gets a JSON payload parse *and* a synchronous SAS
URI generation (`AzureBlobStorageClient.GenerateReadSasUri`) — and the
old `?days=30` route did this for **every capture in 30 days** on a
single gallery load, regardless of whether the user ever scrolled past
"Today." For a device snapshotting every 30 minutes, that's ~1,440 SAS
URI generations per page load, almost all wasted.

*Decision:* split into two request shapes instead of one, matching how
the gallery actually gets used — most of a 30-day window is never looked
at, and even the days that are get looked at newest-first:

1. **Day summaries** (`GET .../captures/summary?days=N`) — `{date, count}`
   per day, computed by grouping the same entities `GetByDeviceAndDateRangeAsync`
   already returns, but deliberately skipping `ToDto` entirely — no JSON
   parse, no SAS URL, just a count. Cheap enough to fetch the whole
   30-day window up front so every day's collapsed header can show a
   count immediately.
2. **Paginated single-day captures** (`GET .../captures?date=X&skip=N&take=N`)
   — bounds the expensive read to one day (not 30), and generates SAS
   URLs only for the requested page (10 at a time, newest first), not the
   rest of that day's captures. Called once when a day is actually
   expanded, and again for each "Load more" click within it.

`IDeviceQueryService.GetDeviceCapturesByDateRangeAsync` (the old
whole-window method) was removed, not deprecated — `CaptureGallery.tsx`
was its only caller, and it's fully replaced by the two methods above.
`GetByDeviceAndDateRangeAsync` itself (the underlying reader method)
stays, reused by both new methods, one now bounded to a day instead of a
window.

**Frontend (`CaptureGallery.tsx`, full rewrite):** fetches day summaries
on mount, renders every day collapsed except the one matching today's UTC
date (auto-expanded, matching the old default of showing the latest
capture immediately). A single `useEffect` watching the day-state array
handles loading — expanded-and-not-yet-loaded is the only condition that
triggers a fetch, so the initial Today auto-load and a user manually
expanding a day go through the identical path, not two. A ref-tracked
`apiKey:deviceId` key guards against a slow in-flight day-load from a
previous device landing after the user's already switched devices —
setting state for a day that no longer belongs to the current device
would otherwise be a real (if narrow) cross-device data mix-up risk once
requests can outlive a device switch.

**Deliberately UTC-consistent, not local-timezone-aware.** Day summaries
group by `DateOnly.FromDateTime(entity.OccurredAtUtc)` — UTC date, since
Cloud has no concept of the browser's timezone and nothing today passes
it one. The frontend's `dateHeading`/"Today"/"Yesterday" comparison was
changed to match (UTC date, not `Date.toDateString()`'s local-timezone
read the old flat-list version used) — needed so a day's summary count
and that day's fetched captures always agree on which bucket a capture
landed in. Consequence: for a user far from UTC, the "Today" boundary
shifts at UTC midnight, not local midnight — a deliberate simplification
given the whole system already stores everything in UTC, not a bug.

## ADR-018 — Dashboard visual redesign: hand-rolled tokens, not a UI framework; status-accented rows, not tables; Agent Detail is a new real page

**Prompted directly**: "is it time to make the dashboard professional
looking? it should be responsive." Explored as mockups first (the
`visualize` tool, not real code) before touching the actual app, working
through several rounds: three layout directions compared (minimal cards,
status-accented rows, dense console table), Device Detail iterated with a
Live Feed hero panel and its interaction with clicking a capture
thumbnail, and a new Agent Detail page - all approved before
implementation started.

**Kept the "no UI framework dependency" decision, treated it as a
constraint to design within, not a reason to look raw.** The gap wasn't
missing components, it was missing an actual design system: no color/
spacing tokens, tables that didn't reflow at any width. Fixed with a
real `:root` custom-property token set in `App.css` (surfaces, text,
border, and status-role colors for online/warning/offline-error/unknown,
all referenced by name everywhere instead of hardcoded hex) and a
`.entity-list`/`.entity-row` pattern - status-accented row cards (left
border colored by status, icon, title, status badge, right-aligned
metadata) - replacing the raw `<table>` markup in both `DeviceList` and
`AgentList`. Chose this over adopting Tailwind or a component library
(both discussed as real alternatives) because the "no framework"
decision was explicit and the actual problem was solvable without
reversing it.

**Agent Detail is new, not a redesign of something that existed** -
`AgentList` previously had no drill-down at all. Mirrors `DeviceDetail`'s
shape: status-accented header, a metric grid, then a list of that
agent's devices (filtered client-side from the already-fetched device
list, not a new endpoint - see the DTO changes below) linking back into
`DeviceDetail`. Reached this design by first asking whether a Site-level
page was also needed (multiple agents can exist per site, per tenant) -
decided *not yet*: today there's effectively one agent, a Site Detail
page would just show what the Agents list already shows unfiltered, and
the "second real consumer" rule this codebase already applies elsewhere
applies here too. If per-site grouping becomes genuinely useful later,
grouping the Agents list by site (same collapsible pattern as the
capture gallery's day-grouping) is the cheap next step, not a new page.

**`DeviceSummaryDto`/`AgentSummaryDto` gained `AgentId`/`TenantId`/
`SiteId`** - all three already lived on the underlying
`DeviceHeartbeatEntity`/`AgentHeartbeatEntity` (via `AgentEntity`/
`BaseEntity`) but had never been exposed through the API, since nothing
before this needed them. Device's `AgentId` now backs a real, clickable
link to `AgentDetail` (hidden for `DevicesOnly` keys, which get 403 from
`/agents*` - the link isn't just hidden client-side, the destination
would genuinely fail). Tenant/Site are shown on both detail pages mostly
for confirmation/debugging value, since a single dashboard session is
always scoped to one tenant+site already (`TenantContext`) - not a
choice the user is ever making between values, just a fact worth being
able to see.

**Live Feed is a placeholder panel, not a built feature** - explicitly
scoped as layout-only during design. True live video needs a real
streaming subsystem that doesn't exist (the camera speaks RTSP on the
home LAN; browsers can't play RTSP directly; the Dashboard is a
cloud-hosted static site with no path to the camera without a relay).
Two real architectures were discussed for when this gets built: an
Agent-side relay (ffmpeg already ships in the Agent's Docker image;
cheap; LAN-only unless separately tunneled) versus a Cloud-side relay
(reachable from anywhere; requires a persistent container, which is a
real reversal of ADR-014's "Function App and Static Web App, not
containers" decision). Parked, not decided - the Live Feed panel's
placeholder markup already reserves the right layout slot for whichever
gets built.

**Resolved a real layout conflict, not just a color choice: what happens
when you click a capture thumbnail, given Live Feed now owns the one
big-image spot on the page.** Two options considered and rejected before
landing on the third: a lightbox overlay (rejected - "no lightbox",
explicit), and a second separate preview panel below Live Feed (rejected
- doubles vertical space, redundant). Landed on swapping the Live Feed
panel's own content: clicking a thumbnail replaces the placeholder/stream
with that capture and its timestamp, with a "Back to live" control
beneath it to return; the selected thumbnail keeps the same accent
border used elsewhere for "this is the active one." One hero panel,
two states, driven by lifting `selectedCapture` out of `CaptureGallery`
(which used to own its own top preview image directly) up into
`DeviceDetail`, which now decides what the hero shows.

**`DeviceEventList` (renamed from an initial `RecentEvents` pass, to
match the existing `DeviceList`/`AgentList` naming convention) is why
cameras stopped calling `GET .../events` entirely** - a separate,
smaller fix bundled into the same pass. Every event a camera produces is
`CameraCaptured`, already shown richer (with the actual image) in the
gallery, so the events list was pure redundant noise for cameras
specifically - not for other device types, where it's still the only
place events are visible. Extracted into its own component and gated
behind `deviceType !== "Camera"` in `DeviceDetail`, rather than fetching
the data and just hiding the rendered list, so cameras don't pay for a
request whose result would never be shown.

## ADR-019 — Motion detection built natively against Tapo H100/T100 hardware, mirroring the SmartPlug pattern file-for-file, not through Home Assistant

**Prompted directly**: the user bought a Tapo H100 hub and T100 motion
sensor specifically to unblock Sprint 6's motion-detection goal, which
ADR-016 had left parked ("none is on hand"). Two integration paths
existed - bridge through the already-working Home Assistant connection
(ADR-016), or reimplement the H100/T100's own protocol natively in C#,
the same choice already made for the plug (ADR-015) over the Tapo
camera's broken cloud protocol. Asked directly; the user chose **native
C# first**, explicitly declining the HA-bridge alternative initially
offered.

**Protocol discovery, verified empirically before writing any production
code.** The H100 is a "Tapo"-branded hub, not a "Kasa"-branded plug, so
`KasaSmartPlug`'s existing protocol (unauthenticated TCP, port 9999)
doesn't apply. Cross-referenced `python-kasa`'s own source
(`klaptransport.py`, `smartprotocol.py`) to confirm the hub speaks
`SMART.KLAP` (v2 handshake): `auth_hash = sha256(sha1(user)+sha1(pass))`;
a two-step `/app/handshake1`/`/app/handshake2` exchange establishes a
session key/iv/sequence-number/signature (all SHA-256-derived); every
subsequent request is AES-128-CBC-encrypted and HMAC-style-signed,
posted to `/app/request?seq=N`. Validated in two stages before
committing to the design: first the `kasa` CLI itself
(`--type smart`) against the real hub, then a from-scratch standalone C#
console spike that round-tripped the full handshake and read live
`detected: false` state from both the hub and the T100 child directly.
Both succeeded on the first attempt - unlike every previous attempt this
session to talk to the Tapo camera (ONVIF, python-kasa, pytapo, even real
HA), which had failed consistently and turned out to be a TP-Link
firmware bug specific to that device, not a protocol implementation
problem. The H100/T100 use a different protocol entirely and were
unaffected.

**A hub child device has no network presence of its own** - the T100
is addressed through the H100's `control_child` wrapper
(`{"method":"control_child","params":{"device_id":<child>,"requestData":{"method":"get_device_info"}}}`),
not a separate host/port. `DeviceSettings` gained one new field,
`ChildDeviceId`, reusing the existing `Host`/`Username`/`Password` for
the hub's own connection - the same shape `Settings` already had for
every other device type, extended rather than restructured.

**Built by deliberately mirroring `SmartPlug` end to end, not by
inventing a new shape:** `TapoKlapClient` (`Vivnest.Infrastructure/Tapo`)
promotes the spike into a reusable `IDisposable` client with a lazy
handshake and both `SendAsync` (hub-level) and `SendChildRequestAsync`
(the `control_child` wrapper); `IMotionSensor`/`MotionSensorState`/
`IMotionSensorFactory` (`Vivnest.Core/MotionSensor`) mirror
`ISmartPlug`/`SmartPlugState`/`ISmartPlugFactory` field-for-field;
`TapoMotionSensor` (`Vivnest.Infrastructure/MotionSensor`) mirrors
`KasaSmartPlug` - a raw TCP connect to the hub's port 80 for
`IsReachableAsync`, a full protocol round trip for `GetStateAsync`;
`MotionSensorMonitorService`/`MotionSensorMonitorWorker`
(`Vivnest.Agent`) mirror `SmartPlugMonitorService`/`SmartPlugMonitorWorker`,
down to the same event-driven-not-every-tick design: `DeviceEventTypes.MotionDetected`
(pre-existing constant, previously unused) fires only when `Detected`
actually flips, exactly like `SmartPlugPowerStateChangedEvent` fires only
on an actual on/off transition, rather than persisting a `DeviceEvent` on
every poll. One deliberate deviation from the plug's shape:
`MotionSensorMonitorWorker` has no separate liveness-probe/full-read
split - a motion read is already as cheap as a probe (one
`control_child` round trip), so `SmartPlugMonitorWorker`'s probe/full-read
distinction (ADR-010) wasn't worth reproducing here.

**Cloud-side notification mirrors `PowerStateChanged` exactly**:
`DeviceEventQueueHandler` gained a `MotionDetected` case parsing
`{Detected}` from the event's JSON payload and dispatching a
`NotificationTypes.MotionDetected` Telegram notification at
`NotificationPriority.Normal` - same priority as `PowerStateChanged`,
not `Urgent` (reserved for device/agent offline alerts elsewhere in
`HealthMonitorService`), since a motion event isn't itself a health
signal.

**Smoke-tested against the real production code path, not just the
spike, before calling this done** - a throwaway console harness
(scratchpad, not committed) referenced the real `Vivnest.Infrastructure`/
`Vivnest.Core` projects and called the actual `MotionSensorFactory` →
`TapoMotionSensor` → `TapoKlapClient` chain against the real H100
(`192.168.50.170`) and T100 child, deliberately *not* by running the full
`Vivnest.Agent` host - the Agent's `appsettings.json` carries live
production connection strings and would have started every other worker
(camera, heartbeats, the real Home Assistant connection) against
production Storage/Queues/Telegram, which a docs-and-glue-code smoke test
had no reason to touch. Result: hub reachable, child read succeeded,
live `Detected: true` state returned along with real model/firmware/MAC
metadata - confirming the production wiring works end to end, not just
the isolated spike.

**Deferred: `ChildDeviceId` is hand-copied into config today, not
discovered.** Getting `802E099D6468F67C86117F63882C429E24A16843` into
`appsettings.json` meant manually calling `get_child_device_list` against
the hub once (via the `kasa` CLI, then confirmed again in the C# spike)
and pasting the value in - fine for one hub with one child, consistent
with this codebase's existing "explicit config, no auto-discovery" style
(`Devices[]`, `HomeAssistant:Entities` work the same way). Raised
directly: is a device-discovery feature worth building now - an
Agent-side lookup (`get_child_device_list` wrapped in a CLI flag or
endpoint), possibly surfaced through the Dashboard for onboarding new
hubs without touching config by hand. Decided **not yet**, same
"second real consumer" reasoning as everywhere else in this doc - one
hub doesn't justify it, and a Dashboard-driven version specifically
would need a Cloud→Agent command channel that doesn't exist yet (queues
today only flow Agent→Cloud - see CLAUDE.md). The cheap version if this
becomes a real need: a small reusable CLI tool/flag in the Agent that
takes a hub host+credentials and prints its children's `device_id`s,
no new architecture required. The Dashboard/Cloud-driven version is the
one that actually needs the command channel, so it's a natural forcing
function for building that channel for real, rather than speculatively -
worth reconsidering together if/when broader Hub integration (multiple
hubs, self-service onboarding) gets designed.

The lookup itself is trivial once authenticated - reuses the production
`TapoKlapClient` unchanged, just calls its hub-level `get_child_device_list`
instead of a child-wrapped command. Credentials come from args, not
hardcoded, so this is safe to keep around:

```csharp
// Standalone discovery script - reuses the production TapoKlapClient as-is.
// Usage: dotnet run -- <hub-host> <tapo-username> <tapo-password>
using System.Text.Json;
using Vivnest.Infrastructure.Tapo;

var host = args[0];
var username = args[1];
var password = args[2];

using var client = new TapoKlapClient(host, username, password, TimeSpan.FromSeconds(5));

var result = await client.SendAsync("get_child_device_list", null, CancellationToken.None);

foreach (var child in result.GetProperty("child_device_list").EnumerateArray())
{
    Console.WriteLine(
        $"device_id={child.GetProperty("device_id").GetString()} " +
        $"model={child.GetProperty("model").GetString()} " +
        $"detected={(child.TryGetProperty("detected", out var d) ? d.GetBoolean().ToString() : "n/a")}");
}
```

This is exactly the `get_child_device_list` call `TapoMotionSensor.GetStateAsync`
already builds on for the child-wrapped `get_device_info` read - the only
difference is calling `SendAsync` directly (hub-level) instead of
`SendChildRequestAsync` (child-wrapped), since discovery happens *before*
you have a `ChildDeviceId` to wrap with.
