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

*Eighth follow-up: the post-recovery `Unknown` gap (fourth follow-up
above) could get permanently stuck, live in production - reported
directly ("the status are shown as Unknown, looks like a bug"), and
initially suspected to be a `DevicesOnly` API key issue since that's
what the user happened to be testing with.* Traced every code path
`DeviceQueryService`/`DeviceStatusResolver` touch and confirmed neither
branches on `TenantContext.DevicesOnly` at all - it only gates the
`/agents*` routes (ADR-012). Queried the live tables directly instead of
guessing: `camera-001` and `motion-001` were both stuck on `Unknown`,
their `DeviceHeartbeatEntity.LastHeartbeatUtc` frozen at ~04:35-04:37
while `AgentHeartbeatEntity.LastRecoveredUtc` was 04:40 - a gap that had
already lasted over two hours with no sign of resolving on its own, for
*any* API key, `DevicesOnly` or not.

Root cause: the fourth follow-up's gap check compares
`device.LastHeartbeatUtc < agent.LastRecoveredUtc`, implicitly assuming
a device's post-recovery heartbeat necessarily arrives *after* Cloud
records the recovery. It doesn't. `DeviceHeartbeatWorker` publishes once
immediately on every process start regardless of status, because
`DeviceRuntimeState.LastReportedStatus` starts `null` and so always
differs from the first computed status (`previousStatus == status` is
the only guard against republishing, and `null` can't equal any real
status) - that first publish lands within seconds of `AgentHeartbeatEntity.StartedUtc`.
`LastRecoveredUtc`, by contrast, is when *Cloud's* health-check cadence
(cron sweep or queue processing) happened to notice the agent was back -
an independently-timed, unbounded-delay event with no causal
relationship to the agent's own local first-tick publish. When Cloud's
detection lagged behind the device's own (earlier, perfectly valid)
first-tick heartbeat - which is the common case, not an edge case - the
gap check compared against the wrong clock and got stuck permanently:
once a device's `Status` stops changing, `DeviceHeartbeatWorker` never
republishes again, so nothing was ever going to move `LastHeartbeatUtc`
past that stale `LastRecoveredUtc`.

Fixed by anchoring the gap check on `agent.StartedUtc` instead of
`agent.LastRecoveredUtc` (`DeviceStatusResolver.cs`). `StartedUtc` is
captured once by `AgentHeartbeatWorker` at process construction and is
causally guaranteed to precede any heartbeat that process can ever
publish - the same first-tick publish that broke the old check now
*always* satisfies the new one, self-healing within one
`DeviceHeartbeatWorker` iteration of every restart rather than getting
stuck indefinitely. Also simpler: no `LastRecoveredUtc is { }` null-guard
needed, since `StartedUtc` is always populated.

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
would genuinely fail). Tenant/Site were originally repeated on both
detail pages (and per-row in `AgentList`) for confirmation/debugging
value.

*Follow-up: moved from per-page/per-row to the app header, shown once.*
Raised directly: since an API key is always scoped to exactly one
tenant+site (`TenantContext`, ADR-012 - never a choice the user is
making between values within a session), repeating the same two strings
on every device, every agent, and every row was redundant, not
confirmatory. `GET /whoami` already returns `TenantId`/`SiteId`
alongside `DevicesOnly` (fetched once at login) - no new endpoint
needed, just reading fields already there. `App.tsx` now holds that as
`site` state and renders it once next to the "Vivnest" title
(`.app-header-site`); the per-detail-page metric cells and `AgentList`'s
per-row `tenant / site` subtitle were removed. `DeviceSummaryDto`/
`AgentSummaryDto` still carry the fields (harmless, still available if
a future view needs them per-entity) - only the redundant display sites
were cut.

*Follow-up: `AgentDetail`'s metric grid backfilled with `Agent ID`,
`Hostname`, and `Firmware` once Tenant/Site freed up the space* -
prompted by a direct question about why the agent row/detail title
showed a value like `1803f8eb4028`: that's `Environment.MachineName`
(`AgentHeartbeatWorker.cs`), which inside a container resolves to
Docker's own auto-generated short container ID unless `--hostname` is
set at `docker run` time, not a stable identifier. `AgentId`/`HostName`
were already in `AgentSummaryDto` (just never rendered as labeled
properties, only used as the row/detail title and for routing) - purely
a frontend addition. `FirmwareVersion` needed real plumbing: `AgentOptions.FirmwareVersion`
(bound from `appsettings.json`'s `Agent:FirmwareVersion`) existed in
config but was never sent anywhere - added to `AgentHeartbeat`/
`AgentHeartbeatEntity`/`AgentHeartbeatMapping.ToModel`/`AgentSummaryDto`,
the same five-file chain every other agent-heartbeat field already
follows. Deliberately did not change what the row/detail title itself
shows (still `HostName`) - the ask was to surface the identity fields
as visible properties, not to fix the title's churn-on-restart problem,
which stays open.

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

**Follow-up bug found live, same class as ADR-016's restart bug:** the
dashboard's "Recent events" showed runs of several "Motion cleared"
entries in a row with no "Motion detected" between them - not physically
possible from a real flip sequence, since `MotionSensorMonitorWorker`
only ever publishes on an actual change. Root cause:
`lastKnownDetected` (`bool?`) starts `null` at the top of
`RunMonitorLoopAsync`'s loop, re-entered fresh on every worker/container
restart. On the first read after a restart, `detected != lastKnownDetected`
is always true (`false != null`), so the sensor's ordinary resting state
got published as if it were a real transition - one phantom event per
restart, not a repeating tick. Same underlying mistake as
`HomeAssistantLivenessTracker`'s "every restart falsely notified 'back
online'" bug above, just never carried over to this worker when it was
built. Fixed the same way: the first read after a restart only records
the baseline (`lastKnownDetected = detected`) silently, without
publishing - only a genuine flip on a *subsequent* read publishes
`MotionSensorStateChangedEvent`.

## ADR-020 — Agent CPU/Memory get their own `AgentEvent` table and worker, not a field on `AgentHeartbeat`

**First attempt, built then reverted.** The initial ask was "CPU and
Memory of the Agent" as a dashboard property, similar to
`FirmwareVersion` (ADR-018's follow-up). Built the same way: `Process.GetCurrentProcess()`
sampled inside `AgentHeartbeatWorker`'s existing tick, `CpuUsagePercent`/
`MemoryUsedBytes` added straight onto `AgentHeartbeat`/`AgentHeartbeatEntity`/
`AgentSummaryDto`. Flagged as wrong before it shipped, by the user, for
two concrete reasons, both correct:

1. **Failure coupling.** The sampling call sat inside the *same*
   try/catch as the heartbeat build-and-publish. If `Process.GetCurrentProcess()`/
   `TotalProcessorTime` ever threw, the whole tick failed - the actual
   liveness heartbeat wouldn't publish either, so a harmless
   metrics-sampling hiccup could make Cloud think the *agent* was down.
   Not hypothetical: this is exactly the shape of bug ADR-005's seventh
   follow-up found in the notification bookkeeping, just in a new spot.
2. **Semantic mixing.** `AgentHeartbeatEntity` is what `HealthMonitorService`/
   `DeviceStatusResolver` read to drive Online/Offline/notification/cascade
   decisions - a minimal, trustworthy liveness signal by design. Bloating
   it with operational metrics couples an observability concern to a
   notification-critical one, and sets the wrong precedent for whatever
   gets added next.

**Second attempt: mirrors `DeviceEvent` instead, end to end.** Checked
that pattern in detail before building rather than reinventing it -
`DeviceEvent`/`DeviceEventEntity` already solves "many timestamped
readings per entity, queryable by date range, cleaned up on a
retention schedule" for devices. `AgentEvent`/`AgentEventEntity`
(`Vivnest.Core`) is the same shape one level up: `PartitionKey = AgentId`,
`RowKey = "{OccurredAtUtc:yyyyMMddHHmmssfff}-{EventId}"` (append-only,
sorted-by-time-within-partition for free, unlike the heartbeat's
upsert-replace), generic `EventType`/`Payload` JSON - `AgentEventTypes.MetricsReported`
is the first value, not the only one, so a future agent-level event
(restart, config change) doesn't need a schema change either, same
reasoning as `DeviceEventTypes` (ADR-007).

**A genuinely separate `BackgroundService`, not a shared tick.**
`AgentMetricsWorker` (`Vivnest.Agent/Runtime/Workers`) has its own
`PeriodicTimer` (`AgentMetricsOptions.Interval`, default 1 minute) and
its own try/catch around the whole tick - this is the actual fix for
failure-coupling problem #1 above, not just moving the data: a
different hosted service means a crash here is physically incapable of
touching `AgentHeartbeatWorker`'s loop, unlike a nested try/catch in a
shared method would have been. Same CPU%-needs-two-samples logic as the
reverted attempt (`TotalProcessorTime` delta between ticks, `null` on
the process's first tick), just relocated. Publishes an
`AgentMetricsSampledEvent` to `AgentMetricsHandler`, which persists via
`IAgentEventWriter` - no queue publish, unlike `DeviceEvent`'s
notification-triggering types, since a metrics sample needs no
Cloud-side reaction, only storage for the dashboard to read later.

**Cloud side mirrors `DeviceEvent`'s reader/retention pair exactly, not
merged with it.** `IAgentEventReader`/`AzureTableAgentEventReader`
(date-range query, ascending order - the dashboard wants chronological
for a chart, unlike the device event feed's newest-first) and a
separate `AgentEventRetentionService`/`AgentEventRetentionTimerFunction`
(`AgentEventRetentionOptions.RetentionDays`, default 30, same as
`DeviceEventRetentionOptions`, cron staggered 15 minutes after
`DeviceEventRetentionCronSchedule` to avoid both hitting Storage at
once). Considered folding agent-event cleanup into the existing device
retention timer instead of adding a second cron function - kept them
separate, mirroring `DeviceEvent`'s already-proven shape 1:1 rather than
merging two different entities' retention into one function for a
marginal reduction in cron-schedule config.

**`AgentQueryService.GetAgentMetricsAsync` returns a typed
`AgentMetricSampleDto`, not raw `AgentEvent`s.** `AgentEvent` stays
generic at the storage layer, but the one real consumer today (the
chart) wants `{OccurredAtUtc, CpuUsagePercent, MemoryUsedBytes}`
directly, not a `JsonElement` payload to parse client-side - so the
query service does that parsing server-side, scoped to
`EventType == MetricsReported`. A generic `GET /agents/{id}/events`
route (mirroring the device one) wasn't built - nothing needs it yet,
same "second real consumer" reasoning as everywhere else; easy to add
once a second `AgentEventTypes` value exists.

**Dashboard chart is hand-rolled SVG, no charting dependency** -
`AgentMetricsChart.tsx` plots two `<polyline>`s (CPU% on a fixed 0-100
scale, Memory on a dynamic 0-to-max*1.1 scale) against a shared time
axis, using `--text-accent` to match the existing link/accent color
rather than introducing a new chart-specific palette. Consistent with
ADR-018's "no UI framework dependency" - a charting library would have
been the first new runtime dependency the dashboard has ever taken on,
for two line charts. One accepted simplification: null CPU samples
(only ever the first tick of a process's lifetime) are dropped from the
point list rather than rendered as a gap, since they're rare enough
that a straight line across one restart isn't misleading.

*Follow-up: bandwidth added as a third chart series; .NET runtime/OS
description added as snapshot fields, not chart series.* Two different
questions, asked and answered separately before building either.

Bandwidth: raised directly as "should network be a metric," decided
**not yet** for plain connectivity (already fully covered by heartbeat
staleness - if the network's down, heartbeats stop and the agent shows
Offline, a separate metric would say nothing new), but **yes** for
upload volume specifically, once there was a concrete reason (metered/
cellular connection tracking). Deliberately not measured via OS network
interface counters (`/proc/net/dev` is Linux-only, same cross-platform
problem `Process.GetCurrentProcess()` was chosen to avoid for CPU/Memory
above) - instead `INetworkUsageTracker` (`Vivnest.Agent/Services`) is a
plain `Interlocked`-guarded counter that `CameraCaptureService` adds to
after every successful upload (`image.Length`, the exact byte count that
left the agent), and `AgentMetricsWorker` drains it each tick via
`TakeBytesUploaded()` (read-and-reset) - same delta-per-tick shape as
CPU%, for the same reason: `BytesUploaded` is a rate (bytes since the
last sample), not a running total, so it rides the existing
`MetricsReported` payload and `AgentMetricsChart` gains a third
`<polyline>` alongside CPU/Memory, no new plumbing.

Runtime/OS: a snapshot fact like `FirmwareVersion`, not a time-series
metric, so it does *not* go through `AgentEvent`/the chart - it's two
more fields on `AgentHeartbeat`/`AgentHeartbeatEntity`/`AgentSummaryDto`,
following the exact five-file chain `FirmwareVersion` already
established, captured once via `RuntimeInformation.FrameworkDescription`/
`RuntimeInformation.OSDescription` in `AgentHeartbeatWorker` (fixed for
the process's lifetime, same as `_startedUtc`). The actual Docker Engine
version was asked about too and deliberately **not** added - reading it
from inside a container needs the host's Docker socket mounted in
(`/var/run/docker.sock`), which would hand a monitoring agent
effective control over the whole host's Docker daemon (start/stop/
inspect any container, not just itself) for a diagnostics nicety. Not
worth that access surface; `RuntimeInformation.OSDescription` gets most
of the same diagnostic value (which kernel/distro the container's
running on) with no new access requirements.

*Follow-up: `FirmwareVersion` itself stopped being manually-typed
config, once a real production-update question exposed why that was
untrustworthy.* Raised directly: how do you actually update a
containerized agent in production - patch a running container's
binaries in place, drop containers entirely for bare exe+DLLs, or pull
a new image? Rejected the first two directly: patching a running
container's filesystem doesn't survive a restart and leaves no record
of what's actually deployed; dropping containers would reintroduce the
exact host-environment-drift problem the Dockerfile exists to
eliminate (the .NET runtime and `ffmpeg` version staying in lockstep
everywhere, not "whatever happens to be installed on this host"). "Pull
a new image" is correct, and cheap at current scale: a small redeploy
script run *on the host* (`docker pull && docker stop/rm && docker
run`), with an off-the-shelf tool (Watchtower - poll a registry,
auto-pull+restart on a new tag/digest) named as the natural next step
once there's more than one agent/host, not built speculatively now for
one.

That surfaced the real gap: `FirmwareVersion` was a hand-typed string
in `appsettings.json` ("0.1.0"), never actually tied to what got built,
so a redeploy story built on top of it would have no reliable way to
confirm which build a given agent was actually running. Fixed by
baking the real git commit into the image at `docker build` time -
`ARG BUILD_VERSION=unknown` / `ENV Agent__FirmwareVersion=$BUILD_VERSION`
in the Dockerfile's `final` stage, populated via
`docker build --build-arg BUILD_VERSION=$(git rev-parse --short HEAD)`.
Needed **zero C# changes** - `AgentHeartbeatWorker` already reads
`Agent:FirmwareVersion` from config, and the Generic Host's
`AddEnvironmentVariables()` already applies the `Section__Key`
convention to `Agent__FirmwareVersion`, the same mechanism every other
env-var-supplied setting in this image already uses. Verified for
real, not just reasoned through: built the image with a real commit
SHA as the build arg, then `docker run --entrypoint env` confirmed
`Agent__FirmwareVersion=<the actual short SHA>` was present inside the
container. Local `dotnet run` (no Docker build step) still gets a
value too - `appsettings.json`'s `Agent:FirmwareVersion` default
changed from the old meaningless "0.1.0" to `"local-dev"`, an honest
label rather than a fake-looking version number.

## ADR-021 — Motion-triggered capture: a generic `DeviceTriggeredEvent`, not a rules engine

**The question, asked directly before any code:** with the T100 motion
sensor actually working, "when motion detected, camera should burst
capture every 30s for 10 min, then revert" - plus a stated long-term
intent to eventually let devices/schedules trigger other devices more
generally. Asked *how other vendors solve this* before designing.
Consumer platforms (Ring/Wyze/Arlo "linked devices") do a hardcoded
one-hop link; prosumer/open platforms (Home Assistant, Hubitat "Rule
Machine") do a full trigger/condition/action engine with a rule store
and an authoring UI. The gap between those two is large - and
`vivnest-runtime-overview.md` already reserves a slot for the second
one ("Rules Engine," listed as a future capability, separate from the
runtime). Decided **not** to build that now, for one motion sensor and
one camera - same "second real consumer" reasoning as everywhere else
in this log. What got built is deliberately the first tier, structured
so it doesn't foreclose the second.

**Also asked directly: does this need to be a formal "capability"?**
No - `ICapability`/Capability Host don't exist in this codebase yet
(see `vivnest-runtime-overview.md`'s "Heartbeat isn't special anymore"
note), and none of the five existing device integrations are capabilities
either, just `BackgroundService` + `IEventHandler<T>` pairs wired
directly in `Program.cs`. Building this one as a formal capability while
everything else stays hardcoded would be inconsistent and premature.
This is one more handler pair, registered the same way as every other
one - nothing new mechanically.

**The shape: separate "who got triggered" from "what each triggered
thing does," using multicast dispatch that already exists today** -
`EventDispatcher.PublishAsync` already awaits every registered
`IEventHandler<TEvent>` for an event type (`Vivnest.Agent/Runtime/Dispatching/EventDispatcher.cs`),
which *is* pub/sub; nothing new was needed to get "devices subscribe."
Three pieces:

1. **`DeviceOptions.TriggersDeviceIds`** (new, `string[]`, default empty)
   - a motion sensor's own config lists which device IDs it triggers.
   Plain config, not a rule store - `motion-001` triggers `["camera-001"]`
   in `appsettings.json` today.
2. **`MotionTriggerResolverHandler : IEventHandler<MotionSensorStateChangedEvent>`**
   (new) - a *second* handler on an event that already has one
   (`MotionSensorStateChangedHandler`, which still does its own job of
   persisting/notifying, untouched). Only acts on `Detected: true`
   (clearing isn't a trigger), resolves `TriggersDeviceIds`, and
   publishes one `DeviceTriggeredEvent { DeviceId, DeviceType, Reason,
   TriggeredAtUtc }` per target. It doesn't know or care what a
   triggered device does about being triggered.
3. **`DeviceTriggeredEvent`** (new, generic) - deliberately not
   `CaptureRequestedEvent` or anything capture-specific. Any number of
   action-specific handlers can subscribe, each filtering on `DeviceType`
   for the one action it knows how to do (`CaptureOnTriggerHandler`
   today; a future `TurnOnPlugOnTriggerHandler` would be a new handler
   file, zero changes to the resolver or to `CaptureOnTriggerHandler`).
   This is the actual property "subscribe" needs to deliver, not just
   the vocabulary.

**`CaptureOnTriggerHandler` stays narrow on purpose** - fires one
immediate capture (so there's a photo the instant motion fires, not
after waiting up to a full `LivenessInterval`) and sets two fields on
`DeviceRuntimeState` (`BurstUntilUtc`, `BurstInterval`). It does not
loop, and does not touch `tblDeviceEvents`/blob storage directly - the
persistence path is identical to every other capture (scheduled or
triggered), see below.

**`CameraCaptureExecutor` finally gets extracted - the "second real
consumer" roadmap.md predicted for it has now actually arrived.**
`CameraCaptureWorker.CaptureAsync` was the sole caller of the
capture-then-publish logic until now; `CaptureOnTriggerHandler` is the
second, so per ADR-007's rule of thumb this was the right moment to
extract it, not before. `ICameraCaptureExecutor`/`CameraCaptureExecutor`
(`Vivnest.Agent/Services`) is that logic moved verbatim - both callers
now share one implementation instead of risking two silently drifting
copies. `CameraCaptureWorker` itself shrank to orchestration only
(decide *whether* to capture, don't know *how*).

**Burst cadence is a self-expiring state machine on `DeviceRuntimeState`,
not a second timer to manage.** `CameraCaptureWorker`'s existing loop
already decides "is a capture due" by comparing `SnapshotInterval`
against `LastCaptureUtc`, and sleeps for `LivenessInterval` between
ticks. Burst mode swaps both of those for `BurstInterval` while
`DateTime.UtcNow < BurstUntilUtc` - once that passes, the same
comparisons naturally fall back to the normal values with no explicit
"end the burst" code path required.

**Found and fixed a real gap while designing this, not after shipping
it: without a wake signal, "every 30s" could take up to a full
`LivenessInterval` to actually start.** `CaptureOnTriggerHandler` sets
`BurstUntilUtc`/`BurstInterval` on `DeviceRuntimeState`, but
`CameraCaptureWorker`'s loop only *notices* on its next wake - if it
happened to just start a 5-minute `LivenessInterval` sleep, the 30s
cadence wouldn't kick in until that sleep finished, eating half the
10-minute burst window before it even started (the one immediate
capture from the handler still lands right away, but the *repeating*
part would lag). Fixed with `DeviceRuntimeState.WakeSignal`
(`SemaphoreSlim(0,1)`) - the worker's sleep is now `Task.WhenAny(Task.Delay(delay),
runtime.WakeSignal.WaitAsync())`, and `CaptureOnTriggerHandler` releases
it after setting the burst fields, so the loop wakes immediately instead
of whenever its current sleep happens to end. `Release()`'s
`SemaphoreFullException` (a second trigger arriving before the worker
consumed the first signal) is caught and ignored - harmless, the worker
was already about to wake up.

## ADR-022 — Motion sensor battery: a boolean status badge, not a percentage chart; mirrors `PowerReading`, not `AgentMetrics`

**The ask:** show battery life for the T100 motion sensor on the
dashboard, similar to the Agent Detail resource-usage chart, updated
every 2 hours rather than on every poll.

**Checked before designing, not assumed:** `MotionSensorState` already
carried `BatteryLow` (a bool, parsed from `at_low_battery`) but nothing
numeric. Real Tapo hardware was known to sometimes report more than this
codebase parses, so a temporary diagnostic (`Console.WriteLine` of the
raw `get_device_info` response, removed once done) was added to
`TapoMotionSensor` and run against the real H100/T100. The real response
confirmed: only `at_low_battery` (bool) - no `battery_percentage` or
equivalent field anywhere in the payload. This ruled out a CPU%-style
line chart outright; the honest UI for a boolean is a status badge plus
a history of readings, not a graph.

**Mirrors `SmartPlug`'s `PowerReading` pattern, not `AgentMetrics`.**
Battery is a **device**-level periodic reading, not agent-level, so the
closer existing analog (per ADR-015) is `PowerReading`'s
throttled-snapshot-into-`DeviceEvent` shape, not `AgentEvent`/
`AgentMetricsWorker`'s agent-level chain (ADR-020). New
`DeviceEventTypes.BatteryStatus` constant; `DeviceOptions.BatteryReportInterval`
(default 2 hours) throttles persistence the same way `SnapshotInterval`
throttles SmartPlug readings - `runtime.LastBatteryReportUtc` on
`DeviceRuntimeState` tracks it. No extra device traffic: `MotionSensorMonitorWorker`
already reads `BatteryLow`/`SignalLevel` on every `LivenessInterval` tick
(no probe/full-read split exists for this sensor, ADR-019); the throttle
only gates how often that reading gets **persisted**, alongside the
existing flip-only `MotionSensorStateChangedEvent` publish, not
replacing it. `MotionSensorBatteryReportedEvent` →
`MotionSensorBatteryHandler` persists via `IDeviceEventWriter`, no queue
publish - same reasoning as `PowerReading`, a routine reading needs no
Cloud-side reaction.

**Cloud/REST mirrors `GetDeviceCapturesAsync` exactly, filtered by the
new event type instead of `CameraCaptured`**: `IDeviceQueryService.GetDeviceBatteryReadingsAsync`
→ `GET /devices/{deviceId}/battery?take=N`, reusing the generic
`DeviceEventDto` shape (`Data` is the raw `MotionSensorState` JSON,
`ImageUrl` always null). No new Cloud-side table or reader needed - it's
the same `IDeviceEventReader.GetByDeviceAsync` every other device-event
endpoint already uses, just with a different `eventType` filter.

**Dashboard:** a new `BatteryStatus` component (self-contained fetch, same
pattern as `CaptureGallery`/`DeviceEventList` - each section owns its own
data, nothing lifted into `DeviceDetail`), gated on
`device.deviceType === "MotionSensor"` alongside (not instead of) the
existing `DeviceEventList`, since motion-detected events are still
wanted too. Renders a `Status`/`Last checked` cell pair in the existing
`.metric-grid` styling, colored via the existing `--text-success`/
`--text-danger` tokens (same pair `.status-online`/`.status-offline`
already use), plus a compact history list reusing `.event-list` as-is -
deliberately not deduped or collapsed, matching how `PowerReading`
entries already appear unfiltered in the generic event list for
SmartPlug today.

**Deliberately not built:** a Telegram alert on `BatteryLow` flipping to
true. Asked directly; declined for now - dashboard visibility was the
actual ask, and the alert would be a straightforward mirror of
`OfflineDetectionRule`/`RecoveryDetectionRule` (ADR-005) if it becomes a
real need later, not a redesign.

## ADR-023 — RTSP capture gets a real timeout; a hung ffmpeg process was silently freezing the camera's status forever

**Root-caused live, not from first principles.** The dashboard showed
`camera-001` as `Unknown` - not `Offline`, not `Error`. Checked the real
Table Storage rows directly (`az storage entity query`) rather than
guessing: the agent's own heartbeat was fresh and had been running
continuously for 17+ hours with no restart; `motion-001` and `plug-001`
on the *same* agent were reporting fine over that whole window. Only
`camera-001` was frozen - `DeviceHeartbeatEntity.LastHeartbeatUtc` hadn't
moved since roughly an hour after the agent started.

**Traced through `OfflineDetection.Evaluate`, not assumed.** `Unknown`
specifically means `DeviceRuntimeState.LastActivityUtc` is still `null` -
distinct from `Error`, which the same method returns first if
`LastError` is set. Both `CameraCaptureExecutor.CaptureAsync` and
`RtspCamera`'s own reachability probe correctly set one or the other on
every failure path that's actually reached. Neither had ever fired for
this device, in 17+ hours - meaning the capture call itself had never
*returned* at all, success or failure, since shortly after the agent's
last restart.

**Root cause: `RtspCamera.CaptureAsync` had no timeout on the ffmpeg
subprocess it spawns.**
(`Vivnest.Infrastructure/Camera/RtspCamera.cs`) `await
process.WaitForExitAsync(cancellationToken)` was only ever cancelled by
the worker's long-lived `stoppingToken` (application shutdown), not a
per-capture deadline. A stalled RTSP stream - camera hiccup, a network
blip that doesn't cleanly close the TCP connection - leaves ffmpeg
running indefinitely, and the `await` never returns. Since
`CameraCaptureWorker.RunCaptureLoopAsync` runs each device as an
independent `Task` under `Task.WhenAll`, this froze *only* that one
device's loop permanently - every other device, and the agent's own
heartbeat, kept working normally the entire time, which is exactly why
nothing else looked wrong and no alert fired anywhere (Cloud's health
monitor only cascades on *agent* staleness, and the agent was never
stale).

**Fixed with a real deadline, matching the shape
`IsReachableAsync`/`ReachabilityTimeout` already used in the same file** -
`CaptureTimeout` (20s, a generous but bounded value for grabbing one
frame locally, not a tuned constant) links against the existing
`cancellationToken` via `CancellationTokenSource.CreateLinkedTokenSource`.
On timeout, `process.Kill(entireProcessTree: true)` (guarded against the
process having already exited in the same instant) and a `TimeoutException`
is thrown - which now flows through
`CameraCaptureService.CaptureAsync`'s existing catch block exactly like
any other capture failure, setting `LastError`/`LastFailureUtc` for real
this time, so the device correctly reports `Error` instead of freezing
silently as `Unknown`. Temp-file cleanup (`finally`) was widened to cover
the whole method body, not just the post-exit branch, so a
killed-on-timeout process still cleans up its partial temp file.

**Distinct from the `RtspCamera` process-handling fix in
EVOLUTION-PLAN.md's stabilization step** - that earlier fix was about
correctly cleaning up the process/`Process` object; this is about the
process never exiting in the first place. Related file, different bug.

## ADR-024 — First Cloud-to-Agent command: a dedicated queue per command, not a shared one; restart exits the process and lets Docker's restart policy do the rest

**The trigger, exactly as EVOLUTION-PLAN.md predicted it would arrive:**
a dashboard "restart this agent" button. Every queue in this codebase
until now flows Agent-to-Cloud only (ADR-004) - this needed genuinely new
infrastructure, not a config tweak. Raised directly alongside it: a
future "deploy a specific container version" button, since designing the
channel with zero foresight for a second command would likely mean
redesigning it a week later.

**One queue per command, not one shared queue with type-based routing -
checked against how this codebase already does it, not designed fresh.**
Every existing queue (`CameraCapturedQueue`, `DeviceHeartbeatQueue`,
`DeviceEventQueue`, etc.) is already single-purpose;
`DeviceEventQueueHandler`'s `EventType` switch only works because *one*
consumer (Cloud) handles every case it dispatches on. Restart and the
future Deploy command have two *different* consumers - Restart is
handled by the Agent process itself, but Deploy (pull a new image,
recreate the container) needs Docker access, which the Agent container
deliberately doesn't have (see ADR-020's Docker-socket refusal) - it
would need a separate host-level component. Azure Storage Queues have no
per-consumer filtering, so two different consumers sharing one queue
would each risk dequeuing a message meant for the other. `agent-restart-commands`
is its own queue for exactly this reason - Deploy gets its own queue
later, not a shared one requiring either consumer to selectively ignore
messages.

**`RestartCommandQueueMessage` deliberately doesn't follow ADR-004's
`{PartitionKey, RowKey}`-only shape.** That rule exists because those
messages reference an already-persisted Table Storage row the consumer
re-fetches; a command has no such row to reference - it *is* the
payload (`AgentId`, `IssuedAtUtc`). `AgentId` is carried purely so a
future multi-agent deployment sharing infrastructure could filter for
messages actually addressed to it - not exercised with today's single
agent, but cheap now and expensive to retrofit later.

**Publisher moved to `Vivnest.Core`, not duplicated.** `IQueuePublisher`/`AzureQueuePublisher`
already existed (Agent-side, `Vivnest.Infrastructure`) and was already
exactly the right shape - generic, no Agent-specific logic. Rather than
write a second, Cloud-only copy, it moved to `Vivnest.Core.Storage`
(alongside `AzureBlobStorageClient`, which already lives there for the
identical reason) so both Agent and Cloud share one implementation.
`Vivnest.Cloud`'s `IAgentCommandPublisher`/`AgentCommandPublisher` is the
thin, Cloud-specific piece on top - same split ADR-009 already
establishes between a shared low-level client and each side's own
higher-level wrapper.

**How "restart" actually restarts, without giving the Agent container
Docker access.** A .NET process can't cleanly relaunch itself from
inside a container. Checked what the real deployment already does
(`scripts/update-agent.ps1`) rather than assuming: the container already
runs with `--restart unless-stopped`. So the fix is almost embarrassingly
simple - `CommandPollingWorker` calls `IHostApplicationLifetime.StopApplication()`
on a matching command, and Docker's own restart policy brings the
container back with a fresh process. No Docker socket, no new
privileges, nothing for the Agent to know about Docker at all.

**Follow-up: that "the container already runs with `--restart unless-stopped`"
check was true of the *script*, not the *live container* - a real
production incident, not a hypothetical.** First real click of Restart
left the container sitting `Exited` instead of coming back. Traced end
to end (dashboard → `agent-restart-commands` queue →
`CommandPollingWorker` → `StopApplication()`) and confirmed the design
itself was correct - the gap was deployment drift, not the C# path: the
live container had been started before `update-agent.ps1`'s
`--restart unless-stopped` flag existed (or via some other invocation
that predated it), so it was actually running under Docker's default
policy (`no`). A clean exit under `no` just stops, forever - nothing in
this repo checks or enforces a running container's actual restart
policy (no compose file, no IaC, no `docker inspect` verification
anywhere). Confirmed via `docker inspect vivnest-agent --format
"{{.HostConfig.RestartPolicy.Name}}"` on the host, fixed by re-running
`update-agent.ps1` to recreate the container with the policy actually
applied this time. Restart now works. The underlying gap - nothing
here verifies a live container's restart policy before this design
depends on it - stays open; worth a health-check or startup assertion
if this happens again, not fixed speculatively now for a single
confirmed one-off.

**Agent-side consumption is polling, not push - matches every other
Agent-side integration's shape (Storage Queues, not a listener).**
`CommandPollingWorker` (15s interval, not tuned, generous for something
as infrequent as a manual restart click) is the Agent's first-ever queue
*consumer* - every other queue interaction from the Agent side has been
publish-only until now. Deletes each message before processing (not
after) - a simple, non-retrying design: occasionally losing a restart
request to a rare transient error is a smaller problem than a malformed
message crash-looping this worker forever, and there's no poison-queue
handling here the way Azure Functions' queue triggers get for free.

**REST endpoint reuses `/agents*`'s existing tenant-scoping exactly, not
a new auth tier (yet).** `POST /agents/{agentId}/restart` is gated
identically to `GET /agents/{agentId}` - `DevicesOnly` keys get 403, and
the agent must resolve for the caller's own tenant before the command
publishes (ADR-008: a tenant key must not be able to restart a different
tenant's agent by guessing an id). Flagged directly, not deferred
silently: the *planned* Deploy command is a meaningfully bigger privilege
(arbitrary container replacement, not a temporary monitoring gap) and
will need stricter gating than this when it's built - restart borrows
today's tier because it's genuinely proportionate to restart's actual
blast radius, not because the tiers were assumed to be reusable as-is.

**Dashboard's first mutating request.** Every prior dashboard call has
been a read; `restartAgent` in `api.ts` doesn't reuse the shared `request<T>()`
helper since the endpoint returns `202 Accepted` with no JSON body to
parse. A native `window.confirm()` guards the button rather than a
custom modal component - consistent with ADR-018's "no UI framework
dependency" stance, not worth a dependency (or even a hand-rolled modal)
for one confirmation.

## ADR-025 — Remote agent config: an additive blob layered on top of local config, not a replacement for it; dynamic DLL loading considered and declined

**Status: Agent-side only.** The Agent downloads and layers a per-agent
config blob on startup - that part is real and live. Everything Cloud-side
that was built on top of it during this same conversation (the `GET`/`PUT`
`/agents/{agentId}/config` REST endpoints, server-side redaction, the
dashboard's config editor, and a whole-blob encryption layer) was built,
then explicitly walked back, in stages, by direct instruction - "I only
want the agent changes; if the GET APIs aren't used by the agent, remove
those too." They weren't - the Agent talks to Blob Storage directly with
its own `Storage:ConnectionString`, never through the Cloud API - so all
of it came back out. What's documented below as "built" in past tense but
absent from the code today is intentional history, not drift: the
reasoning stays useful if any of it gets revisited, and it's all still in
git history regardless. Uploading a new config blob today means writing
directly to `agent-config/{agentId}.json` in Blob Storage (`az storage
blob upload` or the Azure Portal) - there is no dashboard path for it.

**The ask, in two parts, from the same conversation as ADR-024's Deploy
discussion:** (1) restructure the Agent into a stable "shell" (Restart,
Logs, AgentHeartbeat) plus dynamically-loaded business logic, so a code
update is a DLL swap instead of a new container; (2) make `Devices[]`/
`HomeAssistant`/etc. editable remotely instead of hand-edited on the host.

**Part 1 - dynamic DLL loading - considered and declined, reasoning worth
keeping since it'll come up again.** `EVOLUTION-PLAN.md` already lists
"Plugin architecture / dynamic capability loading" as explicitly deferred
until a second real consumer needs it - one agent doesn't meet that bar.
Beyond that standing rule, three concrete problems: cleanly hot-swapping
running `BackgroundService`s (stop old workers, tear down their DI
registrations, start new ones without leaking connections or corrupting
in-flight state) is genuinely hard to get right, and most systems that
attempt it restart the process anyway to load cleanly - at which point
nothing was saved over a container restart. `AssemblyLoadContext` (the
actual .NET mechanism) has real, well-documented pitfalls: unloading a
previous version cleanly is finicky, and a shared type (from
`Vivnest.Core`) loaded into two different contexts can fail type-identity
checks in ways that are unpleasant to debug on a headless device. And
functionally it duplicates what a container image already provides -
`docker pull`/`stop`/`run` *is* "download new code, run it," with layer
caching, versioning, and rollback already mature and free. The
already-scoped Deploy feature (ADR-024) gets the same outcome - new code
running, chosen from the dashboard - without a second, custom,
higher-risk update mechanism alongside the one that already exists.

**Part 2 - config-as-data, not code-as-data - accepted, and this is what
got built.** Genuinely different risk profile from part 1: no runtime
type loading, no process-internals surgery, just one more blob download
layered into `IConfiguration` alongside what the Agent already does
every startup.

**Deliberately additive/overriding, not a replacement requiring local
config to be stripped down.** Local `appsettings.json` (repo dev copy
and the real host's `C:\vivnest-agent\appsettings.json` alike) keeps its
full existing shape unchanged - `Devices[]`, `HomeAssistant`, everything.
The remote blob, when present, layers *on top* via `IConfigurationBuilder.Sources`,
overriding matching keys the normal `IConfiguration` way. This was a
real revision mid-conversation: the first framing implied stripping local
config down to a bootstrap minimum, which would have made local `dotnet run`
depend on a live Storage blob to do anything, and (worse) risked a
production agent booting with zero devices configured if the blob upload
sequencing ever raced a redeploy. Additive-only avoids both risks entirely
- an agent with no blob uploaded yet behaves exactly as it always has.

**What's genuinely irreducible and stays local, checked by reasoning
through the bootstrap chicken-and-egg, not assumed:** `Agent:AgentId`
(so the agent knows which blob is "mine") and `Storage:ConnectionString`
(the actual requirement - you need Storage access before you can
download anything that would tell you more). Nothing else needs to be
pinned local. Even `AgentHeartbeat`'s interval, initially treated as
special, doesn't need to be - it already has (or should have) a safe
C#-level default the way `BatteryReportInterval`/`SinkCleanlinessOptions`
do, so a missing/failed remote fetch degrades to that default rather
than breaking heartbeat.

**Table/queue *names* are a separate question from what's local vs.
remote, raised directly rather than left conflated.** They're not
config that varies per deployment at all - every agent writes to the
same `tblAgentHeartbeat`/`device-events`/etc., multi-tenancy already
works by scoping rows *within* shared tables (`PartitionKey =
"{TenantId}|{SiteId}"}`), not by giving each tenant separate
infrastructure. The honest fix would be hardcoding them as constants in
`Vivnest.Core` (like `DeviceEventTypes`) instead of configuration at
all, local or remote - **not done in this change**, scoped out
deliberately to keep this diff to the actual feature requested rather
than an unrelated cross-cutting refactor touching every `TablesOptions`/`MessagingOptions`
consumer in both Agent and Cloud. Worth doing later, on its own.

**Also raised and worth remembering: making a queue/table name
remotely configurable doesn't actually deliver "add a new queue without
a deploy."** A new queue is only useful paired with new code that reads
or writes it - a new worker or handler. That's a code change regardless
of where the *name* lives. Config only helps when a *value* changes
(a camera's IP, a threshold, an interval); adding new capability is
inherently a code change, and belongs to the Deploy feature (ADR-024),
not this one.

**Mechanically:** `Program.cs` reads `Agent:AgentId`/`Storage:ConnectionString`
from `builder.Configuration` *before* adding any remote source (bootstrap
values must come from local config/env vars alone), downloads
`agent-config/{agentId}.json` best-effort, and inserts it as a
`JsonStreamConfigurationSource` at the position immediately before the
environment-variables source in `builder.Configuration.Sources` - not
appended, which would put it *after* env vars and silently break the
existing `docker run -e HomeAssistant__BaseUrl=...` override (the
ADR-016 Docker-networking fix). Any failure (blob missing, network
error, malformed JSON) is caught and logged; the agent proceeds on local
config alone exactly as it always has - verified live, not just reasoned
through: ran the agent locally against the real (then-empty) Storage
account and confirmed the "no remote config blob found" fallback path
before the blob existed, then again after uploading an initial `{}`
blob via `az storage blob upload`.

**Everything below this point describes Cloud-side work that was built
during this conversation and then fully removed - kept as a record of
what was tried and why, not as a description of anything currently in
the tree.**

Built: `GET`/`PUT /agents/{agentId}/config` REST endpoints, gated
identically to `/agents/{agentId}/restart` (tenant-scoped,
`DevicesOnly`-excluded); a raw-JSON-textarea config editor on the
dashboard's Agent Detail page, explicitly not auto-triggering a restart
on save so "save config" and "apply it" stayed separate actions; and,
once the editor made it obvious the blob (and the API responses serving
it) carried plaintext device credentials, two further layers on top:

- **Whole-blob AES-256-GCM encryption** (`Vivnest.Core.Storage.AesGcmProtector`,
  shared by Cloud and Agent). Asymmetric encryption was considered first
  and declined - the editor needs Cloud to decrypt existing content to
  show it, so Cloud would need decrypt capability regardless, which
  defeats the property asymmetric keys are usually chosen for. Built,
  verified end-to-end against the real blob, **then explicitly reverted**
  ("I only want redacted") - `AesGcmProtector`/`AgentConfigOptions`
  deleted outright rather than left disabled, the blob migrated back to
  plain JSON. The reasoning stays valid if direct-Blob-Storage-access
  protection (as opposed to API-response exposure) becomes a real
  concern later - it protects a genuinely different threat than
  redaction does, and Azure Storage's default at-rest encryption covers
  the baseline case for free regardless.
- **Server-side redaction** (`AgentConfigProtection.Redact`/`FillUnchangedSecrets`,
  `Vivnest.Cloud/Api`) - this was the piece that actually closed the real
  risk (a *valid* tenant key reading plaintext secrets straight through
  `GET`, which encryption alone never addressed, since Cloud always had
  to decrypt for the editor anyway). Nulled known-sensitive field values
  (`Password`, `RtspPassword`, `Username`, `RtspUsername`, `AccessToken`)
  before responses were built; `FillUnchangedSecrets` made that
  round-trippable by filling redacted-and-untouched fields back in from
  the stored blob on `PUT`, merging object fields by key and array
  elements by index (not a semantic key like `DeviceId` - a known,
  accepted limitation, never resolved before the endpoint itself was
  removed). Field-level encryption and Azure Key Vault were both raised
  as alternatives and declined - Key Vault specifically because the
  Agent isn't Azure-hosted and would need its own service-principal
  credential to authenticate to it, no simpler than the key it would
  replace.

**Then all of it - endpoints, editor, redaction - was removed in the same
conversation**, once it was confirmed the Agent never called the API at
all (it reads Blob Storage directly). `IBlobStorageService.UploadAsync`
(added only to support the `PUT` endpoint) and `AgentConfigProtection.cs`
were deleted with it, not left dangling. `IAgentQueryService`/
`IAgentCommandPublisher` (the restart feature, ADR-024) were untouched -
a different feature that happened to live in the same file.

## ADR-026 — `Vivnest.Agent` reorganized by capability, not by architectural layer; a formal plugin/package system considered and declined

**The trigger:** a proposal (via a separate AI conversation the user
brought in and asked for a second opinion on) to restructure the Agent
into a "stable runtime shell" plus independently-versioned,
independently-deployed "capability packages" - a manifest format
(`camera.cap` containing `Camera.dll`/`deps.json`/`manifest.json`),
per-capability config files, version-compatibility rules (`Camera ≥2.1
requires Runtime ≥1.5`), and a capability repository service - explicitly
modeled on Home Assistant integrations, VS Code extensions, and Kubelet's
update model.

**Declined, on the same grounds ADR-025's dynamic-DLL-loading section
already established, extended further.** Every cited example (Home
Assistant, VS Code, JetBrains, Kubelet, Datadog Agent, CrowdStrike) exists
to solve a coordination problem this project doesn't have: independent
parties releasing on independent schedules, or a fleet large enough that
you can't just look at it. Every capability in this codebase is written
by the same person, in the same repo, usually in the same commit as
whatever runtime change it needs - there is no version skew to protect
against, because there is no independent release process. A package
manifest, a compatibility matrix, and a capability repository service are
real infrastructure investments (a schema to design, a repository
service to build and host, compatibility-checking logic to maintain) to
solve a problem that would first need a second physical deployment or a
second developer to even exist. This is the same "second real consumer"
rule `EVOLUTION-PLAN.md` has applied consistently throughout this log -
applied here to the single largest infrastructure proposal raised so far.

**What was accepted: the organizational idea, at zero infrastructure
cost.** Reorganized the existing single project so each capability's
worker, service, event handler(s), and event(s) live together in one
folder/namespace instead of scattered across `Runtime/Workers`,
`Runtime/EventHandlers`, `Runtime/Events`, and `Services` - the exact
readability problem the plugin proposal was also trying to solve, gotten
for the cost of a mechanical move-and-renamespace instead of a new
subsystem:

- `Vivnest.Agent/Capabilities/Camera/` - `CameraCaptureWorker`,
  `CameraCaptureExecutor`, `CameraCaptureService` (+interfaces),
  `CameraCaptureHandler`, `CameraCaptureFailedHandler`, both capture
  events.
- `Vivnest.Agent/Capabilities/SmartPlug/`, `MotionSensor/`,
  `HomeAssistant/` - same shape, one folder per device
  integration. The old empty `Capabilities/HomeAssistant.cs` placeholder
  stub was deleted outright once a real, actively-used
  `Capabilities/HomeAssistant/` folder existed right next to it -
  confusing to leave a dead file with the same name beside the real
  thing.
- `Vivnest.Agent/Capabilities/DeviceHealth/` - `DeviceHeartbeatWorker`/`Handler`/`Event`
  plus `OfflineDetection`/`IOfflineDetection` (moved out of the old
  top-level `Capabilities/` folder) - cross-device status evaluation,
  not one capability's concern.
- `Vivnest.Agent/Capabilities/Triggers/` - `DeviceTriggeredEvent`,
  `MotionTriggerResolverHandler`, `CaptureOnTriggerHandler` (ADR-021) -
  the generic device-triggers-device mechanism, which by design spans
  more than one capability (motion → camera today), so it isn't at home
  inside either one.
- `Vivnest.Agent/Runtime/Shell/` - `AgentHeartbeatWorker`/`Handler`/`Event`,
  `AgentMetricsWorker`/`Handler`/`Event`, `CommandPollingWorker`
  (ADR-024's restart consumer), `NetworkUsageTracker` - the pieces that
  aren't a device capability at all, named "Shell" to match exactly how
  it was described when proposed ("a shell with the ability to Restart,
  Logs, AgentHeartbeat").
- `Runtime/Dispatching` (`EventDispatcher`) and the three fully generic
  `Interfaces/` types (`ICapability`, `IEventHandler<T>`,
  `IEventDispatcher`) are the only things that didn't move - genuinely
  capability-agnostic runtime machinery, not specific to any one
  integration. `Capabilities/SnapshotScheduler.cs` also stayed exactly
  where it was, at the top level, deliberately not filed under any
  capability - its purpose still isn't decided (see EVOLUTION-PLAN.md's
  original note on it), and this reorg isn't the moment to guess.

**One deployable unit throughout - no new assemblies, no plugin loader,
no manifest format.** Every file still compiles into the same
`Vivnest.Agent.dll`; this is namespace/folder hygiene, not an
architecture change. Verified as more than a successful compile: ran the
reorganized agent locally against the real Storage account afterward,
confirmed every worker started and the remote-config fetch (ADR-025)
still worked - and noticed a real, unplanned benefit doing so: log
categories are now self-documenting (`Vivnest.Agent.Capabilities.Camera.CameraCaptureWorker`
instead of the old generic `Vivnest.Agent.Runtime.Workers.CameraCaptureWorker`),
since .NET's default logger category is the fully-qualified type name.

**If a formal capability-package system becomes worth building later**,
this reorganization is exactly the boundary it would need anyway - the
folders it would need to become independently-versioned packages already
exist and already contain the right files. Nothing here forecloses that;
it just doesn't pay for infrastructure the project doesn't need yet.

**Follow-up, same conversation: `HomeAssistant` moved one level deeper,
under a new `Capabilities/Bridges/` folder - `Capabilities/Bridges/HomeAssistant/`,
not a sibling of Camera/SmartPlug/MotionSensor.** Raised and initially
declined on "second real consumer" grounds identical to the rest of this
entry - `Bridges/` has exactly one member today, and a single-item
grouping folder doesn't organize anything a plainly-named folder
wouldn't already say. Built anyway, by direct instruction, after that
tradeoff was made explicit rather than silently. The distinction it
encodes is real even with one member: HomeAssistant isn't a device
capability the way Camera/SmartPlug/MotionSensor are - it's a bridge that
can carry *any* device type through it (the smart plug's dual-path
reachability, ADR-016, is direct proof), so grouping it as a peer to
device-specific capabilities was always slightly inaccurate, independent
of how many bridges exist. `MQTT`/`ONVIF`/`Zigbee` (`roadmap.md` Phase 4)
would be the natural next members if any of them get built as a generic
bridge rather than a direct protocol implementation - `Bridges/` is
already the right place for them to land without another reorganization.

## ADR-027 — Agent log download: an in-process `ILoggerProvider` buffer shipped to Blob Storage, not `docker logs` or host/socket access

**The trigger:** wanting to download an agent's recent logs from its
Dashboard detail page. The first framing considered was literal - some
way to run `docker logs` against the container and surface the output -
but that requires either a host-side script Cloud can somehow invoke, or
giving the Agent container access to the Docker socket, both of which are
a materially bigger privilege/infrastructure grant than the problem
("see what a worker recently logged as a warning/error") actually needs.
Reframed and confirmed directly with the user: an app-level
`ILoggerProvider` + `BackgroundService`, not `docker logs`.

**Shape mirrors the two closest existing precedents on purpose, not by
coincidence.** The upload side (`LogShippingWorker`) is
`AgentMetricsWorker` with the payload swapped - its own `BackgroundService`,
its own `PeriodicTimer` (`AgentLogShippingOptions.FlushInterval`, default
5 minutes), its own try/catch, so a shipping hiccup can never touch
anything else, least of all the very logging it's trying to ship. The
download side (Cloud's `GET /agents/{agentId}/logs`) is
`DeviceQueryService.TryGenerateImageUrl` with the blob swapped - resolve
a short-lived SAS read URI (`IBlobStorageService.GenerateReadSasUri`, 15
minutes, same duration as `DeviceQueryService.ImageUrlValidFor`) rather
than proxying the blob's bytes through the Function.

**Captures Warning+Error only, by default - a deliberate, configurable
floor, not a missing feature.** Several workers already log at
Information level every tick (`AgentMetricsWorker`, `CommandPollingWorker`);
shipping all of that would be mostly noise and a lot of blob churn for a
single-agent deployment. `AgentLogShippingOptions.MinimumLevel` (default
`Warning`) controls only what the *shipped* buffer captures - the
existing Console provider, and whatever `Logging:LogLevel` config already
governs it, is untouched, so local/`docker logs` verbosity is unaffected
either way.

**A capped ring buffer (`AgentLogBuffer`, default 500 lines,
`AgentLogShippingOptions.MaxBufferedLines`), not unbounded, and not
persisted across restarts.** "Download recent problems" is the actual use
case, not a full audit history - Table Storage or a growing blob would be
the shape for that, and nothing today needs it. Oldest lines drop first
once the cap is hit.

**Constructed before `builder.Build()`, registered as the same singleton
afterward - not the usual `Configure<T>()`-then-inject pattern.**
`builder.Logging.AddProvider(...)` needs a live provider instance
immediately, before the DI container exists, since loggers get created as
the host composes. `Program.cs` binds `AgentLogShippingOptions` directly
from configuration (not via `IOptions<T>`, which isn't available yet),
constructs `AgentLogBuffer` with it, registers that exact instance as
`IAgentLogBuffer` so `LogShippingWorker` (constructed later, through
normal DI) reads from what the provider writes to, and wraps it in
`AgentLogBufferLoggerProvider`. `LogShippingWorker` itself uses the normal
`IOptions<AgentLogShippingOptions>` pattern for `FlushInterval`/`Enabled`,
consistent with every other worker - only the buffer's construction is
unusual, and only because the logging pipeline forces it to happen early.

**Each flush overwrites the blob wholesale with the buffer's current
snapshot; there's no append and no explicit clear-after-flush.** Simpler
than the alternative and avoids any window where a line could be lost
between "clear" and "next line added" - the buffer's own ring-buffer cap
is what keeps old content bounded, not the upload step.

**`GET /agents/{agentId}/logs` reuses `/agents*`'s existing tenant-scoping
exactly, same as `POST /restart` (ADR-024).** `DevicesOnly` keys get 403,
and the agent must resolve for the caller's own tenant before a SAS URI
is generated - without that check, any valid tenant key could read
another tenant's agent logs by guessing an id (ADR-008). The SAS URI is
generated unconditionally, without checking the blob actually exists yet
(same as `TryGenerateImageUrl`) - if the agent hasn't shipped a log blob
yet, the signed URL simply 404s when opened, which is an acceptable
first-open experience rather than a reason to add an existence check.

**Dashboard: `getAgentLogs` fetches the SAS URL through the normal
`request<T>()` helper (unlike `restartAgent`, this endpoint returns a
JSON body), then `window.open(url, "_blank")` - no proxying, no in-page
viewer.** The button sits next to Restart in a new `.detail-header-actions`
wrapper, styled distinctly (`.logs-button`, neutral border) rather than
reusing `.restart-button`'s warning styling, since downloading logs isn't
a disruptive action the way restarting the process is.

## ADR-028 — Deploy: a new standalone `Vivnest.Agent.Updater` process on the host, not Docker access inside the Agent; Watchtower considered and deferred

**The trigger:** a "Deploy latest" button on the Agent Detail page,
exactly the feature ADR-024 named and deliberately didn't build -
"Restart is handled by the Agent process itself, but Deploy... needs
Docker access, which the Agent container deliberately doesn't have
(ADR-020's Docker-socket refusal) - it would need a separate host-level
component." This entry is that component.

**Watchtower considered directly, not just named and skipped.** Discussed
before building anything: Watchtower still needs the exact same
queue-polling component this design already requires, since the host is
behind NAT and Cloud can't reach a local HTTP API directly - it would only
make the poller thinner (forward a signal to Watchtower's local API
instead of running `docker pull/stop/rm/run` itself), while requiring
`/var/run/docker.sock` mounted into *its own* container - the identical
access-surface tradeoff already rejected once in this log (reading Docker
Engine version info from inside the Agent's own container, ADR-020: "would
hand a monitoring agent effective control over the whole host's Docker
daemon... not worth that access surface for a diagnostics nicety"). And
Watchtower's actual strength - continuous background polling, digest
comparison, run-flag introspection, auto image cleanup across a fleet -
isn't the shape of what was asked for (on-demand, single host, always
`:latest`). Matches this log's own prior timing call almost exactly:
"Watchtower - the natural next step once there's more than one
agent/host, not built speculatively now for one" (ADR-020's `FirmwareVersion`
follow-up). Deferred again, for the same reason, now confirmed rather than
assumed.

**`Vivnest.Agent.Updater` — a new, separate deployable, not a feature of
`Vivnest.Agent`.** This is a genuine exception to "the runtime never
references capabilities" / "one deployable project" - not a violation of
it, because the boundary being drawn is a *process* boundary the Agent
container is deliberately kept on the wrong side of. It runs natively on
the host (never inside a container), polls a new `agent-deploy-commands`
queue (own queue, same one-per-command convention `agent-restart-commands`
already established, ADR-024) via the same raw `QueueServiceClient`
polling shape as `CommandPollingWorker` (30s default interval, configurable
via `DeployOptions.PollInterval` rather than hardcoded like
`CommandPollingWorker`'s own 15s — a standalone host process is easy to
reconfigure without a rebuild, so there was no reason not to; delete-before-process,
non-retrying), and on a match shells out to `docker pull` / `stop` / `rm` /
`run` - the same four commands `scripts/update-agent.ps1` already runs by
hand, now automated instead of hand-typed. `Process.Start` uses
`ProcessStartInfo.ArgumentList`, not a concatenated command string - the
same argument-injection class of bug already fixed once in this codebase
(`RtspCamera`, EVOLUTION-PLAN.md step 1).

**Own config file, `updater.settings.json`, deployed into the same folder
as the Agent's `appsettings.json` - deliberately not the same file.**
First cut reused the Agent's own `appsettings.json` directly (one file,
no risk of the two processes disagreeing about which agent they are) -
reconsidered immediately once a real deployment detail surfaced: both
executables' publish output lands in the same host folder
(`C:\vivnest-agent`), and `Host.CreateApplicationBuilder`'s default
`appsettings.json` convention would make copying the Updater's own build
output overwrite the Agent's real, secret-bearing config file. Renamed to
`updater.settings.json`, added to `builder.Configuration.Sources`
manually (not the `AddJsonFile` convenience extension) so it can be
inserted before the environment-variables source - same ordering
convention `TryLoadRemoteConfigAsync` already established on the Agent
side, so an env var override still wins over this file, same as it
already wins over `appsettings.json` everywhere else in this codebase.
Binds only `Agent:AgentId` and
`Messaging:ConnectionString`/`Messaging:DeployCommandQueue` - a small,
separate file, not a risk to the Agent's real secrets. The bind-mount path passed to `docker run`
is derived from `AppContext.BaseDirectory` (wherever the executable
itself was actually placed), not a hardcoded `C:\vivnest-agent` literal -
deliberately, since the host might not stay Windows. Plain .NET was
chosen over a PowerShell-loop specifically for this reason: `dotnet
publish -r linux-arm64 --self-contained` produces a Raspberry Pi build of
the identical code with zero changes, where a PowerShell script would
need a second implementation. Only the OS-level startup wrapper differs
by host - a Windows Scheduled Task today, a systemd unit on a future Pi -
never the executable itself.

**Image and container name are hardcoded constants
(`vivnestagentacr.azurecr.io/vivnest-agent:latest`, `vivnest-agent`), not
config, and the queue message carries no deploy-time configuration
(`DeployCommandQueueMessage` is `{AgentId, IssuedAtUtc}`, identical shape
to `RestartCommandQueueMessage`).** Both are deliberate v1 scope cuts, not
oversights - discussed directly and deferred: always deploy `:latest`
rather than building a version picker now, and don't let the queue
message carry arbitrary deploy-time config (env var overrides, a specific
tag) until something real needs it. The message shape is the natural
extension point for that later - adding fields to
`DeployCommandQueueMessage` and having `DeployPollingWorker` apply them to
the `docker run` invocation is additive, not a redesign.

**REST endpoint gating: same tenant tier as Restart, a conscious choice
against this log's own earlier flag, not a silent reversal of it.**
ADR-024 explicitly called out that "the *planned* Deploy command is a
meaningfully bigger privilege (arbitrary container replacement, not a
temporary monitoring gap) and will need stricter gating... when it's
built." Built anyway on today's tenant tier (`DevicesOnly` 403 + tenant-owns-agent
check, identical to `POST /agents/{agentId}/restart`) because there is
exactly one tenant in practice today, so a separate, stricter auth tier
has no real consumer to justify it yet - revisit this specific gating
choice the day this platform is genuinely multi-tenant, not before.

**Firmware version updates automatically, no new code needed.** A
recurring question once Deploy was real: does the dashboard's Firmware
field reflect the new build after a deploy? Yes, for free - `FirmwareVersion`
is already baked into each image at `docker build` time as
`Agent__FirmwareVersion` (ADR-020's follow-up), and `AgentHeartbeatWorker`
already reads it fresh into every heartbeat. A newly-recreated container
is a newly-started process reading its own image's baked-in env var, so
its very first heartbeat after a deploy reports the new commit SHA with
zero changes to this feature.

## ADR-029 — Capture image caching: immutable Cache-Control headers, longer SAS validity; the actual browser-cache-miss cause (SAS URLs regenerate every call) left open

**The trigger:** asked directly whether there's any caching policy on
capture photos served through the dashboard. There wasn't - checked, not
assumed: `AzureBlobStorageClient.UploadAsync` passed no `BlobHttpHeaders`,
`GenerateReadSasUri`'s `BlobSasBuilder` set no response-header overrides,
the dashboard renders plain `<img src>` with no client-side cache logic,
and no Cloud Function response sets `Cache-Control`. Entirely
default/unconfigured behavior at every layer.

**What actually got fixed: two independent, correctness-only changes,
both safe because captures are genuinely immutable.** Each capture gets
its own unique, timestamp-named blob (`IBlobNameGenerator`) that's never
overwritten - unlike the config blob (ADR-025) or the log blob (ADR-027),
which both get overwritten repeatedly, "cache this forever" is actually
true for a capture once it exists.

1. `AzureBlobStorageClient.UploadAsync` gained an optional `BlobHttpHeaders?`
   parameter (routed through `BlobUploadOptions` instead of the old
   `overwrite: true` convenience overload - `BlobUploadOptions` with no
   `Conditions` set is the identical unconditional-overwrite behavior, not
   a change in semantics). `AzureBlobStorage` (`Vivnest.Infrastructure`,
   the `IPhotoStorage` implementation - the *only* thing that ever uploads
   through this path) now always passes
   `Cache-Control: public, max-age=31536000, immutable` and
   `Content-Type: image/jpeg`. `LogShippingWorker`, the client's other
   real caller, passes nothing (its blob's content changes every flush -
   this must never get a long-lived cache header).
2. `GenerateReadSasUri` gained an optional `cacheControl` parameter - a
   SAS response-header override (the `rscc` query parameter), not a
   change to the blob's own stored headers. `DeviceQueryService.TryGenerateImageUrl`
   passes the same immutable directive; `AgentsFunction.GetAgentLogs`
   (ADR-027) deliberately does not. This is what makes the fix apply to
   every capture already sitting in Blob Storage, not just ones uploaded
   after this shipped - the override wins regardless of what the blob's
   own headers say.

**`ImageUrlValidFor` raised from 15 minutes to 24 hours**, by direct
request, after a genuinely long browser session (a tab left open, a slow
retry, scrolling back through an already-loaded gallery) outlived the old
window mid-view.

**What this does *not* fix, flagged directly rather than left implied:
repeat page loads still don't cache-hit.** `DeviceQueryService` signs a
*fresh* SAS - new `ExpiresOn`, new signature, new query string - on
every single API call, `ImageUrlValidFor` duration notwithstanding. A
`Cache-Control` header only lets the browser skip re-fetching a URL it's
already seen; if the URL itself is different every time (because the
signature changed), there's nothing to hit. So today's fix guarantees
correct headers on whatever image bytes do get fetched, and reduces
mid-session expiry, but doesn't reduce Blob Storage egress or speed up a
second visit the way "real" caching would. Closing that gap would mean
making the SAS deterministic within some window (e.g. rounding
`ExpiresOn` to a fixed time bucket so repeated calls within that bucket
produce an identical URL) - a real design decision, not a drive-by fix,
since it trades a slightly larger worst-case SAS lifetime for
cacheability. Not built now; flagged for whenever repeat-load performance
or egress cost becomes the actual, felt problem rather than a
theoretical one.

## ADR-030 — Device list thumbnails: a live per-device latest-capture query, not a denormalized heartbeat field like Timezone/Brand/Model/Firmware

**The trigger:** dashboard work to show each camera device's latest
capture as its list-row thumbnail instead of a generic icon. The
established pattern for adding a device-list field this round of work
(Timezone, Brand, Model, Firmware) was: stamp it onto `DeviceHeartbeat`
from data the Agent already has locally, denormalized so Cloud never
needs a cross-entity lookup. That pattern doesn't fit here.

**Why the heartbeat pattern doesn't fit.**
`DeviceHeartbeatWorker.ProcessDeviceHeartbeat` only publishes a new
heartbeat when `DeviceRuntimeState.LastReportedStatus` actually changes
(ADR-005) — not on every capture, not every tick. Stamping
`DeviceRuntimeState.LastBlobName` onto the heartbeat the way
Timezone/Brand/Model/Firmware are stamped would freeze the thumbnail at
whatever was captured near the last status change, then go stale
immediately after and never update again until the next status flip —
potentially hours or days later. Same staleness that motivated dropping
"Last heartbeat" from the dashboard UI entirely; worse here, since it's a
visibly wrong photo rather than a hidden timestamp.

**What got built instead:** `DeviceQueryService.TryGetThumbnailUrlAsync`
queries `IDeviceEventReader.GetByDeviceAsync(..., eventType:
CameraCaptured, take: 1)` fresh, per device, per request — the exact same
lookup `GetDeviceCapturesAsync` already does with a larger `take`, just
bounded to the single latest row. Only devices with `DeviceType ==
"Camera"` incur the extra query; every other device type gets
`ThumbnailUrl: null` with no query at all. `GetDevicesAsync` fans these
out with `Task.WhenAll` rather than awaiting one at a time. The resulting
URL is generated through the same `TryGenerateImageUrl` →
`GenerateReadSasUri` path captures already use, so it inherits the
immutable `Cache-Control` header and 24-hour SAS validity from ADR-029
for free.

**Cost accepted:** N extra Table Storage queries per `/devices` call, one
per camera device, versus zero for the denormalized fields. Deliberately
accepted rather than engineering around it (e.g. a Cloud-side "latest
capture per device" projection table) — negligible at current device
counts, and a stale photo is a worse failure mode than a few extra
point-reads. Revisit if per-site device counts grow enough to make this a
real cost.

**Not built:** an actual resized/optimized thumbnail image.
`ThumbnailUrl` points at the same full-resolution capture blob the
gallery already serves, scaled down via CSS on the frontend — identical
shape to how capture images work elsewhere (see ADR-029), just surfaced
on `/devices` and `/devices/{deviceId}` too now.

## ADR-031 — Capture cancellation during shutdown/restart no longer logged and recorded as a capture failure

**Found while re-checking ADR-023's timeout fix**, not from a reported
bug. `RtspCamera.CaptureAsync` (ADR-023) correctly distinguishes a
timeout-triggered cancellation from a genuine external one and lets the
latter propagate as `OperationCanceledException`. But two layers up,
`CameraCaptureService.CaptureAsync` and `CameraCaptureExecutor.CaptureAsync`
each had an unconditional `catch (Exception ex)` that predates ADR-023 —
neither distinguished "the caller cancelled us" from "the capture actually
failed." A genuine cancellation got logged at `LogError`, turned into a
`CameraCaptureResult { Success = false, Error = "The operation was
canceled." }`, and had `runtime.LastError`/`LastFailureUtc` set on the
device — indistinguishable from a real fault.

**Why this went from latent to actively reachable:** the outer
`CancellationToken` threaded through this whole chain is
`CameraCaptureWorker`'s `stoppingToken`, which is the same token
`IHostApplicationLifetime.StopApplication()` cancels. ADR-024's restart
button calls exactly that. So any Restart click that lands while a capture
is in flight now logs two spurious `ERROR` lines (the second from
`PublishCaptureFailedSafeAsync` failing to publish with an
already-cancelled token) and records a misleading `LastError`, purely as a
side effect of an intentional, graceful shutdown.

**Fix:** both methods gained
`catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`
ahead of their generic `catch (Exception ex)`, so real cancellation
propagates as cancellation — which `BackgroundService`/the Generic Host
already handles gracefully on shutdown — instead of being recorded as a
device error. `RtspCamera.CaptureAsync` itself (ADR-023) needed no change;
this was entirely in the two callers above it.

## ADR-032 — Sink-cleanliness ML: on-device ONNX classifier chosen; cloud vision LLM, fine-tuned object detector, and one-class anomaly detection considered and declined for now

**Context.** The heuristic edge-density approach (originally shipped and
then reverted alongside ADR-023, see git history around `34351b7`) couldn't
separate "actively cooking, counter cluttered, basin empty" from "actually
needs cleaning" — both look similar in pixel-level edge density. Revisiting
as a real ML classifier, first genuine ML feature in the codebase, so the
options were laid out before picking one.

**Options considered:**

1. **On-device classifier (chosen).** Small model (MobileNet/EfficientNet-lite)
   fine-tuned on labeled captures pulled from this device's own history,
   exported to ONNX, run locally via `Microsoft.ML.OnnxRuntime` — same
   edge-inference constraint the heuristic already lived under. Fully
   private (photos never leave the device beyond the Blob Storage upload
   that already happens today), no per-inference cost, no new network
   dependency. Cost: needs a labeling workflow, 150-300+ hand-labeled
   examples, and a Python/PyTorch training pipeline that lives outside this
   .NET solution as a one-off script producing an `.onnx` artifact.

2. **Cloud vision LLM call (declined for now).** Send the capture to a
   multimodal model with a prompt asking clean/dirty. Needs zero training
   data and is the only option that could plausibly reason about the
   cooking-clutter-vs-real-mess distinction directly. Declined as the
   primary path because it's a real architecture step up from everything
   else in this codebase: today capture images go to this tenant's own
   Blob Storage and nowhere else — sending them to a third-party API for
   every inference is a materially bigger privacy exposure for real home
   photos, plus a per-call cost and a new hard network dependency for
   something currently offline-tolerant. Worth a cheap one-off gut-check
   against existing sample photos before committing to option 1's labeling
   effort, but not the shipped path.

3. **Fine-tuned object detector, e.g. YOLO-style (declined).** Detect
   specific objects (dishes, food debris) rather than a binary score — more
   interpretable output, but COCO-pretrained weights don't cover "dirty
   dish"/"food residue" well, so it would need the same fine-tuning
   investment as option 1 for a more complex model and more moving parts,
   without a clear benefit over a binary classifier for this use case.

4. **One-class anomaly detection trained on clean-only images (declined).**
   Sidesteps needing many labeled *dirty* examples, which are inherently
   rarer — train only on the easy, abundant "clean" class and flag high
   reconstruction error. Declined for now: noisier in practice than
   supervised classification, and still needs the same local training
   pipeline as option 1 for a less proven technique on a first ML feature.

**Decision: option 1.** Matches the edge-first constraint the rest of this
codebase already operates under (ADR-020 declined even CPU-metric-level
host access for the Agent container; sending home photos to an external API
for routine inference would be a much bigger version of that same
trade-off), and keeps the failure mode local and inspectable rather than
dependent on a third party's model behavior. First concrete step: a
labeling workflow over the historical captures already sitting in Blob
Storage, before any training.

## ADR-033 — Sink-cleanliness classifier pipeline built: fetch/label/train scripts outside the .NET solution, ONNX inference wired into the Agent behind a config flag that defaults off

**What this covers.** The three-part pipeline ADR-032 decided on, actually
built. Nothing here is a new decision - it's the concrete shape of that
one, recorded because it touches event dispatch, `DeviceOptions`, and
`DeviceRuntimeState`, all of which `current-architecture.md` describes.

**`scripts/ml/sink-cleanliness/`** (Python, not part of the shipped
product, same footing as the throwaway heuristic spike before it):
`fetch_captures.py` lists Blob Storage by the exact prefix
`BlobNameGenerator` produces (`{tenant}/{site}/{agent}/{camera}/...`) and
downloads to `raw/`; `label_tool.py` is a keyboard-driven
clean/dirty/skip sort into `dataset/{clean,dirty}/`, resumable, no server;
`train.py` fine-tunes `torchvision`'s `mobilenet_v3_small` and
`torch.onnx.export`s to a `.onnx` file. `raw/`/`dataset/` are gitignored
(real home photos); the exported `.onnx` is not - it's a build input the
Agent actually loads, no artifact registry exists to fetch it from
otherwise.

**Agent side - deliberately not built as a monolithic new capability, but
as the smallest addition to what already exists:**
- `SinkCleanlinessOptions` (`Vivnest.Core.Options`) opt-in on
  `DeviceOptions.SinkCleanliness`, null for every camera that doesn't set
  it - same shape the original heuristic's options class already had.
- `DeviceEventTypes.SinkCleanliness` re-added (removed by the revert).
- `DeviceRuntimeState.LastSinkClean` (`bool?`) re-added for the same
  reason it existed before: restart-safe transition tracking, null only
  on this process's first observation for the device, exactly the
  pattern `MotionSensorMonitorWorker` already established for its own
  restart bug.
- `SinkCleanlinessClassifier` (`Microsoft.ML.OnnxRuntime` 1.28.0,
  `SkiaSharp` 4.150.1 - first ML runtime dependency in this codebase, and
  the first time `Vivnest.Agent` itself takes a SkiaSharp dependency
  rather than only `Vivnest.Cloud`) - caches one `InferenceSession` per
  configured `ModelPath`, and a missing or corrupt model file logs an
  error and disables classification for that camera rather than crashing
  the Agent process; every other capability keeps running regardless.
  Preprocessing (224x224, ImageNet mean/std normalization) has to stay in
  lock-step with `train.py`'s transforms - noted in-line in both files
  since a mismatch there would fail silently, not loudly.
- `SinkCleanlinessHandler` - a second `IEventHandler<CameraCaptureCompletedEvent>`
  alongside `CameraCaptureHandler` (multicast dispatch already supports
  N handlers per event). Persists+queues a `DeviceEvent` on *any*
  clean/dirty transition (dashboard history shows both directions); the
  Cloud-side handler is what actually decides whether to alert.
- `Vivnest.Agent.csproj` gained a wildcard `<None Include="Models\**\*.onnx">`
  item (`CopyToOutputDirectory: PreserveNewest`) instead of a fixed
  filename or a Dockerfile `COPY` step - the model doesn't exist in this
  repo yet, and a zero-match glob is a safe no-op rather than a build
  break, unlike referencing a specific file that isn't there.

**Cloud side** - `DeviceEventQueueHandler` gained a `SinkCleanliness`
case matching its own file's existing convention (`JsonDocument.Parse` +
`GetProperty`, not the typed-deserialize pattern `CameraCapturedHandler`
uses in a different file) - notifies with the attached photo only on the
transition to NotClean, staying silent on the return to Clean, per the
product decision already made last time this was attempted. Also gained
its own mute switch, `SinkCleanlinessNotificationOptions.Enabled`
(config section `SinkCleanlinessNotification`, default true) - same
shape as `SnapshotNotificationOptions.Enabled` for capture photos:
independent of `Telegram.Enabled` (which mutes every alert type) and of
`DeviceOptions.SinkCleanliness.Enabled` (which controls whether the Agent
classifies at all), so a household annoyed by repeat "needs cleaning"
pings can turn just this one off while classification and dashboard
history keep running.

**Config**: `camera-001`'s local dev device-config file
(`Vivnest.Agent/{agentId}.json`, gitignored) got a `SinkCleanliness`
block with `Enabled: false` and the same ROI
(650,150,1300,650) the original heuristic validated for this camera's
mounting position - reused as a starting point, not re-validated for the
classifier. Stays disabled until a real trained model exists; the real
host's remote config blob needs the same block added separately when
this actually ships, same manual-config-sync caveat every prior
camera-side change has carried.

**Not done, deliberately**: no training has run, no model exists, no
photo has ever been labeled. `dotnet build` is clean across the full
solution and that's the extent of what's been verified - live
classification needs a real trained `.onnx` file and a real capture to
prove out, and I didn't bulk-download real home photos or kick off
training without being asked to do that specifically, separate from
building the pipeline that makes it possible.

**Follow-up fix, 2026-08-07: `SkiaApi`'s type initializer threw in
production the first time a camera actually had `SinkCleanliness.Enabled:
true`.** `dotnet build` being clean never would have caught this -
`SkiaSharp` (the base package referenced above) ships only managed
bindings, no native library. `SinkCleanlinessClassifier.Classify` doesn't
touch a single SkiaSharp API until the first real (enabled) call, so
`SkiaApi`'s static constructor - which resolves `libSkiaSharp.so` - had
never actually run in this container until then. Fixed by adding
`SkiaSharp.NativeAssets.Linux.NoDependencies` (same `4.150.1` version, the
NativeAssets packages are versioned in lockstep with the base package) to
`Vivnest.Agent.csproj`. `.NoDependencies` specifically, not the regular
`SkiaSharp.NativeAssets.Linux` - the Agent Dockerfile's runtime base image
installs only `ffmpeg`/`tzdata`, not the `libfontconfig1`/`libGL1`-family
packages the regular Linux native asset needs; `.NoDependencies` is
statically linked against those and needs nothing extra from apt. No
Dockerfile change required - confirmed `dotnet publish` (no explicit `-r`)
places `runtimes/linux-x64/native/libSkiaSharp.so` in the output, and the
runtime host picks it automatically at startup.

**Follow-up fix, 2026-08-07: `SinkCleanlinessHandler` could misreport a
healthy camera as failed.** `EventDispatcher.PublishAsync` runs every
`IEventHandler<CameraCaptureCompletedEvent>` in sequence, catching each
handler's exception individually (so one handler failing never stops the
others), but then re-throws everything collected as a single
`AggregateException` once all handlers have run.
`SinkCleanlinessHandler`'s catch block (blob download failure, Table
write failure, queue publish failure - none of which involve the photo
itself) used to `throw;` after logging, same as `CameraCaptureHandler`'s
own catch. That propagated up through `CameraCaptureExecutor.CaptureAsync`'s
own catch, which overwrote the `runtime.LastError` it had just cleared to
`null` moments earlier (the capture itself had already succeeded) -
turning a transient hiccup in this opt-in side analysis into what looked
like the camera itself failing, on the dashboard's error banner. Fixed by
dropping the `throw;` - logs and returns instead, matching this ADR's own
stated intent ("every other capability keeps running regardless"), which
held for the other handlers but not, until now, for the capture's own
reported error state.

**Not yet fixed, flagged rather than silently left:** `runtime.LastSinkClean`
is updated before the transition `DeviceEvent`'s persist/publish step, which
can still fail (now silently, per the fix above). If it does, the missed
transition isn't retried on a later capture, since the in-memory state has
already moved on by then. Narrow window - only matters if the state
actually changed and only the *event write* fails, not the classification
itself - not fixed now.

## ADR-034 — Cross-process capability routing (AI inference living on a different agent): three designs by locality, only the same-process one built now

**The trigger:** discussing why `SinkCleanlinessHandler` runs synchronously
inside `CameraCaptureExecutor`'s dispatch (ADR-032/033 above), the
question came up: what if the AI capability itself lived on a *different*
agent than the one that captured the photo? roadmap.md's Phase 6B
("intra-site agent mesh") already names this scenario directly (item #3,
"Capability distribution": a camera agent without an AI accelerator
routes work to one that has one).

**Three designs, one per locality - not one design that has to cover
every case:**

1. **Same process** (today's actual reality - the sink-cleanliness
   classifier runs inside `Vivnest.Agent` itself, no separate agent
   involved at all). `System.Threading.Channels.Channel<T>` - an
   in-memory, in-process producer/consumer queue. `SinkCleanlinessHandler`
   enqueues a capture reference and returns immediately; a
   `SinkCleanlinessWorker` (`BackgroundService`, same shape as
   `AgentMetricsWorker` - own loop, own try/catch so a hiccup here can't
   touch anything else) drains the channel and does the download+classify+
   persist work off the capture path. New pattern for this codebase
   (nothing currently uses `System.Threading.Channels`), but the cheapest
   and most appropriate one for this locality - no network hop, no new
   infrastructure. **Built now - see below.**

2. **Different process, same host** (e.g. a separate "AI Agent" container
   on the same Raspberry Pi, per Phase 6B's illustrative multi-agent-per-
   site design). Would need real local IPC - a Unix domain socket, named
   pipe, or localhost gRPC/HTTP; a `Channel<T>` cannot cross a process
   boundary even on the same machine. Not built, not designed in detail -
   this codebase has no multi-process-per-host deployment today (always
   exactly one `Vivnest.Agent`), so building this now would be
   speculative ahead of a real second consumer, the same rule of thumb
   EVOLUTION-PLAN.md already applies elsewhere. Documented here so the
   option isn't lost, not because it's scheduled.

3. **Different host entirely** (a genuinely separate Raspberry Pi on the
   site's network, or cross-site per Phase 6A). Cloud-mediated, reusing
   the exact Cloud→Agent command-queue pattern already built for
   restart/deploy (ADR-024/028): the capturing agent uploads and publishes
   an event to Cloud as it already does, Cloud dispatches a command to the
   AI-capable agent ("classify this capture"), that agent reports the
   result back via its own event queue. Not Phase 6B's full peer-to-peer
   mesh (service discovery, network-transparent `EventDispatcher`,
   failover) - a materially smaller step that reuses plumbing that
   already exists, at the cost of a Cloud round-trip instead of a direct
   hop. Not built, not scheduled - documented for when a real second
   agent on a site actually exists.

**Decision:** build design 1 now (same-process `Channel<T>` +
`SinkCleanlinessWorker`), since it's the only one that matches a scenario
that's actually real today. Designs 2 and 3 are recorded here so the
reasoning isn't lost, not because either is scheduled - same treatment
Phase 6's other "illustrative designs, not commitments" already get in
roadmap.md.

**Follow-up, 2026-08-07: every classification now produces a dashboard
event, not just transitions - `Changed` added to the payload so Cloud's
notification logic doesn't regress.** By direct request, the dashboard's
Events feed should show a `SinkCleanliness` reading the same way
`CameraCaptured` shows every capture - `CameraCaptureInferenced` was
briefly discussed as a distinct in-process event for this, but declined:
this is a single linear next step (persist + queue), not multiple
independent capabilities reacting to the same fact, so a new
`IEventHandler<T>` type here would be exactly the kind of one-consumer
abstraction this codebase avoids extracting speculatively.

`SinkCleanlinessWorker.ProcessAsync` no longer gates on `changed` before
building the `DeviceEvent` - every classification is persisted and
queued. `runtime.LastSinkClean` is still tracked (restart-safe, same as
before), but now only to compute a `Changed: bool` carried in the event's
`Data` payload, not to decide whether the event fires at all.

This has a real downstream consequence, caught before it shipped:
`DeviceEventQueueHandler.HandleSinkCleanlinessAsync` (Cloud) had no
throttling of its own - it relied entirely on the Agent only ever queuing
on a real transition to keep "alert once per dirty streak" true. Once the
Agent queues every classification, an unchanged `Clean: false` reading
would have re-alerted on every single capture cycle a sink stayed dirty.
Fixed by having that handler check the new `Changed` field and skip
alerting when it's `false`, alongside the existing `Clean` check -
preserves the original "alert only on the transition to NotClean" product
decision (ADR-032) while no longer depending on the Agent to enforce it.

Dashboard label wording (`eventDescriptions.ts`) also branches on
`Changed`: a real transition still reads as an action ("Sink cleaned" /
"Sink needs cleaning"), a repeat reading reads as a state ("Sink clean" /
"Sink still dirty") - showing "Sink cleaned" on every one of many
identical steady-state readings would have been actively misleading.

**Follow-up, 2026-08-07: object detection added, gating classification on
person presence and flagging unusual objects - explicitly not person
identification.** By direct request: a person actively at the sink makes
a clean/dirty read unfair (they're mid-use, not done), and the same
detection pass can flag objects that don't normally belong on that
counter. Identifying *which* person was explicitly declined - only
presence and timing, no face recognition, no enrollment.

**Model:** `Models/object-detection/yolov8n.onnx` - the standard
Ultralytics YOLOv8n export (`yolo export model=yolov8n.pt format=onnx`),
COCO-pretrained (80 classes including `person`, `cup`, `bowl`, `bottle`,
`knife`, `spoon`, `fork`, `sink`, `microwave`, etc.) - no custom training
needed, unlike the sink classifier. Sourced from Ultralytics' own export
tooling directly, not a third-party pre-exported file, and AGPL-3.0
licensed - noted in `Models/README.md` since that's a real consideration
beyond personal use.

**`ObjectDetector`** (`Vivnest.Agent/Capabilities/Camera`) decodes this
exact export shape - `[1, 84, N]` (4 box coords + 80 class scores per
anchor, no separate objectness score, YOLOv8 dropped it), full greedy
per-class NMS (IoU > 0.45 suppressed), boxes scaled back from the 640x640
model input to the original capture's resolution via a straight
non-letterboxed stretch (same simplification `SinkCleanlinessClassifier`
already makes for its own resize). This is not a generic ONNX
object-detection decoder - a different YOLO version, different export
flags (e.g. baked-in NMS), or a non-COCO/custom-trained model would need
different decode math here, same "preprocessing must stay in lock-step or
fail silently wrong" risk ADR-033 already flagged for the sink model.

**Where it plugs in:** one detection pass per capture, run inside
`SinkCleanlinessWorker.ProcessAsync` (not a separate opt-in capability -
its whole purpose is refining the existing feature's accuracy), feeding
two independent checks against `ObjectDetectionOptions`'s own ROI (kept
separate from `SinkCleanlinessOptions`'s ROI even though they're
typically the same region for a camera - either capability can be
enabled without the other):
- A `person` detection whose box center falls inside the ROI sets
  `DeviceRuntimeState.LastPersonSeenUtc` and skips classification for
  that capture entirely - no `SinkCleanliness` event at all for a
  person-present frame.
- Every other detection inside the ROI not in
  `ObjectDetectionOptions.ExpectedClasses` (a configured allowlist, not a
  learned baseline - same reasoning ADR-032 already used to reject
  one-class anomaly detection: predictable and inspectable beats a model
  that can silently drift) becomes its own `UnusualObjectDetected`
  `DeviceEvent`, listing every unusual class found in that capture in one
  event rather than one per object. Runs regardless of whether a person
  is also present in the same frame - unlike classification, an unusual
  object is still worth flagging mid-use.
- `SinkCleanliness` events now also carry `LastPersonSeenUtc` in their
  payload (timing only, per the "no identification" decision above) -
  the #3 "attribution" idea from the original ask, narrowed to "someone
  was here recently" rather than "who."

`SinkCleanlinessWorker` gained a shared `PersistAndQueueAsync` helper
(entity save + queue publish) once a second event type needed the exact
same two steps `SinkCleanliness` already did - the "second real consumer"
threshold this codebase already applies before extracting anything.

**Follow-up, 2026-08-07: `UnusualObjectDetected` restructured into
`ObjectsDetected` - fires on every capture, carries every box, feeds a
new "show detections" toggle on the dashboard's main photo.** By direct
request: (1) draw boxes over the *main* photo (not the gallery strip)
for detected objects/persons, behind an on/off toggle; (2) show whether
`SinkCleanliness`/`ObjectDetection` are enabled as their own tiles on
Device Detail; (3) the sink-cleanliness thumbs-up/down badge, previously
gallery-thumbnail-only, on the main photo too; (4) an `ObjectsDetected`
event every time detection runs, not just when something unusual turns
up. (1) exposed a real gap: `UnusualObjectDetected` only ever stored
class names for the *unusual* subset, with no box coordinates at all - it
couldn't have fed an overlay even for the objects it did know about.

**Agent side** - `DeviceEventTypes.UnusualObjectDetected` renamed to
`ObjectsDetected` (fires unconditionally per capture ObjectDetection
runs on, same "every classification, not just the interesting case"
cadence `SinkCleanliness` already established). `SinkCleanlinessWorker.PersistObjectDetectionEventAsync`
now emits every ROI-contained detection (person included), each carrying
its box (`X1,Y1,X2,Y2`) and a per-object `Unusual` flag, plus
`PersonPresent`/`HasUnusualObjects` summary flags - `Severity` is
`Warning` when `HasUnusualObjects`, `Information` otherwise, which for
free gives the dashboard's already-existing severity-based badge coloring
(`EventsFeed.tsx`) the right color with no new logic there.

**Capability-enabled tiles** - `DeviceOptions.SinkCleanliness?.Enabled`/
`ObjectDetection?.Enabled` denormalized onto `DeviceHeartbeat` (new
`SinkCleanlinessEnabled`/`ObjectDetectionEnabled` bools), same
Brand/Model/Firmware-style pattern, *not* ADR-030's fresh-query pattern -
config, not a live reading, and any config change already needs an Agent
restart to take effect, which `DeviceHeartbeatWorker` already republishes
on unconditionally (ADR-005) - no staleness window exists here the way
one did for thumbnails.

**Cloud side** - `DeviceEventDto` gained `DetectedObjects: DetectedObjectDto[]?`
(`ClassName, Confidence, X1, Y1, X2, Y2, Unusual`), joined the same way
`SinkCleanlinessResult` already is: `GetDeviceCapturesByDayAsync` fetches
the day's `ObjectsDetected` events once, keyed by `OccurredAtUtc`, and
attaches the matching capture's full detection list. `DeviceSummaryDto`
gained `SinkCleanlinessEnabled`/`ObjectDetectionEnabled`, mapped straight
through from the heartbeat entity.

**Dashboard side** - `DeviceDetail`'s live-feed photo (not
`CaptureGallery`'s thumbnail strip - kept deliberately separate per the
request) gained: a "Show/Hide detections" toggle rendering an SVG
overlay of every `DetectedObjects` box (person = warning-amber, unusual =
danger-red, everything else = accent-blue); the sink-cleanliness
thumbs-up/down badge already on gallery thumbnails, now here too; two new
metric-grid tiles ("Sink check"/"Object detection", camera-only, colored
by on/off). The SVG overlay's `viewBox` is set to the image's own
`naturalWidth`/`naturalHeight` with the default `xMidYMid meet` fitting -
that's the SVG equivalent of the `<img>`'s own `object-fit: contain`, so
boxes land correctly without any manual scale-factor math even when the
capture's aspect ratio doesn't match the 16:9 container and the image
gets letterboxed.

**Not done / still true from ADR-034's original follow-up:** none of this
has run against a real capture yet - genuinely untested, same caution
already given for the underlying classifier and detector.

**Follow-up, 2026-08-07: found live, not theoretically - `ObjectDetector`
was starving motion-triggered bursts.** Once `ObjectDetection.Enabled`
was actually flipped on for a real camera, motion-triggered captures
stopped continuing their burst cadence (`CaptureOnTriggerHandler`'s
`BurstUntilUtc`/`BurstInterval`, `CameraCaptureWorker.cs`) after the
first shot - not a crash, and confirmed via `git diff` that no code in
the motion-sensor/trigger/capture-scheduling path had changed at all.

**Root cause:** `CameraCaptureWorker.RunCaptureLoopAsync` awaits
`CameraCaptureExecutor.CaptureAsync` before it can compute the next
delay and loop back - a hard sequential dependency. `SinkCleanlinessWorker`
runs off that path via the `Channel<T>` (that's the whole point of design
1), so it doesn't block the capture loop *directly* - but it does compete
with it for the same CPU and .NET thread-pool threads, and
`InferenceSession.Run()` is a synchronous, blocking native call that
doesn't yield a thread while it works. `ObjectDetector.ToTensor`'s
original preprocessing made this materially worse: a 640x640 nested loop
calling `SKBitmap.GetPixel()` per pixel - 409,600 individual native
interop round-trips - before inference even started. On a Raspberry Pi
with no hardware acceleration, that's enough wall-clock time pinning a
thread that the capture loop's 30-second burst ticks started arriving
late enough that by the time they landed, `BurstUntilUtc` (10 minutes
from the original trigger) had already passed - `inBurst` silently
evaluates `false`, and the loop falls back to the normal 15-minute
schedule with no error, no log line, nothing to point at.

**Fixed:** `ToTensor` now reads the whole pixel buffer in one bulk copy
(`SKBitmap.Bytes`) instead of one `GetPixel()` call per pixel. Requires
the bitmap to actually be in a known format to index into correctly - the
`resized` bitmap is now explicitly constructed as `SKColorType.Rgba8888`
(previously the platform default, which varies) rather than relying on
whatever `GetPixel()`'s internal format-translation happened to produce.

**Immediate mitigation, separate from the fix:** `ObjectDetection.Enabled`
flipped back to `false` in the local dev config first, to confirm the
diagnosis by restoring normal burst behavior before trusting the fix
alone - not re-enabled as part of this change; that's a separate,
deliberate step once the fix itself is verified.

**Also fixed, same change:** `SinkCleanlinessClassifier.ToTensor` had the
identical per-pixel `GetPixel()` pattern (224x224 - smaller than
ObjectDetector's 640x640, but a real cost every capture already pays
regardless of whether ObjectDetection is even enabled). Same fix, same
reasoning - bulk `Bytes` read against an explicitly `Rgba8888` `resized`
bitmap.

**Follow-up, 2026-08-07: analysis skipped for every burst capture except
the last.** The preprocessing fix above cuts the cost *per* capture, but
doesn't touch the underlying rate problem: a burst fires captures every
`BurstInterval` (e.g. 30s) instead of the normal `Schedule.Interval`
(e.g. 15min), so a 10-minute burst was asking for ~20 full
classify+detect passes in the time a normal schedule asks for about one -
and motion mid-event isn't a fair "is this clean" read anyway.

Considered threading a "this is the last burst capture" flag through
`CameraCaptureResult`/`CameraCaptureCompletedEvent` from
`CameraCaptureWorker`, where the real burst-continuation decision is
made. Went with something smaller instead: `SinkCleanlinessHandler`
already reads `DeviceRuntimeState` config via `IDeviceRuntimeStore` for
the `Enabled` checks - it now also reads `ICaptureStatusStore` for the
same `BurstUntilUtc`/`BurstInterval` fields `CameraCaptureWorker` itself
uses, and predicts locally: if `now + BurstInterval >= BurstUntilUtc`,
this capture is (likely) the burst's last one, so it's allowed through;
otherwise it's skipped before ever reaching the channel. "Predicted," not
exact - this runs before `CameraCaptureWorker`'s own next-tick check, not
after, so it's inferring what that check will find a beat later. Worth
being clear-eyed about: this is an approximation against a mutable
runtime value read from a different task than the one that owns it, not
a guaranteed-exact synchronization point - fine given the cost of being
off by one capture at a burst boundary is negligible, wrong for anything
where it wouldn't be.

**Extended, same day: the burst's *first* capture gets analyzed too, not
just the last.** Still only 2 analyses per burst instead of ~20 - a
negligible addition on top of the fix above, nowhere near reintroducing
the starvation problem. Worth doing because the two captures serve
different purposes: the first is taken right when motion fired, making it
the capture most likely to actually catch someone at the sink - exactly
what the person-gate needs to set `LastPersonSeenUtc` promptly, rather
than only learning someone was there once the burst is already ending;
the last stays the fairest "is this clean now" read, once the activity's
likely concluded.

`TriggerReason`/`BurstReason` can't distinguish first from Nth burst
capture on their own - it's the same string for every tick in a burst.
`DeviceRuntimeState` gained `LastAnalyzedBurstUntilUtc`, using the
burst's own `BurstUntilUtc` value as an identity token (a new burst
always gets a new, later one from `CaptureOnTriggerHandler`): "first
capture of this burst" is just "haven't recorded this exact
`BurstUntilUtc` as analyzed yet." Self-cleaning by construction - no
explicit reset needed between bursts, since the token itself changes
every time.

## ADR-035 — AI inference moved to a dedicated second agent (ADR-034's design 3, built for real)

**Why now:** running both ONNX models in-process (design 1) pushed the
capture agent's RAM from ~150MB to ~450MB - real pressure on the 2GB
Raspberry Pi it shares with camera capture, RTSP/ffmpeg, and motion-sensor
polling. A second Raspberry Pi 5 (8GB) became available specifically to
take this load. ADR-034 already described this as "design 3" but
deliberately didn't build it - there was no second real device to justify
it. There is now.

**One reused project, not a new one.** Both roles are the same
`Vivnest.Agent` binary/Docker image - the user's own framing going in was
"another agent with only the AI-capabilities registered in the DI of that
agent," confirmed to be the right shape once checked against the code:
the remote per-agent config blob mechanism (`agent-config/{agentId}.json`,
already how every device Pi gets distinct config) meant a second agent
needs nothing beyond its own `AgentId` and config blob - no Dockerfile or
deploy-script change at all.

**New `AgentOptions.Role`** (`AgentRole.Capture`/`.Ai`, `Enums/AgentRole.cs`),
defaulting to `Capture` - the existing agent's config never needs an
`Agent:Role` key added. `Program.cs` reads it raw off `IConfiguration`
right after the existing remote/local config-layering block, the same
idiom already used for the `Agent:AgentId` bootstrap read, and gates
capability-specific DI registrations into three groups: shared
(heartbeats, metrics, log shipping, restart-command polling -
unconditional), Capture-only (camera, motion sensor, smart plug,
HomeAssistant, `SinkCleanlinessHandler`), Ai-only
(`ISinkCleanlinessClassifier`, `IObjectDetector`, `SinkCleanlinessWorker`).
An Ai-role agent's config simply configures zero `Devices` -
`DeviceHeartbeatWorker` and every other device-iterating worker already
no-op safely on an empty list, confirmed before relying on it rather than
assumed.

**Cross-process hand-off** composes the existing Cloud→Agent command
pattern (`RestartCommandQueueMessage`/`AgentCommandPublisher`/
`CommandPollingWorker`, ADR-024) with a new Agent→Cloud publish leg:

```
SinkCleanlinessHandler (Capture agent)
  --publish--> "classify-requests" queue
  --> ClassifyRequestFunction (Cloud Functions, pure relay - no storage hop)
  --publish--> "agent-classify-commands" queue
  --> SinkCleanlinessWorker (Ai agent, now polls instead of draining a channel)
```

`SinkCleanlinessHandler`'s decision logic (device lookup, `Enabled`
check, burst-throttle via `ICaptureStatusStore`) is unchanged - it's
still the only place with the in-process burst state that decision needs.
Only its last step changed: instead of `Channel<T>.TryWrite`, it builds a
`ClassifyCaptureQueueMessage` and publishes it to
`MessagingOptions.ClassifyRequestQueue`, wrapped in try/catch - unlike
`TryWrite`, a queue publish is a real network call that can throw, and
this handler must still never let an AI-pipeline hiccup surface as a
capture failure (same reasoning ADR-033's follow-up already established
for this class).

**`ClassifyCaptureQueueMessage`** (`Vivnest.Core/Queues/Models`) is a
direct-payload message, not a `{PartitionKey, RowKey}` pointer -
deliberately the same exception to ADR-004's "pointer only" rule that
`RestartCommandQueueMessage` already established: there's no persisted
row to reference, the message *is* the payload, and it's well under
Azure Queue's size limit either way. It flows unchanged through both
hops - `ClassifyRequestFunction`/`ClassifyRequestHandler` on the Cloud
side is a **pure relay** (deserialize → `PublishClassifyCommandAsync` →
done), a new shape for this codebase: every other Cloud Function handler
re-fetches a table row (`CameraCapturedHandler`), this one has nothing to
fetch.

**Identity fix - the one real correctness issue.** `SinkCleanlinessWorker`
used to stamp `DeviceEvent.AgentId/TenantId/SiteId` from its own
`AgentOptions` - correct only because the same process both captured and
classified. Once classification runs on a different agent, it must stamp
the *capturing* agent's identity instead, or every resulting
`SinkCleanliness`/`ObjectsDetected` event would misattribute itself to
the Ai-agent. `ClassifyCaptureQueueMessage` carries `AgentId` (addressee -
the Ai-agent, matching `RestartCommandQueueMessage`'s existing
addressee-naming convention) plus `OriginAgentId`/`OriginTenantId`/
`OriginSiteId` (the capturing agent's identity). Applied in **two
places** in `SinkCleanlinessWorker.cs` - `ProcessAsync` and
`PersistObjectDetectionEventAsync` build near-identical `DeviceEvent`
objects, easy to fix one and miss the other.

**Routing:** which Ai-agent a capture agent forwards to is a new
`AgentOptions.AiAgentId` field on the *capturing* agent's own config -
one Ai-agent per site today, not looked up dynamically. No second
consumer exists yet to justify anything more general.

**`SinkCleanlinessWorker` stays one class**, not split into a poller and
a processor. It already combined "wait for the next unit of work" with
"process it" before this change (channel `await foreach` + `ProcessAsync`) -
only the trigger changed (channel → a `CommandPollingWorker`-shaped queue
poll), not its overall shape. Splitting it would have been exactly the
speculative extraction this codebase's guiding principle says to defer
until a second real consumer needs it.

**Conscious tradeoffs, stated plainly rather than discovered later:**
- *Latency.* Capture → classification goes from today's near-instant
  in-process hand-off to a worst-case mid-tens-of-seconds delay (Cloud
  Functions' queue-trigger polling backoff, plus
  `SinkCleanlinessWorker`'s own poll interval on the Ai-agent side). Fine
  against a 15-minute capture cadence; `ClassifyCommandQueue` gets its
  own 5s poll interval, shorter than `CommandPollingWorker`'s 15s, since
  unlike a rare manual restart this carries automatic, routine traffic.
- *Delete-before-process.* Losing a classify-command message means one
  capture's classification silently never happens. Sounds like a new
  risk, isn't one: `SinkCleanlinessWorker.ExecuteAsync`'s per-item
  try/catch already never retried a failed `ProcessAsync`, even when
  this ran off an in-process channel. This relocates that existing
  "no retry" contract, it doesn't weaken it.
- *A previously dead code path goes live.* `CommandPollingWorker`'s
  discard-if-not-addressed-to-me branch on the restart queue was
  "defensive, not exercised" with a single agent. It's shared,
  unconditionally, by both roles now - a Capture agent and an Ai agent
  polling the same `agent-restart-commands` queue each discard the
  other's restart commands via this filter, no code change needed, but
  worth knowing it's load-bearing for the first time.

**Follow-up, same day: `SinkCleanlinessOptions`/`ObjectDetectionOptions`
split, by direct request - a camera device shouldn't own AI-agent
behavior it doesn't execute.** The first cut of design 3 kept the *full*
per-camera config (ROI, `ModelPath`, `ConfidenceThreshold`,
`ExpectedClasses`) on `DeviceOptions.SinkCleanliness`/`.ObjectDetection`
and forwarded the whole thing over the classify-request message
unchanged - simplest to build, but conceptually wrong: a camera doesn't
classify anything, so it shouldn't be the place model behavior is
configured.

**What actually stays camera-specific:** `Enabled` and the ROI - pixel
coordinates only mean anything relative to this exact camera's own
framing/mounting, which is a genuine fact about the device, not about the
Ai-agent. New `SinkCleanlinessRoiOptions`/`ObjectDetectionRoiOptions`
(`Vivnest.Core/Options`) replace the full types on `DeviceOptions`.

**What moved to the Ai-agent:** `ModelPath`, `ConfidenceThreshold`, and
(for object detection) `ExpectedClasses` - how the classifier/detector
itself behaves, independent of which camera it's analyzing. New
`SinkCleanlinessModelOptions`/`ObjectDetectionModelOptions`, looked up by
`DeviceId` from a new `AiClassificationOptions` (section
`"AiClassification"`, a flat `List<AiDeviceClassification>` since the
Ai-agent has no `DevicesOptions` of its own to hang this off).

**The classifier/detector interfaces didn't change.**
`ISinkCleanlinessClassifier.Classify`/`IObjectDetector.Detect` still take
the full `SinkCleanlinessOptions`/`ObjectDetectionOptions` shape - nothing
configures that shape directly anymore, `SinkCleanlinessWorker.ProcessAsync`
assembles it at classify time by merging the message's Roi options with
the locally-looked-up Model options. Kept this way deliberately: reshaping
two interfaces that already work correctly, just to relocate where their
inputs come from, would have been unnecessary blast radius for a config
question.

**`ClassifyCaptureQueueMessage`** now carries `SinkCleanlinessRoiOptions
SinkCleanlinessRoi`/`ObjectDetectionRoiOptions? ObjectDetectionRoi`
instead of the full option types (renamed from `Options`/`ObjectDetection`
for clarity now that the fields mean something narrower). A smaller,
more honestly-scoped message than before.

**Consequence worth knowing, not a hidden gap:** if a device's `Enabled`
flag on the camera side and its entry (or lack of one) in the Ai-agent's
`AiClassification.Devices` fall out of sync - e.g. a camera enables
`SinkCleanliness` but the Ai-agent has no matching `DeviceId` entry -
`SinkCleanlinessWorker` logs a warning and skips that capability for that
capture rather than crashing the poll loop. Two config files now have to
agree for a device to actually get classified, where one previously
sufficed; this is the direct cost of the split, accepted deliberately in
exchange for the AI-agent owning its own behavior.

**Follow-up, same day: burst throttling removed from `SinkCleanlinessHandler`
- the problem it solved no longer exists once classification moved off
this process.** By direct request, questioning why the burst-throttle
logic (documented at length under ADR-034's follow-ups above - only the
first and last capture of a burst get analyzed) still earned its keep now
that the Ai-agent split is built. It doesn't, and removing it was the
right call, not just a simplification for its own sake:

The throttle's *entire justification*, from the original entries above,
was protecting **this same process's** own thread pool - CPU-bound ONNX
inference was starving `CameraCaptureWorker`'s ability to keep its own
burst-continuation ticks on schedule. Once classification runs on
dedicated hardware (an entirely different agent, different process,
different machine), that coupling is structurally gone: nothing this
process does for AI purposes can compete with its own capture loop
anymore, because it no longer does anything AI-related beyond publishing
a small queue message. The premise the throttle existed to protect
against can't happen here anymore, regardless of how much or little the
Ai-agent has to process.

What the Ai-agent gets instead of custom "which captures matter" logic:
its own queue, which serializes work naturally - a busy burst just means
its 20 captures get worked through one after another over the next
minute or so, with zero risk to anything else on that box (it has no
competing responsiveness requirement of its own to protect, unlike the
capture agent's own loop). The cost moved from "a real correctness bug"
to "a few extra `SinkCleanliness`/`ObjectsDetected` `DeviceEvent` rows
and a short processing delay for the tail of a busy burst" - a trade
worth making for deleting custom logic that no longer has a job to do.

**Removed:** the `BurstUntilUtc`/`BurstInterval`/`LastAnalyzedBurstUntilUtc`
check in `SinkCleanlinessHandler.HandleAsync` - every capture now
publishes a classify request unconditionally once `SinkCleanliness` is
enabled, burst or not. `ICaptureStatusStore` is no longer a dependency of
this class at all (it had no other use here). `DeviceRuntimeState.LastAnalyzedBurstUntilUtc`
deleted - it was written and read exclusively by the code just removed.
`BurstUntilUtc`/`BurstInterval`/`BurstReason` stay - those are
`CameraCaptureWorker`'s own burst-*capture*-cadence fields (how often to
take a photo during a burst), a completely separate concern from whether
a given photo gets analyzed, and nothing about this change touches that.

**Follow-up, same day: dashboard "Pending AI" badge - a direct
consequence of the latency this ADR already named, not a new decision.**
Design 1's in-process classification made a capture's result available
essentially immediately; design 3's Cloud-mediated round-trip means a
capture can now genuinely exist on the dashboard for a real stretch of
time (the "mid-tens-of-seconds worst case" already documented above)
before its `SinkCleanliness`/`ObjectsDetected` result lands. Previously,
"no result" was indistinguishable from "not applicable" - both just
showed no badge. That's no longer honest once the gap is real and
visible.

No backend/API changes - everything needed already exists client-side:
`DeviceSummary.sinkCleanlinessEnabled`/`objectDetectionEnabled` (which
capabilities apply to this device) and each capture's own
`occurredAtUtc` (how long ago it happened). New `isAiPending` helper
(`CaptureGallery.tsx`, exported like `isTriggeredCapture` already was)
computes it purely from data already on the page - true when a relevant
capability is enabled, no result has arrived yet, and the capture is
younger than a 2-minute timeout (generous against the documented
worst-case latency above).

**"No result yet" isn't one signal, it's two, and the wrong one hangs
forever.** `SinkCleanliness` alone never fires at all for a person-gated
capture (ADR-034's follow-up) - waiting on it as the "done" marker would
show Pending indefinitely for every capture where the sink-classifier
was correctly skipped. `ObjectsDetected` fires on every capture
`ObjectDetection` runs on regardless of outcome, so when
`ObjectDetection` is enabled, its presence is the reliable "the Ai-agent
got to this one" signal instead; `SinkCleanliness` presence is only
used as the fallback when `ObjectDetection` isn't enabled on that camera
at all.

New `BotIcon` (`icons.tsx`), shown in the same bottom-right badge slot
`CaptureGallery`'s sink-result badge already uses (gallery thumbnails)
and a new bottom-right slot on `DeviceDetail`'s main photo (the one
corner the timestamp/trigger/sink badges don't already occupy) - the two
never render together by construction, since Pending means no result
exists yet.

**Follow-up, same day: `Vivnest.Agent.Updater`'s `ContainerName` made
configurable - the "real second agent" its own comment named as the
trigger for this has arrived.** By direct request: both agent roles
deployed on the *same* Docker host (a high-spec laptop, replacing what
was going to be a second Raspberry Pi) rather than two separate physical
hosts. Nothing about the Cloud-mediated design cares - `classify-requests`/
`agent-classify-commands` work identically whether the two containers are
on different machines or the same one, so this needed zero changes to
any of the actual classification flow.

What it does need: `DeployPollingWorker.cs` had `ContainerName` as a
hardcoded `"vivnest-agent"` constant, with a comment already anticipating
this exact moment ("a real second agent/image would be the trigger to
make these configurable"). Two Updater instances on one host, both
hardcoded to the same container name, would have collided - both
`docker run --name vivnest-agent`, fighting over the same container
regardless of which one a deploy command was actually addressed to.
Moved to `DeployOptions.ContainerName` (default `"vivnest-agent"`, so
today's single-agent hosts need no config change). `Image` stays a
constant - both roles share the exact same image, that was never going
to differ.

Running two agents on one host now means two Updater *instances* (two
processes, two folders, two `updater.settings.json`s, two
`Agent:AgentId`s), not one instance handling both - `DeployPollingWorker`
already filters incoming deploy commands by `AgentId` (same pattern
`CommandPollingWorker` uses for restart), so each instance only reacts to
commands addressed to its own agent; it just needed its own container
name to act on to be safe about it. `scripts/update-agent.ps1` (the
manual equivalent this mirrors) got the same treatment -
`-ContainerName`/`-AppSettingsPath` parameters, defaulting to today's
values, so the same script serves both agents by argument instead of by
hand-editing a shared file per deployment.

**Follow-up, same day: `Vivnest.Agent.Updater` gained a `--install` CLI
flag, closing a real bootstrap gap the queue-driven deploy path can't
cover.** Checked before building, not assumed: `AgentsFunction.cs`'s
`DeployAgent` endpoint (what the dashboard's Deploy button calls) does a
tenant-scoped `GetAgentAsync` existence check and returns 404 *before*
ever publishing a deploy command - meaning it only works for an agent
Cloud already has a heartbeat from. A genuinely new agent (the Ai-role
container about to exist for the first time) can't be reached this way -
chicken-and-egg, since it can't send a heartbeat before it's ever run
once.

Extracted the actual `docker pull`/`stop`/`rm`/`run` sequence out of
`DeployPollingWorker` into a new `AgentDeployer` class - the second real
caller (`--install`, alongside the existing queue-triggered path) is
exactly the threshold this codebase already uses before extracting
anything, not a speculative abstraction. `stop`/`rm` were already
`allowFailure: true` (tolerating "nothing running yet"), so the same
method needed zero changes to work as a first-time install, not just a
routine update - the queue path and the CLI path were always doing
literally the same operation, just triggered differently.

`Program.cs` checks for `--install` in `args` after the host builds:
resolves `AgentDeployer` from DI, runs one deploy immediately, then falls
through to `app.RunAsync()` as normal - one process handles bootstrap
*and* ongoing updates, rather than needing the manual script for the
first run and the Updater for every run after. No `AgentId` filtering
needed for `--install`, unlike a queue message - running the flag
locally on a host already implies "this Updater instance's own agent,"
there's no shared-queue ambiguity to resolve.

**Follow-up, same day: `--agent`/`--container`/`--connectionstring` CLI
flags, so `updater.settings.json` never needs hand-editing at all.** By
direct request - one command line to fully stand up either agent
instance, not "run `--install`, then go edit a JSON file, then run it
again." `ApplySettingsOverridesFromArgs` (`Program.cs`) patches (or, for
a from-scratch host, creates with the same defaults as the checked-in
template) `Agent:AgentId`/`Deploy:ContainerName`/`Messaging:ConnectionString`
via `System.Text.Json.Nodes`, before `Host.CreateApplicationBuilder`
reads the file - so the same run picks up the values immediately, not
just future ones. Every other setting (`PollInterval`,
`DeployCommandQueue`, `Logging`) is left exactly as found when the file
already exists - a patch, not an overwrite.

Verified against the actual checked-in commented template before
trusting it, not just compiled: parsing needed `JsonDocumentOptions`
with `CommentHandling = Skip`/`AllowTrailingCommas = true` explicitly -
`JsonNode.Parse`'s defaults don't tolerate comments the way
`Microsoft.Extensions.Configuration.Json` does internally, so reading
the template without this would throw on its own comments. Confirmed by
actually running the built exe against a copy of the real template with
only `--agent` passed: the untouched fields (`ContainerName` never set,
`ConnectionString` still `""`) came through exactly as expected, and the
freshly-written file's values were live in that same process (it failed
cleanly on the still-empty `ConnectionString`, not a JSON error).

**Trade-off, stated plainly:** `JsonNode`/`JsonObject` have no concept of
comments - writing the file back out strips whatever comments were
there, including the checked-in template's uncomment-one-line guidance.
Accepted deliberately: the entire point of these flags is not needing
that guidance once you're using them.

**Follow-up, next day: real bug found live - `AgentHeartbeatWorker`
(shared, both roles) crashed on startup for every Ai-role agent, DI
couldn't resolve `IHomeAssistantConnectionTracker`.** The first real
Ai-agent install exposed this immediately: `AgentHeartbeatWorker` is
registered unconditionally (both roles need to report liveness), but its
constructor depends on `IHomeAssistantConnectionTracker`, which was
still registered inside the Capture-only block from before the role
split - a leftover from `AgentHeartbeatWorker` predating ADR-035
entirely, never re-examined for its actual dependency graph once
registrations got triaged into shared/Capture/Ai buckets.

Same audit found a second instance of the identical mistake:
`AgentMetricsWorker` (also shared) depends on `INetworkUsageTracker`,
also still Capture-only.

**Fix, not a workaround:** both `HomeAssistantConnectionTracker` and
`NetworkUsageTracker` are trivial, dependency-free state holders (a
locked nullable `DateTime`; an `Interlocked`-backed counter) - nothing
about them requires the Capture role specifically, only the *callers*
that update them (`HomeAssistantWorker`, `CameraCaptureService`) are
Capture-only. Moved both registrations to the shared block. On an
Ai-role agent nothing ever calls `MarkConnected()` or
`AddBytesUploaded()`, so `LastConnectedUtc` stays `null` and
`TakeBytesUploaded()` reports `0` - correct, honest readings for an
agent with no Home Assistant integration and no photo uploads, not a
missing feature.

**Verified for real, not just compiled** - the exact discipline this
codebase has held to throughout ADR-032 through ADR-035: published the
Agent locally, ran it with `Agent:Role=Ai` and a throwaway
non-production `AgentId` against the real dev storage account, and
confirmed clean startup with no DI exception -
`AgentHeartbeatWorker`/`AgentMetricsWorker`/`DeviceHeartbeatWorker`/
`CommandPollingWorker`/`SinkCleanlinessWorker` (polling
`agent-classify-commands`, as expected for the Ai role) all initialized
and a real heartbeat was successfully persisted and published.

**Lesson for next time a shared component is added or a new one moves
between role buckets:** check its *full* constructor dependency graph
against what's actually registered unconditionally, not just whether
the component itself is in the shared block - a shared component with
even one role-scoped dependency fails for every agent of the excluded
role, every single startup, not intermittently.

## ADR-036 — Per-capability AI routing, and Devices[] split into per-device blobs

**Why now:** `AgentOptions.AiAgentId` (ADR-035) is a single field on the
whole capture agent, so every AI capability on every camera routes to one
Ai-agent. The user wants to eventually run a dedicated Ai-agent per
capability (one for sink-cleanliness, a different one for object
detection) - today's model can't express that. Separately, `Devices[]`
lives embedded in the capture agent's single config blob with no
authoring tooling (hand-edited JSON, uploaded via `az storage blob
upload`) - the literal root cause of several real bugs this session (two
blobs, kept in sync only by matching `DeviceId` strings by hand). Both
fixed together, in a freshly stood-up environment (`rg-vivnest-2`) with
zero devices configured yet - the cheapest possible window to change the
config shape, before there's any data to migrate.

### Part 1: `ExecutingAgentId` per capability, replacing `AgentOptions.AiAgentId`

`AgentOptions.AiAgentId` is deleted. `SinkCleanlinessRoiOptions` and
`ObjectDetectionRoiOptions` each gain their own `ExecutingAgentId` -
SinkCleanliness and ObjectDetection on the same camera can now route to
different Ai-agents, or the same one, independently.

**One combined message becomes two independent ones.**
`ClassifyCaptureQueueMessage` gains a `ClassifyCapability Capability`
field (`SinkCleanliness`/`ObjectDetection`, new `Enums/ClassifyCapability.cs`);
`SinkCleanlinessRoi` becomes nullable, since only the Roi field matching
`Capability` is ever populated. `SinkCleanlinessHandler` now publishes up
to two messages per capture instead of one - one per enabled capability
with a non-empty `ExecutingAgentId`, each in its own try/catch, so one
capability's publish failure can't block the other. `ClassifyRequestHandler`,
`ClassifyRequestFunction`, `AgentCommandPublisher` needed zero changes -
all three are generic relays over this message type, not aware of what's
inside it.

**Accepted behavior change: the person-detection gate is removed, not
rebuilt.** `SinkCleanlinessWorker` used to run both capabilities in one
call, and used ObjectDetection's person-in-frame result to skip
SinkCleanliness classification for that capture (ADR-034's follow-up,
avoiding misclassifying while someone's actively at the sink). Splitting
into independent per-capability messages breaks this - and not only when
the two capabilities route to different Ai-agents. Even routed to the same
agent, two independently-queued messages have no ordering guarantee
(Azure Storage Queues aren't FIFO), so the same-process, same-call
coupling that made the gate reliable is gone regardless of routing. Rebuilding it
properly would need a cross-agent-readable persisted signal (e.g.
`SinkCleanlinessWorker` querying the latest `ObjectsDetected` `DeviceEvent`
for this device within a recent window before classifying) - a new read
dependency, a time-window heuristic, and possible races, for a capability
that's still opt-in and camera-specific. Confirmed with the user rather
than assumed: dropped cleanly instead. `runtime.LastPersonSeenUtc` still
gets recorded whenever ObjectDetection runs, purely informational now -
it will simply stay `null` forever on an Ai-agent that never receives
ObjectDetection messages for a given device.

`SinkCleanlinessWorker.ProcessAsync` is now a dispatcher on
`item.Capability`, calling one of two extracted methods
(`ProcessObjectDetectionAsync`/`ProcessSinkCleanlinessAsync`) instead of
always running both in sequence. Everything else in that class
(`PersistObjectDetectionEventAsync`, `IsWithinRoi`, `PersistAndQueueAsync`,
`HandleMessageAsync`'s existing addressee filter, `ExecuteAsync`) is
unchanged.

### Part 2: `Devices[]` split into one blob per device

New `device-config` blob container, one blob per device
(`DeviceConfigBlob.cs`, mirrors `AgentConfigBlob.cs`'s convention
exactly). `DeviceOptions` gains `CaptureAgentId` - which Capture-role
agent owns/loads this device (the physical agent holding the device
connection). Deliberately distinct from the AI-capability
`ExecutingAgentId`s: one identifies who talks to the hardware, the other
identifies who executes a classification - different kinds of ownership
that happen to both be "an AgentId on a config object."

**Loading, at Capture-agent startup only** (Ai-role agents never consumed
`Devices` at all, so this is skipped for them entirely): a new
`TryLoadRemoteDeviceConfigsAsync` (`Program.cs`, mirrors
`TryLoadRemoteConfigAsync`'s shape and error handling) lists every blob in
`device-config` (new `AzureBlobStorageClient.ListBlobNamesAsync`, no
listing capability existed before this), downloads and parses each, keeps
only the ones whose `CaptureAgentId` matches this agent's own `AgentId`,
and assembles the survivors into a `{ "Devices": [...] }` JSON document
inserted via the same `JsonStreamConfigurationSource` +
`InsertConfigSourceBeforeEnvVars` plumbing already used for the
agent-config blob. `Configure<DevicesOptions>(builder.Configuration)`
needed zero changes - it still binds the same root `"Devices"` key, so
every existing consumer (all eight go through `IDeviceRuntimeStore`,
never `IOptions<DevicesOptions>` directly) needed zero changes either.
There's no server-side filter on the listing call - every agent downloads
every device blob and discards what isn't theirs, fine at this project's
scale. Same "additive, never required" convention as the agent-config
blob: a missing container means zero devices, not a startup failure, and
one bad/unreachable device blob is skipped with a warning rather than
aborting the rest.

**Deployment rule, not enforced in code:** once this ships, no
`agent-config/{agentId}.json` blob may re-introduce a `"Devices"` key -
.NET's configuration system merges JSON arrays by index across providers,
not full replacement, so a stale array on one source could partially leak
through underneath the device-config source's own array. Not a live risk
today (confirmed no current blob has `Devices` data), but a rule worth
stating before it becomes one.

**Explicitly out of scope:** `DeviceOptions`'s existing typed-field shape
(`Settings`/`Schedule`/`Trigger`/`SinkCleanliness`/`ObjectDetection`) is
untouched beyond the additive fields above - no generic capability
dictionary, no dynamic capability registry/discovery service. There are
still only two agents total; a static `ExecutingAgentId`/`CaptureAgentId`
field on config satisfies the two-Ai-agent scenario that motivated this
without a new service, new storage, or new failure mode - revisit if a
real capability-routing need ever outgrows a static field, not before.
No authoring tooling was built either; the real devices get hand-authored
as individual blobs post-implementation, same as agent-config is
hand-uploaded today.

## ADR-037 — Config values duplicated identically across both agent-config blobs extracted into a shared-config blob

**Why:** `Tables`, `AgentHeartbeat`, `DeviceHeartbeat`, `DeviceEvents`,
`AgentEvents`, `AgentMetrics`, and part of `Messaging` (`Transport`,
`ConnectionString`, `AgentHeartbeatQueue`, `DeviceHeartbeatQueue`,
`DeviceEventQueue`, `RestartCommandQueue`) were byte-for-byte identical in
both per-agent blobs, kept in sync only by hand-editing two files - the
same root-cause class ADR-036 already named for `Devices[]`, now confirmed
to have caused two real bugs this session directly (a blob missing
`AgentHeartbeat`/`DeviceHeartbeat`, separately missing
`AgentMetrics`/`DeviceEvents`/`AgentEvents`).

**New `shared-config/common-config.json` blob** (`SharedConfigBlob.cs`, mirrors
`AgentConfigBlob`/`DeviceConfigBlob`'s convention, but `BlobName` is a
fixed `const string` since there's no per-entity ID - every agent,
regardless of role, loads the exact same single blob). Loaded by both
roles via `TryLoadRemoteSharedConfigAsync`, called *before*
`TryLoadRemoteConfigAsync` so the per-agent blob keeps higher precedence
(nothing needs to override a shared value today, but the mechanism
supports it for free via `InsertConfigSourceBeforeEnvVars`'s existing
call-order-determines-precedence behavior).

**`Messaging` ended up fully consolidated into shared config, not split.**
The first cut kept each agent's role-specific queue names
(`CameraCapturedQueue`/`ClassifyRequestQueue` on Capture,
`ClassifyCommandQueue` on Ai) on their own per-agent blob, since those
values genuinely aren't identical across roles. By direct request, moved
those into `shared-config` too, on the same reasoning
`AiClassificationOptions` was already bound unconditionally on both roles
for (ADR-035's comment: "harmless... since nothing on a Capture-role
agent reads it") - a Capture-role agent never reads `ClassifyCommandQueue`
and an Ai-role agent never reads `CameraCapturedQueue`/`ClassifyRequestQueue`,
so having every queue name present on every agent is harmless, and it
means never having to remember which blob needs which queue key. Both
per-agent blobs now have no `Messaging` section at all. `Tables` needed no
equivalent change - every field was already fully shared from the start,
nothing per-agent to consolidate. By the same follow-up requests,
`Storage.BlobContainer` (no bootstrap constraint, unlike `ConnectionString`
- confirmed DI-only) and `Logging` (redundant with local
`appsettings.json`, not protecting anything the way `Storage.ConnectionString`
was) also moved to shared/local-only respectively. What's left per-agent:
just `HomeAssistant` on Capture, `AiClassification` on Ai - genuinely
role-specific data, not unsynced duplication.

**Local dev gets a parallel `TryLoadLocalSharedConfig` + local
`common-config.json` draft file, and this is required, not optional.** Checked
against the real `Options` classes before deciding: `TablesOptions`'s four
table-name fields default to `""`, and
`AgentHeartbeatOptions`/`AgentMetricsOptions`/etc.'s `Enabled` defaults to
`false`. Trimming the local per-agent draft files the same way the remote
blobs get trimmed, without a local shared-config fallback, would silently
disable heartbeats/metrics/events in local dev - the identical failure
shape this ADR exists to fix, just relocated from the remote blobs to
local dev. Not the same situation as `device-config` (ADR-036), which
has no local-loading path: a missing/empty device list degrades to zero
devices, a legitimate local-dev state; a missing shared config degrades to
disabled heartbeats, which isn't.

**`Storage.ConnectionString` cleanup finding.** Confirmed via its two raw
pre-DI reads (`TryLoadRemoteConfigAsync`, `TryLoadRemoteDeviceConfigsAsync`
- both construct a `BlobServiceClient` from it before any remote blob can
be fetched) that it can never legitimately live in *any* remote or local
per-agent blob - it's needed to reach Blob Storage in the first place, so
it must come from local `appsettings.json`/env vars only. The Capture
blob's copy was already dead weight (redundant with the local value) and
worse, misleading - editing it there had zero effect. Removed from both
the remote Capture blob and its local draft. `Storage.BlobContainer`
stayed (genuinely DI-consumed via `IOptions<StorageOptions>`, no such
constraint, Capture-specific - the Ai-agent gets `BlobContainer` off the
incoming queue message instead, never its own config).

**Real bug found and fixed during implementation, not just planning:**
`ConfigurationManager` (unlike a plain `ConfigurationBuilder`) eagerly
rebuilds every registered source into a fresh provider on each `Sources`
mutation, disposing the providers being replaced. For an ordinary
`MemoryStream`-backed `JsonStreamConfigurationSource`, this meant the
*second* successful stream-based insert corrupted the *first* one -
observed directly in local-dev testing: chaining a shared-config insert
then a per-agent insert threw `"Stream was not readable."` on the second
one. This was a latent bug in the pre-existing `TryLoadRemoteConfigAsync`/
`TryLoadRemoteDeviceConfigsAsync` pattern from ADR-025/ADR-036 too - it
simply never surfaced before, since no prior code path chained two
*successful* stream inserts before `Build()`. Fixed with a
`ReusableMemoryStream : MemoryStream` (`Program.cs`, private nested class)
that resets its position instead of actually closing on `Dispose`, so a
stream survives being rebuilt any number of times before `Build()`
finally settles. Applied at all five `JsonStreamConfigurationSource`
construction sites (shared, per-agent, device-config), not just the two
new ones - the fix belongs to the shared helper's contract, not to any one
caller.

**Deployment rule, not enforced in code:** any future per-agent-blob key
that's genuinely meant to be identical across every agent belongs in
`shared-config`, not copy-pasted into each per-agent blob again.

## ADR-038 — Sensitive fields split out of local config drafts into local-only secrets files

**Why:** the local draft files (`common-config.json`, the Capture agent's
`{agentId}.json`, the device-config files) were entirely gitignored, so
90% of their content — queue/table names, heartbeat toggles, camera
host/username, HA base URL, ROI thresholds — had zero git history, even
though almost none of it is actually sensitive. Only a small set of leaf
fields are real secrets: `Messaging.ConnectionString` (shared),
`HomeAssistant.Password`/`AccessToken` (Capture agent), and
`Settings.Password`/`RtspPassword` on 3 of the 4 devices. Splitting those
into sibling `*.secrets.json` files - always local-only, never uploaded to
Azure, never committed - lets everything else become normal tracked
source. Per direct decision: per-scope secrets files (mirroring the
existing shared/agent-config/device-config split from ADR-036/037), not
one consolidated file.

**Naming convention:** `{public-file-stem}.secrets.json`, next to its
public sibling in all three scopes (`common-config.secrets.json`,
`{agentId}.secrets.json`, `device-config/{deviceId}.secrets.json`). Not
`common-secrets.json` - that name doesn't end in the literal substring
`.secrets.json`, so it would have silently missed the gitignore pattern
below and risked committing a live storage account key. No new
`Vivnest.Core/Constants` class - those exist so Cloud and Agent agree on
*blob* names; secrets never touch a blob, nothing else needs to know these
filenames, so they stay inline literals in `Program.cs`.

**Local-only-regardless-of-`LoadLocalSettings` principle.** Extends the
precedent `Storage:ConnectionString` already set (ADR-037's cleanup
finding: it must come from local `appsettings.json`/env vars alone, since
it's needed to reach anything remote). `TryLoadLocalSharedSecrets` and
`TryLoadLocalAgentSecrets` run unconditionally, right after the existing
`LoadLocalSettings` if/else block, so even a remote-config deployment
still sources credentials from that machine's own filesystem, never from
Azure Blob Storage or git. No key overlap with the public files, so
precedence relative to that block doesn't matter functionally.

**Per-device JSON-merge design, and why array cross-provider merging
couldn't be used.** `TryLoadRemoteDeviceConfigsAsync` deliberately
assembles the `Devices` array in code, not via layered config sources,
specifically to avoid .NET's array-merge-by-index footgun already
documented in ADR-036. A separately-inserted config source can supply
`HomeAssistant:Password` (an object-typed path) but can't target
"`Settings.Password` on the third element of `Devices`" (an array-typed
path) - so device secrets can't be "one more inserted source" the way
shared/agent secrets can. Instead, a small recursive `MergeJsonInto`
helper merges a device's local secrets object into its already-downloaded
public `JsonObject` before it's added to the array, inside the existing
per-device loop, right after the `CaptureAgentId` ownership filter (so
file I/O only happens for devices this agent actually owns). Recurses into
nested objects that exist on both sides, so `{"Settings":{"Password":...}}`
adds `Password` without clobbering the public object's existing
`Settings.Host`/`Username`. Values are `.DeepClone()`d - a `JsonNode` can
only be attached to one parent at a time, so assigning a value already
attached to the secrets object directly would throw at runtime. Missing
secrets file: log and continue with whatever `Settings` the public blob
already had - same "additive, never required" convention as everything
else in this file (the smart plug has no `Password` field at all and
needs no secrets sibling).

**Deployment implication:** the public device blobs are always
remote-fetched (no local-loading path exists for devices, unchanged from
ADR-036), but device secrets are loaded unconditionally local - so a real
deployed instance needs its `*.secrets.json` files placed on disk by hand,
through some channel that is neither git nor Azure Blob Storage.

**Verified for real, not just compiled:** ran both agents locally and
against real Azure both before and after re-uploading the trimmed public
blobs, confirming the full `[Startup] Loaded local/remote shared
config...` → `Loaded local shared secrets...` → `Loaded local
config/secrets for agent...` → `Merged local secrets for device...` log
sequence each time, the smart plug's correct "no secrets file found...
continuing without them" degrade, the Ai agent's correct "no secrets file
found for agent..." degrade (it has none today) followed by a real
heartbeat persisting, and - the strongest proof - a real photo actually
captured and uploaded from the physical camera, confirming
`Settings.Password`/`RtspPassword` resolve correctly now that they exist
*only* in `device-config/{deviceId}.secrets.json`.

**Follow-up, same day: every sensitive key gets an empty-string placeholder
back in its public file, by direct request.** Omitting a secret key
entirely from the public file (the original cut) meant nothing in the
git-tracked source hinted that `ConnectionString`/`Password`/`AccessToken`
existed as real fields at all - someone reading `common-config.json` cold
would have no way to know a `Messaging.ConnectionString` was expected
without already knowing about the `*.secrets.json` convention. Every
trimmed field now stays in its public file with `""` as the value, so the
key itself documents "this exists, it's just supplied elsewhere." Safe by
construction, not just by convention: `TryLoadLocalSharedSecrets`/
`TryLoadLocalAgentSecrets` always insert after the public config in the
precedence chain (both branches), and `MergeJsonInto`'s per-device merge
unconditionally overwrites `target[key]` with `source[key]` regardless of
what the target already held - so the real secret value always wins over
the `""` placeholder, whether the key came from a separately-inserted
config source or an in-code merge. Re-verified for real after this change
too, both locally and against re-uploaded Azure blobs: the same camera
capture/upload proof point still succeeds, confirming the placeholder
never survives past the secrets layer.

## ADR-039 — Updater self-authenticates to ACR instead of depending on a prior `az acr login`

**Why:** the dashboard's "Deploy" button looks fully automated
(`AgentsFunction.DeployAgent` → `agent-deploy-commands` queue →
`DeployPollingWorker`/`AgentDeployer`), but `AgentDeployer.DeployAsync`'s
`docker pull` silently depended on the host machine already being
authenticated to `vivnestagentacr.azurecr.io` via a prior manual
`az acr login`. That session is tied to the Azure CLI's own token
lifetime - after the host sits untouched for a day, the token's expired
and nobody's there to re-run it, so a queue-triggered deploy just fails
with no human present to fix it. Confirmed via direct code reading before
building anything: no `docker login`/`az acr login` call existed anywhere
in `AgentDeployer.cs`, `DeployPollingWorker.cs`, or `Program.cs` - `pull`
was the very first docker invocation in the whole flow, assuming auth had
already happened.

**Why an ACR repository-scoped token, not a symmetric-key-encrypted blob
or Azure Key Vault** (both discussed and rejected in chat before this ADR
was written): both alternatives just relocate the same bootstrap problem
rather than removing it - a shared symmetric key or a Key Vault service
principal still needs to land on a new host through some manual, secure
channel, exactly like the credential it would replace. An ACR
repository-scoped token, limited to `vivnest-agent` pull-only
(`az acr token create --repository vivnest-agent content/read` +
`az acr token credential generate`), is simplest for this project's
actual scale: no Azure AD app registration or RBAC assignment, doesn't
expire on its own (unlike an Azure CLI session), and is scoped narrower
than the ACR admin user (single repository, read-only) without the setup
overhead of a full service principal.

**`DeployOptions` gains `AcrUsername`/`AcrPassword`**, both defaulting
empty - additive, not required, so an instance that hasn't set these yet
falls back to exactly today's behavior (confirmed by the negative-check
verification below). `AgentDeployer.DeployAsync` runs a new login step
first, only when both are set, immediately before the existing `pull`.

**Security-critical implementation detail:** the password must never
reach `RunDockerAsync`'s existing log line (`"Running: docker {Arguments}"`,
which joins and logs the raw argument list) or the process's command-line
arguments at all (visible via `ps`/Task Manager on some hosts). Used
Docker's own `--password-stdin` flag instead of `--password <value>` - a
new `RunDockerLoginAsync` method (not built on `RunDockerAsync`, since it
needs `RedirectStandardInput`) keeps the password out of `ArgumentList`
entirely (only `login`, the registry, `--username`, and the literal flag
name `--password-stdin` are there - zero secret material, safe to log
verbatim) and writes it to the process's stdin pipe instead. Also split
the previously-single `Image` const into `Registry` + `Image`
(`Image = $"{Registry}/vivnest-agent:latest"`), so login and pull/run
share one source of truth for the hostname rather than duplicating it.

**`Program.cs`'s `ApplySettingsOverridesFromArgs`** gets two more flags,
`--acrusername`/`--acrpassword`, matching the exact existing
`--agent`/`--container`/`--connectionstring` pattern - fits the user's own
stated intent for this mechanism ("extended to any update") from when it
was first built (ADR-035's follow-up).

**Verified for real, not just compiled** - and found a real hazard doing
it: `docker logout vivnestagentacr.azurecr.io` first, to genuinely
simulate the reported failure state (confirmed a plain `docker pull`
failed with "authentication required" immediately after, proving the
failure mode is real, not theoretical). Ran `--install` with the real ACR
token via the new CLI flags: `docker login` ran and succeeded, `pull`
succeeded immediately after with zero manual `az acr login` in between,
full deploy completed, and grepping the captured output for the literal
password string found zero matches. Negative check: logged out again, ran
`--install` with no ACR credentials configured, confirmed no `docker
login` line appeared at all and `pull` failed with the identical
"authentication required" error as the very first check - proving the new
code path is skipped cleanly, not just harmlessly present, when
unconfigured.

The hazard: `--acrusername`/`--acrpassword` patch the same **checked-in,
git-tracked** `updater.settings.json` template that
`--agent`/`--container`/`--connectionstring` already patch - running the
verification commands directly against this repo's working copy wrote the
real ACR token password into a tracked file in plaintext. Caught before
anything was committed and the file was restored to its clean
empty-placeholder template immediately after each test run. Worth stating
plainly for next time: **verifying any CLI flag that patches
`updater.settings.json` must restore the file afterward**, the same
discipline this ADR's own testing needed twice.

## ADR-040 — Read-only Cloud API for the dashboard's Capabilities tab, plus a `Sensors` schema

**Why:** ADR-036 through ADR-038 built the real backend data model a
"Capabilities" tab UI mockup needs (`DeviceOptions` with `CaptureAgentId`
and per-capability `ExecutingAgentId`, `Trigger.DeviceIds`, etc.), but
nothing on the Cloud side could read any of it - the dashboard's REST API
only reads Table Storage (heartbeats, events); `device-config`/
`agent-config`/`shared-config` blobs were readable only by `Vivnest.Agent`
itself. This ADR adds the missing read path: `GET
/devices/{deviceId}/capabilities`.

**Read-only by decision, not by omission** - matches this project's own
history: a GET/PUT config editor was built once, went unused, and was
explicitly removed (ADR-025). Write support is out of scope here and
should only be revisited once the read-only view is actually in use.

**Tenant-scoping via `CaptureAgentId`, not a Table Storage device
lookup** - a deliberate improvement over the pattern every other query
service in this codebase uses (`IDeviceQueryService.GetDeviceAsync`),
not a copy of it. That pattern requires the device to have already sent
a heartbeat before it's considered to exist; a freshly-configured device
blob is completely real before its first heartbeat ever lands. Instead,
`DeviceCapabilitiesQueryService` downloads the device blob first, then
proves tenant ownership via `IAgentQueryService.GetAgentAsync(tenant,
device.CaptureAgentId)` - if that agent doesn't belong to the caller's
tenant (or doesn't exist), 404. The blob download happening before the
ownership check is safe: only the *response* is gated on it, nothing is
ever returned to a wrong-tenant caller.

**`UsedByCount` on each source sensor is computed, not stored** - no
generic capability-to-sensor graph exists or was built. There's exactly
one real relationship in this codebase today (`ObjectDetection`/
`SinkCleanliness` both consume the `Camera` sensor when enabled), so
`DeviceCapabilitiesQueryService.ComputeUsedByCount` hardcodes that one
rule directly rather than modeling a graph for a relationship with a
single instance - consistent with this project's own "second real
consumer" principle. Revisit with a real model only if a second sensor
ever gets a real consumer.

**New `SensorOptions`/`DeviceOptions.Sensors`** - the mockup's "Source
sensors" section (hardware Present/Accessible/Used-by state) had no
backing schema at all; added by direct decision rather than deferred.
Presence is implied by list membership (no separate `Present` flag) -
a device's `Sensors` array only contains hardware that's actually there.
`Accessible`/`InaccessibleReason` are hand-authored per-device facts, not
derived from `DeviceType` - a camera's PIR being blocked is a firmware
fact about *that specific device*, not true of every camera of its type.
Default empty list keeps every existing device-config blob
backward-compatible with zero edits (confirmed below).

**Reused existing `Vivnest.Core.Options` types for deserialization**
(`DeviceOptions`, `AiClassificationOptions`) instead of hand-rolling
`JsonNode` parsing - the Agent already binds these same blobs into these
same types via `Microsoft.Extensions.Configuration`.

**Real bug found during verification, not just "compiles successfully":**
the very first deploy returned a 404 for a device blob confirmed (via
direct `az storage blob show` and other already-working endpoints) to
genuinely exist and be fully readable. Root cause: `Microsoft.Extensions.
Configuration`'s binder (what the Agent uses) parses string enum values
like `"Type": "Camera"` natively, but raw `System.Text.Json.JsonSerializer
.Deserialize<T>` does not, by default - it expects an enum's *numeric*
value unless a `JsonStringEnumConverter` is registered. `DeviceOptions.
Type` deserializing this way threw a `JsonException`, which
`TryLoadDeviceAsync`'s catch block correctly (by design) swallowed and
turned into a clean 404 - masking the real cause as "not found." Fixed by
adding a shared `static readonly JsonSerializerOptions` with a
`JsonStringEnumConverter`, passed to both `JsonSerializer.Deserialize
<DeviceOptions>` call sites in `DeviceCapabilitiesQueryService`. Worth
stating as a standing rule: **any future Cloud-side code that
deserializes an `Options` type directly via `JsonSerializer` (rather than
through config binding) must use this same enum-aware options instance,
or risk the identical silent-404 failure mode.**

**Verified for real** against the already-deployed `vivnestcloud2` app
and real device-config blobs from this session's earlier work, not
synthetic fixtures:
- The camera device (`f7756a79-...`) returns all 4 capabilities
  (`Camera`/`SinkCleanliness`/`ObjectDetection`/`DeviceHeartbeat`) with
  real ROI values and `ExecutingAgentId`; `derivedFrom` shows both
  capabilities' real model paths and confidence thresholds pulled from
  the Ai-agent's own blob; `triggeredBy` correctly lists the real motion
  sensor device whose `Trigger.DeviceIds` names this camera;
  `sourceSensors` shows the 3 hand-authored entries with `MotionSensor`
  marked inaccessible (with its reason) and `Camera`'s `usedByCount`
  correctly computed as 3.
- Negative check: an unknown `deviceId` returns 404 with an empty body -
  no data leak.
- Backward-compat check: the smart plug and motion sensor devices (no
  `Sensors` ever authored) return 200 with `sourceSensors`/`derivedFrom`/
  `triggeredBy` all empty and just the `DeviceHeartbeat` capability - the
  additive default degrades cleanly, not as an error.
- Auth check: a request with no `x-api-key` returns 401, matching every
  other endpoint in this API.

## ADR-041 — Capability and Service split into separate concepts, not a naming layer

**Why:** `BuildCapabilities` (ADR-040) only ever emitted a Built-in entry
for `DeviceType.Camera` - a MotionSensor or SmartPlug device's Capabilities
tab showed nothing but `DeviceHeartbeat`, silently omitting the device's
own actual primary function. Raised directly while reviewing the
Capabilities tab: "isn't motion sensing a capability?" - it is, it just
had no entry. Fixing that led to a deeper, correct objection: the first
pass at showing "which service provides this capability" just appended
the technical name as extra text on the same row (`SinkCleanliness ·
ROI ... · Enabled`) - still a rigid one-to-one row, not an actual
distinction. **"Capability is different and the device is different. A
capability can be achieved by one or more devices and/or services."**
That's the model this ADR actually builds.

**`CapabilityDto` now carries a `Services: IReadOnlyList<CapabilityServiceDto>`**
instead of embedding one service's fields directly
(`Vivnest.Cloud/Api/Dtos/DeviceCapabilitiesDto.cs`). A capability's `Name`
is a fixed, canonical concept - `Image Capture`, `Image Classification`,
`Image Analysis`, `Motion Detection`, `Power Monitoring`, `Health
Monitoring` - genuinely independent of which concrete device/service
provides it. Every capability in this codebase happens to have exactly
one service today, but the shape doesn't assume that: `Services` is a
list because the relationship is naturally one-to-many, not because two
services exist yet. `CapabilityServiceDto` carries what `CapabilityDto`
used to (`Enabled`, ROI, `Host`/`Username`, `ExecutingAgentId`,
`LivenessInterval`/`WarningMultiplier`) plus `ModelPath`/
`ConfidenceThreshold` - absorbing the standalone `DerivedFromDto` (and
`DeviceCapabilitiesDto.DerivedFrom`) entirely, since a service's model
details are just more facts about that service, not a separate
relationship needing its own list.

**One Built-in capability per native device type**, same treatment for
all: `Image Capture` (Camera) existed already; `Motion Detection`
(MotionSensor) and `Power Monitoring` (SmartPlug) are new, each with one
service (`Camera`/`MotionSensing`/`PowerMonitoring` respectively) carrying
`Host`/`Username` from `DeviceOptions.Settings`. **Built-in, not
Derived** - direct hardware readings, no AI model or executing agent
involved, the same distinction that already separated `Image Capture`
from `Image Classification`/`Image Analysis` (Derived, routed through an
Ai-agent's ONNX model). The test for this boundary: *does producing this
capability's value involve shipping data to an agent that runs a model
over it?* If yes, Derived; if it's a direct read of the device's own
sensor, Built-in - regardless of how much on-device signal processing
produces that reading. `Health Monitoring`/`DeviceHeartbeat` (System)
applies unconditionally to every device, same as before.

**Dashboard rendering nests one more level** (`CapabilitiesTab.tsx`):
Capabilities section → `.subsection-heading` per `Source` (Built-in/
Derived/System, unchanged from the first pass) → `.capability-heading`
per capability name → one `.entity-row` per service in that capability's
`Services` list, each with its own Enabled/Disabled pill (a service's
enabled state is its own, not borrowed from the capability - correct now
that more than one could exist). `describeService` replaces the old
`describeCapability`, operating on a `CapabilityService` directly - no
more frontend name-translation table (`CAPABILITY_DISPLAY_NAMES` from the
first pass is gone entirely) and no more cross-referencing a separate
`derivedFrom` array, since `ModelPath`/`ConfidenceThreshold` now live
directly on the service that produces them.

**Verified for real** against `vivnestcloud2` and the live dashboard
(`polite-beach-0e6006e00.7.azurestaticapps.net`, not just local dev): the
camera device's `GET .../capabilities` response nests `Image
Classification` → `Services: [{Name: "SinkCleanliness", ModelPath:
"Models/sink-cleanliness.onnx", ...}]`; the motion sensor's response now
shows `Motion Detection` → `Services: [{Name: "MotionSensing", Host:
"192.168.50.170", ...}]` instead of an empty Capabilities section. In the
running dashboard: BUILT-IN/DERIVED/SYSTEM group headers, each capability
name as its own sub-heading, each service as its own row with its own
Enabled pill and details (ROI, model path, confidence, agent id) - the
capability and the service that provides it are now genuinely two
different things on screen, not one row wearing two labels.

## ADR-042 — Capability master list gets real CRUD, behind a new Admin drawer

**Why:** ADR-041 fixed `Capability`'s *shape* (a canonical concept with a
one-to-many `Services` list) but that data was still hand-derived per
request from device/agent config blobs - there was no actual list of
"the capabilities that exist" anywhere, and no way to manage one. Raised
directly: "would it be better now if we can create a table in table
storage for masterlist for devices? maybe we should start creating the
master list and then give a crud like management of these lists in the
app." Capability was picked as the first master list to build for real
(Service/Device/DeviceType/Automation deliberately deferred - see
EVOLUTION-PLAN.md's "second real consumer" rule) because it's the
simplest: a flat `{CapabilityId, CapabilityName, CapabilityType}` with no
relationships to manage yet.

**New, deliberately global entity** - `CapabilityEntity`
(`Vivnest.Core/DataStores/Entities/CapabilityEntity.cs`) implements
`ITableEntity` directly rather than extending `BaseEntity`: a Capability
("Image Capture", "Motion Detection", ...) is a fixed concept shared
across every tenant, not tenant-owned data, so `TenantId`/`SiteId` fields
would be actively wrong here. `PartitionKey` is a constant
(`CapabilityEntity.PartitionKeyValue = "capability"`), `RowKey` is the
capability's own `Guid` - no separate id field, and "list everything"
is a single cheap partition-scoped query rather than the unpartitioned
scan `ApiKeyEntity`'s hash-partition scheme forces for its own listing
endpoint (a mistake this entity deliberately doesn't repeat).
`CapabilityType` (`BuiltIn`/`Derived`/`System`, the existing enum from
ADR-040/041) travels as a plain string on the wire and in storage,
`Enum.TryParse`-validated server-side - the same convention already
established for `DeviceHeartbeatEntity.Source`, now applied consistently
rather than reaching for a global `JsonStringEnumConverter`.

**New `Vivnest.Cloud.Admin` namespace** houses
`ICapabilityManagementService`/`CapabilityManagementService` - separate
from the existing query-only `Vivnest.Cloud` services (`DeviceQueryService`
et al.) because this is the first genuinely mutating master-data service
in the codebase, not a device/agent query. `AzureTableStore<T>` gained a
`DeleteAsync(partitionKey, rowKey, ct)` method (wrapping
`TableClient.DeleteEntityAsync` with `ETag.All`, 404-swallowing) since
nothing in the codebase had ever deleted a table row before this.
`CapabilityAdminDto`/`CreateCapabilityRequest`/`UpdateCapabilityRequest`
(`Vivnest.Cloud/Api/Dtos/CapabilityAdminDto.cs`) are deliberately named
apart from the pre-existing per-device `CapabilityDto`/
`CapabilityServiceDto` (ADR-040/041) - same English word, two unrelated
DTOs (one derived-per-request, one master-list CRUD), and the name
collision would otherwise be confusing.

**New `CapabilitiesAdminFunction`, routes `capabilities-admin` /
`capabilities-admin/{capabilityId}`** (GET/POST/PUT/DELETE) - not
`admin/capabilities` as first written: Azure Functions rejects any route
starting with `admin/` at startup ("The specified route conflicts with
one or more built in routes"), colliding with the platform's own admin
API. Every route under this feature (and ADR-043's Agent routes) avoids
the `admin/` prefix segment entirely because of this. Gated identically
to every other endpoint via `ApiFunctionBase` (`x-api-key` →
`TenantContext`, 401 if missing/invalid) plus the existing
`DevicesOnly` → 403 check for the three mutating routes - a capability
key still shouldn't be able to rewrite the master list.

**New dashboard Admin section**, entered via a hamburger button
(`MenuIcon`, top-left of the header) opening `AdminDrawer` - a slide-out
panel, `Capabilities` (this ADR) the first real item, `Services`/
`Devices`/`Automations` shown but disabled ("soon") since those master
lists don't exist yet. `CapabilitiesAdmin`/`CapabilityFormModal` are a
plain list-with-filter page and an Add/Edit form modal - hand-written,
not extracted into a shared "MasterListAdmin" component, since this is
the only real instance of the pattern so far (see ADR-043 for the second
instance and why it's still not extracted).

**Verified for real** against `stvivnestagent2`/`tblCapabilities` (new
table) and the deployed `vivnestcloud2` Function App + Static Web App at
the time this was built (before the "local-only until further notice"
instruction landed - see EVOLUTION-PLAN.md): full curl CRUD round-trip
(create → list → update → delete → re-list confirms gone), negative
checks (no key → 401, bad `CapabilityType` string → 400, unknown id on
PUT/DELETE → 404), and browser verification of the same flow through the
live Admin → Capabilities UI.

## ADR-043 — Agent registry: a second master list, pre-registration not live-entity fabrication

**Why:** Immediate follow-up ask after ADR-042: "can we add an Agent menu
item to the Admin, because I think we should be able to create/edit/
delete an Agents, same as Capabilities?" Unlike Capability, `Agent` is not
pure reference data - the existing `/agents`/`/agents/{id}` endpoints and
`tblAgentHeartbeat` are real operational state, populated only by an
actual running `Vivnest.Agent` process's heartbeats (ADR-005). Building
"Create" identically to Capability would mean fabricating a live
monitoring entity for a device that has never actually reported in -
raised directly before implementing anything. Clarified scope: "for now,
can we pick the same attributes for the agent from the appsettings?" -
i.e. the fields `AgentOptions` already defines (`Name`,
`FirmwareVersion`, `Role`), used for **pre-registration**: giving whoever
sets up the physical device an `AgentId` and identity to copy into that
machine's `appsettings.json`, not simulating a live agent. Confirmed via
follow-up question that this belongs in a **new, separate table**
(`tblAgentRegistry`), never `tblAgentHeartbeat` - the registry and the
live heartbeat table stay completely independent, and the existing
read-only `AgentList`/`AgentDetail` dashboard pages are untouched by this
feature.

**`AgentRegistryEntity` extends `BaseEntity`** (`TenantId`/`SiteId`
required) - the opposite choice from `CapabilityEntity` (ADR-042),
because a registered agent genuinely belongs to one tenant/site, matching
`ApiKeyEntity`/`DeviceHeartbeatEntity`'s convention for tenant-owned
data. `PartitionKey = "{TenantId}|{SiteId}"`, `RowKey = AgentId` - same
"list is a single partition-scoped query" discipline as ADR-042, applied
to the tenant-scoped case this time. `TenantId`/`SiteId` are filled from
the authenticated `TenantContext` server-side and never accepted from the
request body, so a tenant key can't register an entry under another
tenant. `Role` reuses the existing `AgentRole` enum (`Capture`/`Ai`)
as-is - no new enum needed, same string-on-the-wire /
`Enum.TryParse`-validated convention as `CapabilityType`.

**Structurally mirrors ADR-042 end to end**: `IAgentRegistryStore`/
`AzureTableAgentRegistryStore`, `Vivnest.Cloud.Admin.
IAgentRegistryManagementService`/`AgentRegistryManagementService`,
`AgentRegistryAdminFunction` at `agents-registry-admin` (not
`admin/agents` - same reserved-route lesson from ADR-042, applied
proactively this time rather than discovered by trial and error),
`AgentRegistryDto`/`CreateAgentRegistryRequest`/
`UpdateAgentRegistryRequest`, and dashboard-side `AgentRegistryAdmin`/
`AgentRegistryFormModal` components plus a second real `AdminDrawer`
item. Deliberately still two separate hand-written admin component pairs
rather than one shared "MasterListAdmin" abstraction - the fields
genuinely differ (Role dropdown vs. CapabilityType, tenant-scoping Agent
needs and Capability doesn't) and there have now been exactly two
instances of the pattern; per EVOLUTION-PLAN.md's "second real consumer"
rule, extraction is worth revisiting once a third master list (Service or
Device) confirms the duplication is real rather than coincidental.

**Verified for real, entirely local per the standing "no Azure deploy
until further instructed" instruction** (see EVOLUTION-PLAN.md): backend
built clean, `tblAgentRegistry` created in `stvivnestagent2`, full curl
CRUD round-trip against `func start` cross-checked directly with
`az storage entity query` (not just trusting the API's own responses),
negative checks (401/400/404) all passed. Dashboard build/lint/tsc all
clean; browser verification against the local dev server
(`http://localhost:5173` → `http://localhost:7071/api`) walked the entire
UI flow - Admin → Agents → Add (all 3 fields, Role defaulting to
`Capture`) → new row appears → Edit (Role correctly pre-selected as the
saved value, firmware version changed) → change survives a full page
reload (proving it's server-persisted, not local React state) → Delete
(confirmation dialog names the agent) → row gone, still gone after
reload. Confirmed throughout that the existing, unrelated bottom-nav
Agents tab continued showing only the one real heartbeat-derived agent,
untouched by any registry create/edit/delete.

## ADR-044 — AgentRole renamed to AgentType, values Capture/Ai → Low/High

**Why:** Discussion prompted by ADR-043's Agent admin screen: "usually
capabilities are assigned to an agent, how should we design the screen
in the admin section?" Traced through what `AgentRole` actually gates
(`Vivnest.Agent/Program.cs`'s `if (role == AgentRole.Capture)`/
`if (role == AgentRole.Ai)` DI branches, ADR-035) - it controls which
workers/dependencies a process loads, not a capability list; which
capabilities an instance actually produces is already fully determined
by device assignment (`CaptureAgentId`) for Capture and model config
presence for Ai, so no capability-assignment field was needed on the
Agent admin screen at all. Follow-up proposals to represent this as a
capacity tier (Light/Low/Med/High) or a manageable "AgentType" master
list were both rejected on the same grounds already established for
`CapabilityType` (ADR-041's follow-up): the role is a mechanical switch
baked into `Program.cs`, not extensible reference data - a master list
entry with no matching code branch would be inert. What survived: keep
the same two values and the same behavior, but rename both the values
and the type itself - `Low`/`High` read more naturally as an agent
"type" in the admin UI than `Capture`/`Ai` do, and once the values
stopped reading as roles, keeping the type named `AgentRole` (with a
property called `Role`) stopped making sense too. No new axis, field, or
entity - purely a rename, top to bottom.

**Pure rename, same semantics, applied consistently everywhere the
concept appears**: `Vivnest.Core.Enums.AgentRole` is now
`Vivnest.Core.Enums.AgentType` (`AgentRole.cs` deleted, `AgentType.cs`
added). `AgentType.Low` is exactly what `AgentRole.Capture` was
(camera/smart-plug/motion-sensor capture, `Program.cs` line ~198),
`AgentType.High` is exactly what `AgentRole.Ai` was (ONNX inference,
`Program.cs` line ~230) - only the DI branches' labels changed, not their
contents. Every property/field/config-key/label that carries the value
was renamed to match, not just the type:
- `AgentOptions.Role` → `AgentOptions.Type` (default `AgentType.Low`)
- `Agent:Role` config key → `Agent:Type` (`Program.cs`'s raw
  `IConfiguration` read, before typed options bind)
- `AgentRegistryEntity.Role` → `AgentRegistryEntity.Type` (still a plain
  string on the wire/in storage, `AgentType.ToString()`)
- `AgentRegistryDto`/`CreateAgentRegistryRequest`/
  `UpdateAgentRegistryRequest`'s `Role` → `Type`
- `IAgentRegistryManagementService`/`AgentRegistryManagementService`'s
  `role` parameters → `type`
- `AgentRegistryAdminFunction`'s `Enum.TryParse<AgentRole>(body.Role,
  ...)` → `Enum.TryParse<AgentType>(body.Type, ...)`, validation message
  "Role must be one of..." → "Type must be one of: Low, High."
- Dashboard: `AgentRegistryRole` → `AgentRegistryType` (`api.ts`),
  `AgentRegistry.role` → `.type`, `AgentRegistryFormModal`'s Role
  dropdown → Type dropdown (`ROLE_OPTIONS`/`agent-role` →
  `TYPE_OPTIONS`/`agent-type`), `AgentRegistryAdmin`'s
  `ROLE_STATUS_CLASS` → `TYPE_STATUS_CLASS`

No live config needed updating: confirmed no shipped config sets
`Agent:Role` explicitly (only ever used in a local dev throwaway test per
ADR-038's mention) - the tracked `Vivnest.Agent/appsettings.json` for the
real local agent has no `Role`/`Type` key at all and relies entirely on
the default, so the rename changes nothing about its actual behavior.

**Historical ADR text (ADR-035, ADR-038, ADR-043) intentionally left
unchanged** - those entries describe decisions as they were made at the
time, when `AgentRole`/`Capture`/`Ai` were the real names; rewriting them
would misrepresent what was actually decided when. This entry is the
record of the rename itself.

**Verified**: full backend rebuild (`Vivnest.Core`, `Vivnest.Cloud`,
`Vivnest.Cloud.Functions`, `Vivnest.Agent`) clean after the rename;
`tsc -b` clean on the dashboard.

## ADR-045 — DeviceOptions.CaptureAgentId renamed to OwningAgentId

**Why:** Direct follow-up question after ADR-044: "should we call
CaptureAgentId as DeviceAgentId?" `CaptureAgentId` names the field after
the pre-ADR-044 `AgentRole.Capture` value, which no longer exists.
Recommended `OwningAgentId` over the proposed `DeviceAgentId` for two
reasons: the field applies uniformly to Camera, MotionSensor, and
SmartPlug devices, but "Capture" was only ever the right verb for the
Camera case (`CameraCaptureWorker`) - the other two device types' own
code already uses "Monitor" (`MotionSensorMonitorService`,
`SmartPlugMonitorService`), so "Capture" was a camera-specific word
generalized to every device type. And `Program.cs`'s own code already
reaches for a different word the moment it reads this exact field - the
local variable at the point of consumption
(`TryLoadRemoteDeviceConfigsAsync`) was already named `owningAgentId`,
not `captureAgentId`, before this rename. `DeviceAgentId` on a class
called `DeviceOptions` also reads redundantly ("device's device agent
id"); `OwningAgentId` doesn't.

**Bigger blast radius than ADR-044**: not just an enum this time - a
JSON key inside the real, already-uploaded `device-config/{deviceId}.json`
blobs in Azure Blob Storage (`stvivnestagent2`) that the live agent reads
at startup. Renamed everywhere the concept appears: `DeviceOptions.CaptureAgentId`
→ `.OwningAgentId` (`Vivnest.Core/Options/DeviceOptions.cs`), the raw JSON
key read in `Program.cs`'s `TryLoadRemoteDeviceConfigsAsync`, both
`device.CaptureAgentId`/`candidate.CaptureAgentId` reads in
`DeviceCapabilitiesQueryService` (main-device tenant check and the
triggered-by candidate scan), all 4 local device-config drafts under
`Vivnest.Agent/device-config/*.json`, and the corresponding 4 real blobs
in the `device-config` container (re-uploaded via `az storage blob
upload` after the local rename, so the live agent's remote copies match
what ships in the repo).

**Doc sweep caught pre-existing staleness from ADR-044**: while updating
`current-architecture.md` for this rename, found several "Capture-role"/
"Ai-role"/"Ai-agent" mentions that ADR-044 should have updated to
"Low-type"/"High-type" but a case-sensitive grep missed at the time
(`Capture-role` doesn't match a pattern requiring `Role` capitalized).
Fixed those in the same pass rather than filing it away, per this
project's own "fix a stale doc on the spot" rule.

**Verified**: full backend rebuild (`Vivnest.Core`, `Vivnest.Cloud`,
`Vivnest.Cloud.Functions`, `Vivnest.Agent`) clean after the rename. Local
agent restarted against the renamed local device-config files and
resumed real camera capture/heartbeat/motion-sensor readings normally -
confirms `OwningAgentId` filtering still correctly resolves this agent's
3 devices after the key rename.

## ADR-046 — Agent registry gets a declared Capabilities list

**Why:** Direct request: "now we need to be able to map capabilities
agents in the admin menu." Revisits ground covered a few messages
earlier in the same thread, where a capability-assignment field on the
Agent admin screen was explicitly rejected - but that rejection was
about `Vivnest.Agent/Program.cs`'s *live* process, where capabilities
are already fully determined by real device assignment
(`DeviceOptions.OwningAgentId`, ADR-045) or model config presence
(`AiClassificationOptions`), so a stored field there would just drift
out of sync with reality. The Admin > Agents *registry* (ADR-043) is a
different thing: declared/planned identity for an agent that may not
have any devices wired up yet. Declaring "this agent will provide these
capabilities" as forward-looking reference data doesn't have a live
source of truth to drift from - there's nothing to derive it from until
real devices/config exist. That distinction is what makes a stored field
appropriate here where it wasn't on the live agent.

**Every agent type gets the same checklist, not just High-type** - since
this is planning data, not live capability probing, a Low-type agent
declaring "will provide Image Capture + Motion Detection" before any
camera/sensor is physically wired up is just as legitimate as a
High-type agent declaring its model capabilities.

**Storage: a comma-separated string on `AgentRegistryEntity`, not a join
table.** Azure Table Storage has no native array/collection type, and at
this project's actual scale (a handful of agents, a handful of
capabilities each) a separate join entity would be pure ceremony - same
"don't build for a scale that doesn't exist" reasoning
`DeviceCapabilitiesQueryService`'s O(N) blob scan already established.
`AgentRegistryEntity.CapabilityIds` is `string` (empty means none,
backward compatible with every row that predates this field);
`AgentRegistryManagementService` serializes/parses it to/from
`IReadOnlyList<Guid>` at the DTO boundary
(`AgentRegistryDto`/`Create`/`UpdateAgentRegistryRequest`'s
`CapabilityIds`, optional and defaulting to empty on the request records
so existing callers don't break).

**No server-side join for display** - `AgentRegistryDto` carries raw
`CapabilityIds`, not resolved names. The dashboard fetches the
Capability master list alongside the Agent list (`AgentRegistryAdmin`)
and cross-references client-side (`capabilityNameById` map) for both the
row badges and the form's checklist - same "resolve locally, no
server-side join" convention already established by `AgentDetail`'s
device list. No existence validation against the Capability master list
either, matching this codebase's general lack of FK-style validation
elsewhere (e.g. `ExecutingAgentId` was never validated against a real
agent existing).

**Dashboard**: `AgentRegistryFormModal` gained a `.form-checklist` of
checkboxes (new `.form-checklist`/`.form-checklist-item`/`.form-hint`
CSS, same token/spacing conventions as the rest of `App.css`'s admin
styles), populated from the `capabilities` prop `AgentRegistryAdmin`
already fetches. `AgentRegistryAdmin`'s row now shows a second
`.entity-row-subtitle` line listing the agent's declared capability
names (falls back to the raw id if a capability was since deleted from
the master list - no orphan-reference crash).

**Verified for real**, local-only: full curl round-trip (create with 2
capabilities → update dropping to 1, cross-checked directly against
`tblAgentRegistry` via `az storage entity show` → update omitting
`capabilityIds` entirely confirms backward-compat defaults to empty, not
a crash) plus a full browser pass - added 2 real capabilities, created
an agent checking both, confirmed the row badge line, edited to uncheck
one, reloaded the page (server-persisted, not local state), cross-checked
the reduced list directly against Table Storage again. All test data
cleaned up afterward.

## ADR-047 — Device Types master list, real CRUD (unlike CapabilityType)

**Why:** Direct request: "we should work on adding Devices similar to the
Agents in the admin, i think all the device attributes in the current
device-config should be there i think, we maybe also need to add list
for DeviceTypes?" Traced through whether `Vivnest.Core.Enums.DeviceType`
should become a manageable list, applying the same test already used for
`CapabilityType`/`AgentType` (ADR-041/044's follow-ups): is this a
mechanical classification real code branches on, or genuinely extensible
reference data? `DeviceType` fails that test the same way they did -
`CameraCaptureWorker`, `MotionSensorMonitorService`,
`SmartPlugMonitorService` are each hardcoded to one enum value, and 5 of
the 9 values (`HumiditySensor`/`SmokeAlarm`/`WaterLeak`/`HeatPump`/
`DoorSensor`) have no Agent implementation at all yet (per this
project's "camera-only implemented today" status - see [README.md](../../README.md)/
decision-log.md ADR-007). Recommended reusing the enum; **explicitly
overruled** - a real, manageable master list was requested anyway,
accepting that new entries won't be functional until matching Agent
worker code exists. Unlike the `CapabilityType`/`AgentType` decisions,
this one is the user's call to make differently, not a default I should
have talked out of - noted here as a real record of an accepted tradeoff,
not a design mistake.

**Structurally identical to `CapabilityEntity`/ADR-042** - global (not
tenant-scoped, a "Camera" is a shared concept across every tenant),
`DeviceTypeEntity` with a constant `PartitionKey`, `RowKey = DeviceTypeId`.
`DeviceTypeAdminDto(DeviceTypeId, DeviceTypeName)` - deliberately no
"type of type" field the way `CapabilityAdminDto` has `CapabilityType`,
since there's nothing analogous to classify a device type by.
`DeviceTypesAdminFunction` at `device-types-admin` (not `admin/`, and not
`devices-*` - avoids colliding with both the reserved prefix and the new
`devices-registry-admin` routes from ADR-048). No existence validation
against `Vivnest.Core.Enums.DeviceType` or against anything referencing a
deleted type - same no-FK-validation convention as `CapabilityIds`
elsewhere.

**Verified for real**: full curl CRUD round-trip against `func start`,
cross-checked against real `tblDeviceTypes` via `az storage entity show`;
browser pass creating a real "Camera" entry and confirming it appears as
a real, selectable option (not a placeholder) in the new Device registry
form's Device Type dropdown (ADR-048). Test data cleaned up afterward.

## ADR-048 — Device registry: declared identity + descriptive facts + free-form Settings

**Why:** Same request as ADR-047. Scoped via a direct question back: how
much of `DeviceOptions` should live in this new registry? Three options
laid out - bare identity only (mirrors ADR-043's Agent registry exactly),
full device-config replication (every `DeviceOptions` field including
`Settings` with real credentials, `Schedule`, `Trigger`, ROI, `Sensors`),
or identity plus the fields `DeviceOptions.cs`'s own doc comment already
calls "purely descriptive, never read by any capability's worker to
decide behavior" (`Location`/`Brand`/`Model`/`Firmware`). Full replication
was flagged as a materially bigger, riskier feature: it would need
`Program.cs` rewired to read config from Table Storage instead of
device-config blobs (or you get two disconnected sources of truth - this
registry never actually configures a real device either way, same as the
Agent registry never actually runs an agent), and it would put device
credentials through the Cloud admin API and Table Storage, directly
undoing ADR-038's design that secrets never leave local disk. Chosen:
identity + descriptive fields, **plus** a free-form Settings key-value
list per direct follow-up ("settings and fields that can change from
device to device should be key value pair, so we can add") - since
`DeviceOptions.Settings`'s actual shape already differs by device type
(Camera: `Host`/`Username`/`Password`/`RtspUsername`/`RtspPassword`;
MotionSensor: `Host`/`Username`/`Password`/`ChildDeviceId`; SmartPlug:
`Host`/`MACAddress`), a rigid per-type schema in the admin form would
just be re-deriving that same heterogeneity in a worse place. **Settings
is non-secret connection facts only** - `Password`/`RtspPassword`-shaped
data must never go through this API; see below for how that's enforced.

**`DeviceRegistryEntity` extends `BaseEntity`** (tenant-scoped, same
reasoning as `AgentRegistryEntity`) with `DeviceTypeId`/`OwningAgentId`
(both plain string references, empty means unset, no existence
validation - same convention as `CapabilityIds`), `Location`/`Brand`/
`Model`/`Firmware`/`Enabled` (descriptive, mirrors `DeviceOptions`
exactly), `CapabilityIds` (comma-separated, same pattern and reasoning
as ADR-046 - available for a device the same as an agent, not restricted
by device type), and `Settings` (JSON-serialized `Dictionary<string,
string>`, `DeviceRegistryManagementService` handles serialize/parse at
the DTO boundary same as `CapabilityIds`). No existing Azure Table
Storage array/collection type is why both `CapabilityIds` and `Settings`
are string-encoded rather than modeled as separate rows - same "don't
build a join table for a scale that doesn't exist" reasoning as
`DeviceCapabilitiesQueryService`'s O(N) blob scan.

**Credential guard is a real server-side check, not just documentation** -
`DeviceRegistryAdminFunction` rejects any `Settings` key that
case/separator-insensitively contains `password`/`rtsppassword`/
`secret`/`token`/`accesstoken` with a 400 naming the offending key and
pointing at the device's local `*.secrets.json` file instead. Explicitly
documented as best-effort, not a security boundary - a key named `pwd`
would slip past it - but it catches the obvious, likely mistake, which is
better than nothing given what's at stake (this API is Cloud-reachable,
unlike the local-only secrets files).

**`devices-registry-admin` routes** (not `admin/`, not literally
`/devices` - the real tenant-facing route) - CRUD shape identical to
`agents-registry-admin`. Dashboard: `DeviceRegistryFormModal` fetches
Device Types, Agents, and Capabilities (`DeviceRegistryAdmin` owns all
three fetches, passed down as props) purely for dropdowns/checklist and
client-side id -> name resolution on the list rows - same "resolve
locally, no server-side join" convention as `AgentRegistryAdmin`.
Settings gets a dynamic key-value row editor (new `.form-kv-list`/
`.form-kv-row`/`.form-kv-add` CSS) with an explicit warning line above it
naming the non-secret-only rule.

**Found live, fixed in the same pass**: with 10 fields (vs. Agent's 4),
`.form-dialog` had no height cap at all - the dialog pushed its own Save
button off the bottom of a real laptop-height viewport, reported
directly during verification ("the Device add/edit screen is too tall,
it's going over the my laptop screen"). Fixed with `max-height:
calc(100vh - 2rem)` + `overflow-y: auto` on `.form-dialog` - confirmed
via a real 1280x720 viewport that a dialog with `scrollHeight` far
exceeding its capped box now stays within the viewport and scrolls
internally instead of overflowing. Same fix benefits every existing
`.form-dialog` user (Capability/Agent/Device Type forms), not just this
one - none of them were long enough to hit the bug before now.

**Verified for real**: full curl CRUD round-trip (create with
`DeviceTypeId`/`OwningAgentId`/`CapabilityIds`/`Settings` all populated,
cross-checked directly against `tblDeviceRegistry` via `az storage
entity show`; negative check confirming the credential guard actually
rejects a `Password` key; update clearing fields back to empty/disabled;
delete) plus a full browser pass creating a device through the real UI
with every field populated (Device Type and Owning Agent dropdowns
sourced from real fetched data, a real capability checked, a real
Host/value Settings row added), confirming the row's resolved
type/agent/location/capability display, reopening it in Edit and
confirming every field - including the Settings row's actual `.value`,
not just its placeholder - came back pre-filled correctly, and
confirming the dialog height fix on a real constrained viewport. All
test data cleaned up afterward.

## ADR-049 — Surface real validation errors instead of "Request failed (400)"

**Why:** Direct bug report: "why do i get Request failed (400). when i
try to save a Device?" - hit while following up on the previous Settings
conversation, almost certainly by adding a `Password`-shaped key (exactly
what ADR-048's credential guard exists to reject). Root cause traced two
levels deep, both real bugs:

1. `api.ts`'s shared `request<T>()` helper discarded every non-401/404
   error response body entirely, always throwing a generic
   `Request failed (${status}).` - so `CapabilitiesAdminFunction`/
   `AgentRegistryAdminFunction`/`DeviceTypesAdminFunction`/
   `DeviceRegistryAdminFunction`'s specific `BadRequestObjectResult`
   messages (e.g. "Name is required.", the Settings credential-guard
   text) were being silently thrown away for every admin form, not just
   Device's - Device is just the first one whose validation a normal user
   was likely to actually trigger.
2. Fixing that required knowing the real wire format:
   `BadRequestObjectResult("some string")` on this ASP.NET Core Functions
   stack serializes as `Content-Type: text/plain` with the message as raw
   body text, **not** a JSON string - confirmed directly by calling the
   endpoint from the browser and inspecting `response.text()`/
   `response.headers.get('content-type')`. A first fix attempt assumed
   JSON encoding (`JSON.parse` then unwrap), which silently fell back to
   the same generic message for every plain-text body - verified this
   attempt actually still failed live before landing on using the raw
   text directly (JSON-parsing only as a defensive unwrap, in case some
   future endpoint returns a JSON-encoded string instead).

**Second bug, found live in the same repro**: even with the real message
now available, `DeviceRegistryAdmin`'s existing `handleError` (same
pattern as `AgentRegistryAdmin`/`CapabilitiesAdmin`/`DeviceTypesAdmin`)
sets a page-level `error` state whose presence replaces the *entire* admin
view - list and open modal both - with just the error paragraph. For a
1-3 field form (Capability/Agent/Device Type) that's mildly annoying; for
Device's 10-field form it meant a single rejected Settings key discarded
every other field the user had just filled in. Fixed for Device only
(the form where this is actually costly, and the one that surfaced the
bug) with a separate `saveError` state shown inline in the still-open
`DeviceRegistryFormModal` (new `.form-dialog-error` CSS) instead of
unmounting anything - the existing page-level `error` state is now used
only for the four initial list-load fetches, not for save failures. The
same rough edge still exists in the three simpler forms; left as-is since
they're cheap to re-fill and weren't what broke, but worth applying the
same fix if one of them grows fields the way Device did.

**Verified for real**: reproduced the exact failure via the real UI (Add
Device, fill Name + Location, add a `Password` Settings row, Save) and
confirmed, in order: (1) before the fix, the generic message; (2) after
the JSON-assumption attempt, still the generic message - confirmed via
`response.headers.get('content-type')` directly from the page's own
`fetch()` that the real body is `text/plain`, not JSON; (3) after the
correct fix, the real credential-guard message shown inline, dialog still
open, Name and Location fields still populated; (4) fixing the bad
Settings key to `Host` and saving succeeds normally, confirming no
regression to the success path.

## ADR-050 — Device registry Settings allowed to hold credentials

**Why:** Direct request, immediately following ADR-049's fix: "also allow
password and sensitive info to be added via the settings too." Reverses
ADR-048's original credential guard on `DeviceRegistryAdminFunction`.
Flagged the concrete consequence before implementing (per this project's
practice of surfacing risk rather than silently complying with a security
posture change) and got explicit confirmation to proceed anyway: unlike
every other credential in this codebase, which stays exclusively on local
disk in a `*.secrets.json` file never uploaded anywhere (ADR-038),
anything entered in this Settings field now travels to the Cloud Function
over HTTPS, is stored as plain text in `tblDeviceRegistry`, and is
returned as plain text by `GET devices-registry-admin` to any caller
holding a valid tenant `x-api-key`. This is a real, accepted departure
from that design, not an oversight - the user's call to make for their
own system, made with the tradeoff stated plainly first.

**Removed**: `DeviceRegistryAdminFunction`'s `DisallowedSettingsKeys`
check and `TryFindDisallowedSettingsKey` helper entirely (both
Create/Update routes) - no replacement validation. `DeviceRegistryEntity.
Settings`'s doc comment, `DeviceRegistryManagementService`'s class
comment, and `DeviceRegistryFormModal`'s in-form warning text all updated
to state plainly what's now true (credentials accepted, stored/returned
as plain text) rather than what ADR-048 originally guaranteed.

**Verified for real**: curl round-trip creating a device with
`{"Host": "...", "Password": "hunter2"}` in `Settings` - confirmed 200
(previously would have 400'd with the now-removed guard's message) and
the value round-tripping unchanged in the response. Backend rebuilt
clean, dashboard `tsc -b` clean, real local agent/func restarted and
confirmed still monitoring normally. Browser-confirmed the form's warning
text now states the actual behavior instead of a prohibition. Test data
cleaned up afterward.

## ADR-051 — Tenant/Site domain model foundation

**Why:** Externally-authored spec ("Vivnest — Tenant & Site Domain Model
Specification") pasted by the user, asking for a review of what it would
take to implement. The spec's hard requirements: preserve the existing
`PartitionKey = "{TenantId}|{SiteId}"` convention used by tenant-scoped
entities (`AgentRegistryEntity`, `DeviceRegistryEntity`, etc.) immutably;
no EF Core or relational database; no rewrite of existing storage;
introduce `Tenant`, `Site`, `SiteScope`, `ISiteScoped` as real domain
concepts; do not model Sites/Machines/Agents/Devices as an in-memory
collection inside a `Tenant` aggregate; existing MVP flows (appsettings,
device-config/*.json, heartbeats, events) must keep working unchanged; do
not touch Machine/Agent/Device/Capability yet. Reviewed against the actual
codebase first (no `Tenant`/`Site`/`SiteScope`/`ISiteScoped` existed
anywhere) rather than assumed compliance, then implemented against the
user's confirmed defaults after three flagged judgment calls (naming
followed existing conventions exactly; new types are additive only, no
retrofitting of existing entities beyond `BaseEntity : ISiteScoped`; no
automated test project, matching this repo's existing "known,
explicitly-deferred gap" - verified for real instead, same as every prior
admin feature this session).

**New domain layer** (`Vivnest.Core/Domain/`) - a genuinely new third
layer for this codebase, sitting between the `I<Feature>Store`
(Table-shaped) and `<Feature>ManagementService` (DTO-shaped) pair every
prior admin feature used directly:
- `Tenant`/`Site` - persistence-agnostic classes, private setters, a
  validating public constructor for real creation, and a `Rehydrate(...)`
  static factory that bypasses validation when reconstructing from
  already-trusted storage data (needed so an Update flow can restore
  `CreatedUtc`/`Status` rather than resetting them). `Site.TenantId`/
  `SiteId` have no public mutator - immutable after construction, per the
  spec.
- `ISiteScoped` (`{ string TenantId { get; } string SiteId { get; } }`) -
  marker interface, now implemented by `BaseEntity` (a one-line addition;
  `BaseEntity` already had `required string TenantId/SiteId { get; init; }`,
  which already satisfies a getter-only interface, so this is a pure
  label with no behavior change).
- `SiteScope` - `readonly record struct(string TenantId, string SiteId)`
  with a `PartitionKey => $"{TenantId}|{SiteId}"` computed property.
  Exists to DRY up the ~6 places that used to hand-roll this exact string
  interpolation independently - now wired into all of them:
  `AgentHeartbeatWriter`/`DeviceHeartbeatWriter` (Agent-side, via
  `Vivnest.Infrastructure`), `HealthMonitorService`/`DeviceQueryService`/
  `AgentRegistryManagementService`/`DeviceRegistryManagementService`
  (Cloud-side). `DeviceHeartbeatEntity`'s partition key is
  `"{TenantId}|{SiteId}|{AgentId}"` (three parts, not two), so those call
  sites compose `$"{new SiteScope(...).PartitionKey}|{agentId}"` rather
  than using `SiteScope.PartitionKey` alone.

**New persistence layer**:
- `TenantEntity` - global master list, same shape as `CapabilityEntity`/
  `DeviceTypeEntity`: constant `PartitionKey = "TENANT"`, `RowKey =
  TenantId`. One partition-scoped query lists every Tenant.
- `SiteEntity` - `PartitionKey = TenantId`, `RowKey = SiteId`. This is a
  genuinely new partitioning shape (neither the global-constant pattern
  nor the `"{TenantId}|{SiteId}"` tenant-scoped pattern), chosen
  specifically to make "list all Sites for Tenant X" a single
  partition-scoped query - the spec's composite-key requirement governs
  the *existing* tenant-scoped entities (Agent/Device registries,
  heartbeats), not Site's own row key, since Site has no Site-scoped
  child data of its own yet.
- `ITenantStore`/`AzureTableTenantStore`, `ISiteStore`/
  `AzureTableSiteStore` - both intentionally have no `DeleteAsync`. A
  Tenant/Site is the ownership boundary other data scopes under, not
  disposable reference data like a Capability - deactivate via
  `UpdateAsync(status: Inactive)` instead.

**New service/API layer**:
- `TenantManagementService`/`SiteManagementService` map the domain
  classes to/from their entities. `SiteManagementService` depends on
  `ITenantStore` only to enforce "a Site can't be created under a
  nonexistent Tenant" at create time (returns `null` → 409) - it never
  reads/writes Tenant data otherwise.
- `TenantsFunction`/`SitesFunction` (`Vivnest.Cloud.Functions`, root
  namespace, not `.Http`) use `AuthorizationLevel.Function` throughout,
  the same operator-only tier as `ApiKeysFunction` - **not** the
  tenant-scoped `x-api-key`/`ApiFunctionBase` pattern every other admin
  feature this session used. Deliberate: a tenant `x-api-key` is scoped
  to exactly one Tenant/Site; if these routes accepted one, any tenant
  could list or create every other tenant, which is exactly the
  cross-tenant boundary violation the Tenant/Site model exists to
  prevent. Routes: `GET/POST tenants`, `GET/PUT tenants/{tenantId}`,
  `GET/POST tenants/{tenantId}/sites`, `GET/PUT
  tenants/{tenantId}/sites/{siteId}`.
- `TenantId`/`SiteId` are caller-chosen strings (not server-generated
  Guids, unlike Capability/AgentRegistry/DeviceRegistry ids) - `Create`
  returns `null` → 409 on collision instead of the Guid-based "can't
  collide" assumption those other features relied on.

**Verified for real**: real local `func start` restarted (a stale host
process from before these routes existed was killed first) and the real
local `Vivnest.Agent` process restarted alongside it - confirmed both
still operating normally (heartbeats, camera capture, classify relay all
observed in logs) before and after. Curl round-trip against
`stvivnestagent2`: create Tenant → duplicate Tenant (409) → list → get →
get-unknown (404) → create Site under a nonexistent Tenant (409) → create
Site under the real Tenant → duplicate Site (409) → list Sites for Tenant
→ get Site → update Site → update Tenant with an invalid Status (400) →
update Tenant to Inactive. Cross-checked `tblTenants`/`tblSites` directly
via `az storage entity query` - partition/row keys and field values
matched the API responses exactly (`TenantEntity`: `PartitionKey=TENANT,
RowKey=acme`; `SiteEntity`: `PartitionKey=acme, RowKey=hq`). Test data
deleted afterward directly via `az storage entity delete` (no Delete API
exists for these on purpose, per above).

**`SiteScope` refactor verified for real too**: rebuilt `Vivnest.Agent`
(picks up `Vivnest.Infrastructure`) and `Vivnest.Cloud.Functions`
(picks up `Vivnest.Cloud`) clean, restarted the real local agent and
`func start` host, confirmed heartbeats/captures/classify relay still
flowing normally - the refactor is a pure call-site substitution
(`SiteScope.PartitionKey` computes the exact same string the old
interpolation did), so no behavior change, just one fewer place that
could drift out of sync with the `"{TenantId}|{SiteId}"` convention.

**Also created for real** (not test data, left in place): Tenant `Sana`
/ Site `1Fitz` - the tenant/site pair every real device in this dev
environment already reports under (visible in blob paths like
`Sana/1Fitz/{agentId}/{deviceId}/...`), which had no corresponding
`tblTenants`/`tblSites` row until this ADR gave the concept somewhere to
live.

**Not done as part of this ADR** (explicitly out of scope, per the
spec's own "do not proceed to Machine/Agent/Device/Capability redesign"
constraint and this session's practice of not building UI unless asked):
no Tenant/Site admin dashboard screen (the spec's deliverables list only
covers Domain/Persistence/Services/API/Tests, unlike Capability/Agent/
Device/DeviceType which all got one); any automated test project.

**Addendum - soft-delete `DELETE` routes**: Follow-up request, same
session. `DELETE tenants/{tenantId}` and
`DELETE tenants/{tenantId}/sites/{siteId}` added as thin wrappers over a
new `DeactivateAsync` on both management services - sets `Status` to
`Inactive` and nothing else, identical effect to `PUT .../{id}` with
`{"Status": "Inactive"}`, just a more RESTful verb for it. Still no hard
delete (unchanged reasoning: Tenant/Site is the ownership boundary
everything else scopes under). Verified for real: created a scratch
Tenant + Site, `DELETE` both (200, `Status: Inactive` in the response),
confirmed a follow-up `GET` still finds them (soft, not gone), `DELETE`
on an unknown Tenant still 404s. Cleaned up the scratch rows afterward
via `az storage entity delete` (left the real `Sana`/`1Fitz` rows
untouched). `func start` rebuilt/restarted, confirmed the real agent's
heartbeats/captures kept flowing.

## ADR-052 — Validate Tenant/Site existence when creating an API key

**Why:** Direct question ("is it now safe to add validation logic when
creating api key?") - `POST /apikeys` has always accepted any
`TenantId`/`SiteId` string with no existence check, since before
ADR-051 there was nowhere to check against. Checked the real data before
answering: `tblApiKeys` holds exactly one key, `Sana`/`1Fitz` - the same
pair registered as real Tenant/Site rows in the last change - so adding
the check wouldn't reject anything already in use. Confirmed the check
is also safe to add narrowly (creation-time only): `ApiKeyAuthenticator`
resolves `TenantContext` from the key's own denormalized `TenantId`/
`SiteId` on `ApiKeyEntity`, never re-querying `tblTenants`/`tblSites`, so
this can't retroactively invalidate any key that already exists,
including ones for a Tenant/Site that predates this ADR or gets
deactivated later.

**Scope, per explicit choice**: reject if the Tenant is missing *or* not
`Status: Active`, and same for the Site - not existence-only. A
deactivated (soft-deleted, ADR-051) Tenant/Site can't be handed new
keys.

**Implementation**: `ApiKeyManagementService` gained `ITenantStore`/
`ISiteStore` constructor deps (same shape `SiteManagementService`
already uses `ITenantStore` for its own existence check). `CreateAsync`
now returns `ApiKeyCreationResult?` - `null` on either check failing -
instead of the previous non-nullable result. `ApiKeysFunction.CreateApiKey`
returns 400 with a message naming both the TenantId and SiteId on `null`.
`ListAsync`/`RevokeAsync` untouched - they operate on `ApiKeyEntity`
directly and were never in scope for this check.

**Verified for real**: rebuilt `Vivnest.Cloud.Functions` clean (had to
kill the running `func start`'s worker host first - it held
`Vivnest.Core.dll` locked), restarted it, curl-tested: unknown TenantId
→ 400, known Tenant + unknown SiteId → 400, real Active `Sana`/`1Fitz` →
200 (real key minted, immediately revoked afterward since it was just a
verification byproduct, not a requested key). Created a scratch Tenant +
Site, soft-deleted the Site only → key creation 400s even with the
Tenant still Active; soft-deleted the Tenant too → still 400s. Scratch
Tenant/Site deleted via `az storage entity delete` afterward. Confirmed
the real agent kept heartbeating throughout (this change didn't touch
`Vivnest.Agent`, so no rebuild/restart was needed on that side).

## ADR-053 — Machine / Agent / AgentInstallation domain model

**Why:** Externally-authored follow-on spec ("Vivnest — Machine, Agent &
Agent Installation Domain Specification"), explicitly building on ADR-051
and covering exactly what that ADR deferred ("do not touch Machine/
Agent/Device/Capability yet"). Core idea: separate three identities that
today are conflated or missing entirely - `AgentId` (WHO, a stable
logical identity), `MachineId` (WHERE, the physical/virtual host),
`InstallationId` (WHICH DEPLOYMENT, linking one to the other with
history), `ContainerId` (the ephemeral Docker runtime instance, never a
domain identity). Reviewed against the actual codebase before
implementing (not assumed compliance) and surfaced four judgment calls,
all resolved to the recommended default:

1. **`Agent` reconciles with the existing `AgentRegistryEntity`
   (ADR-043/046), not a parallel `tblAgents`.** The spec's `Agent`
   concept (tenant/site-scoped, `RowKey = AgentId`, `Name`/`Description`/
   `Status`) is materially the same thing `AgentRegistryEntity` already
   is - extending it in place (`Description`, `Status`, `CreatedUtc`,
   `UpdatedUtc` added directly to the entity/DTO) avoids two competing
   "list of this tenant's agents" tables. `tblAgentRegistry`,
   `agents-registry-admin` routes, and the existing dashboard screen keep
   their names - no renaming churn on a working, tested feature, per this
   project's gradual-evolution principle.
2. **No `CurrentMachineId`/`CurrentInstallationId` denormalized onto
   Agent.** "Where is this agent currently installed" is answered by
   `AgentInstallationManagementService.GetActiveByAgentAsync`, not a
   second field that could drift out of sync - same reasoning ADR-030
   already established for `DeviceSummaryDto.ThumbnailUrl`.
3. **`AgentInstallation` is purely declarative for this phase, not wired
   to the real deploy pipeline.** Creating/moving/uninstalling an
   installation record does not call into `Vivnest.Agent.Updater`'s
   `AgentDeployer`/`DeployPollingWorker`, and a real `docker run` doesn't
   write an installation row either - the two are independent until a
   later "Agent Synchronization" phase (which the spec itself describes
   as future work). Matches the spec's own "the goal is not to rewrite
   the Agent runtime."
4. **No automated test project**, same resolution as ADR-051 despite this
   spec's much more explicit test section (~20 named cases, phase
   completion gated on "tests pass") - verified every case for real
   against local `func start` + live Azure Table Storage instead,
   consistent with every other feature this session. A real, deliberate
   departure from the spec's literal instruction, not an oversight.

**New domain layer** (`Vivnest.Core/Domain/`):
- `Machine` - `TenantId`/`SiteId`/`MachineId`/`Name`/`Hostname?`/
  `Description?`/`Status`/`OperatingSystem?`/`Architecture?`/
  `CreatedUtc`/`UpdatedUtc`. `MachineId` is caller-chosen (like
  `TenantId`/`SiteId`), not a generated Guid - the spec's own examples
  (`M001`, `M002`) read as operator-assigned, memorable ids for physical
  hardware, not a disposable reference-data id. `MachineStatus`:
  `Active`/`Offline`/`Retired`/`Decommissioned` (spec section 12,
  verbatim). If hardware is permanently replaced, the old `Machine` is
  retired (`SetStatus`), never reused for different physical hardware.
- `AgentInstallation` - `TenantId`/`SiteId`/`InstallationId`/`AgentId`/
  `MachineId`/`ContainerId?`/`ImageName?`/`ImageVersion?`/`Status`/
  `InstalledUtc`/`RemovedUtc?`/`UpdatedUtc`. `InstallationId` IS a
  generated Guid (unlike `Machine`) - an installation is the record of a
  lifecycle action (Install/Move/Uninstall), not something an operator
  names. `AgentInstallationStatus`: `Active`/`Removed` (spec's own
  vocabulary, sections 1/4/11/12). A `Remove()` method flips status and
  stamps `RemovedUtc` - used both for a real uninstall and for retiring
  the old installation during a Move (the old row is never mutated to
  point at a new Machine - moving creates a new installation and retires
  the old one, preserving history, per spec section 11).
- `AgentRegistryEntity`'s own domain gap: `AgentId`/`MachineId` on
  `AgentInstallation` are relationship properties, not validated for
  existence by the domain class itself - existence is checked by
  `AgentInstallationManagementService` before construction, mirroring
  ADR-052's "validate at the point something real gets created."

**New persistence layer**:
- `MachineEntity` - `BaseEntity`-derived (tenant-scoped, like
  `AgentRegistryEntity`/`DeviceRegistryEntity`), `PartitionKey =
  TenantId|SiteId`, `RowKey = MachineId`.
- `AgentInstallationEntity` - same partitioning, `RowKey =
  InstallationId`. No separate `tblMachineAgents` relationship table
  (spec section 17, explicit) - "agents currently on Machine X" and "an
  Agent's installation history" are both answered by
  `AgentInstallationEntity` queries alone:
  `GetByAgentAsync`/`GetByMachineAsync`/`GetActiveByAgentAsync`/
  `GetActiveByMachineAsync` all filter the tenant/site partition scan on
  `AgentId`/`MachineId`/`Status` client-side (same shape
  `AzureTableDeviceEventReader`'s tenant-wide queries already use).
- `IMachineStore`/`AzureTableMachineStore`,
  `IAgentInstallationStore`/`AzureTableAgentInstallationStore` - no
  `DeleteAsync` on either. A Machine's identity should remain stable for
  its lifetime (retire via `Status`, never delete/reuse an id); an
  AgentInstallation is a historical record, not disposable reference
  data - it's marked `Removed`, never deleted.

**New service/API layer**:
- `MachineManagementService` - same CRUD shape as
  `TenantManagementService`, `CreateAsync` returns `null` → 409 on a
  `MachineId` collision (caller-chosen id, same reasoning).
- `AgentInstallationManagementService` - the orchestration service the
  spec's section 19-20 asks for, living in `Vivnest.Cloud.Admin` like
  every other management service, not inside the Table repository.
  `InstallAsync` validates the Agent exists (`IAgentRegistryStore`), the
  Machine exists (`IMachineStore`), and that the Agent doesn't already
  have an active installation - returns `null` (409) on any of the
  three, directly enforcing the spec's "at most one active installation
  per Agent" invariant (section 10) at the point of creation, not via a
  table-level constraint (Azure Table Storage has none). `MoveAsync`
  retires the current active installation (if any) and creates a new one
  on the new Machine in the same call - both writes land in the same
  `TenantId|SiteId` partition but aren't wrapped in an Azure Table batch
  transaction; sequential writes, same pragmatic-non-transactional style
  used everywhere else in this codebase. `UninstallAsync` marks the
  active installation `Removed` and returns `null` (404) if there wasn't
  one.
- `MachinesFunction`/`AgentInstallationsFunction`
  (`Vivnest.Cloud.Functions/Http`) - tenant `x-api-key` via
  `ApiFunctionBase` + `DevicesOnly` 403 gate, same tier as
  `AgentRegistryAdminFunction`/`DeviceRegistryAdminFunction` - a Machine
  or an installation record is tenant-owned operational data, not the
  Tenant/Site ownership boundary itself (which stays on the
  operator-only `AuthorizationLevel.Function` tier). Routes:
  `GET/POST machines-admin`, `PUT machines-admin/{machineId}`;
  `POST agent-installations-admin/install`,
  `POST agent-installations-admin/move`,
  `POST agent-installations-admin/uninstall`,
  `GET agent-installations-admin/by-agent/{agentId}`,
  `GET agent-installations-admin/by-machine/{machineId}`,
  `GET agent-installations-admin/active-by-agent/{agentId}`,
  `GET agent-installations-admin/active-by-machine/{machineId}`.
  Install/Move/Uninstall are `POST` actions, not a CRUD resource - they
  carry real invariants, not a bag of fields to overwrite.

**A real bug found and fixed during verification**: extending
`AgentRegistryEntity` with non-nullable `DateTime CreatedUtc`/
`UpdatedUtc` looked backward-compatible on paper (rows that predate the
fields just read as `default(DateTime)`), but `UpdateAsync` never touched
`CreatedUtc`, so updating a pre-existing row crashed with `System.
NotSupportedException: DateTime ... has a Kind of Unspecified. Azure SDK
requires it to be UTC` - the Azure Table SDK rejects a `DateTimeKind.
Unspecified` value on write, and a `default(DateTime)` field with no
stored column is exactly that. Reproduced for real by editing one of the
two genuinely pre-existing `AgentRegistryEntity` rows through the
browser (a `500` came back, confirmed in the `func start` log). Fixed by
backfilling `CreatedUtc` in `AgentRegistryManagementService.UpdateAsync`
- if it's still `default`, set it to `UpdatedUtc` (no real creation time
exists for these rows); otherwise normalize its `Kind` to `Utc` before
the write. Re-verified the exact same browser edit succeeds (`200`, not
`500`) and reverted the test edit afterward so the real row's content is
unchanged.

**Verified for real** (all against local `func start` + live
`stvivnestagent2` Table Storage, using a scratch API key created under
the real `Sana`/`1Fitz` tenant and revoked afterward): Machine CRUD
(create, duplicate → 409, list, get, get-unknown → 404, invalid status →
400). Agent CRUD with the new fields, including confirming the two
genuinely pre-existing `AgentRegistryEntity` rows (which predate this
ADR) still list correctly with `Status` falling back to `Active` and
`CreatedUtc`/`UpdatedUtc` reading as the epoch default without crashing
on `GET`. The full hardware-replacement scenario from spec section 4,
assertion-for-assertion: installed Agent A001 on Machine M001, confirmed
installing the same Agent again while active 409s, confirmed installing
onto a nonexistent Machine 409s, retired M001 (`Status: Retired`),
created replacement Machine M002, moved A001 to M002, then confirmed via
`GetByAgentAsync` that the Agent's full history shows exactly two
installations - the old one `Removed` and still pointing at M001, the
new one `Active` and pointing at M002 - with `AgentId` unchanged
throughout. Also verified multiple Agents (A001, A002) both holding
simultaneous active installations on the same Machine (M002), and
Uninstall (marks `Removed`, a second Uninstall 404s). Cross-checked
`tblMachines`/`tblAgentInstallations`/`tblAgentRegistry` directly via `az
storage entity query` - every `PartitionKey` read exactly `Sana|1Fitz`,
confirming the immutable `TenantId|SiteId` convention held across all
three new/extended tables. 401 confirmed for both endpoint families with
no key and with a bad key. All scratch Machines/Agents/Installations
deleted afterward (`az storage entity delete` for Machine/Installation,
the real `DELETE agents-registry-admin/{agentId}` endpoint for the two
test Agents) - the two genuinely pre-existing Agent rows and the real
`Sana`/`1Fitz` Tenant/Site were left untouched. Backend (`dotnet build`)
and dashboard (`tsc -b`, `oxlint`) both clean. Real local `Vivnest.Agent`
process confirmed still heartbeating/capturing normally throughout, with
no rebuild/restart needed on that side (this change didn't touch
`Vivnest.Agent`/`Vivnest.Infrastructure`).

**Dashboard**: `AgentRegistryFormModal.tsx`/`AgentRegistryAdmin.tsx`/
`api.ts` updated to keep the *existing* Agent Registry admin screen
working with the new `Description`/`Status` fields (a Description input,
a Status dropdown shown only when editing since `Status` isn't accepted
on create). Browser-verified end to end: opened the real screen, edited
a genuinely pre-existing agent, hit the `CreatedUtc` bug live, confirmed
the fix, reverted the edit. No dashboard screen was built for Machine or
AgentInstallation - like ADR-051, none was requested, and the spec's own
deliverables list only covers Domain/Persistence/Services/API/Tests.

**Not done as part of this ADR**: no dashboard UI for Machine/
AgentInstallation; no automated test project; no wiring between
AgentInstallation and the real `Vivnest.Agent.Updater` deploy pipeline;
no change to the real Agent heartbeat payload (spec section 23 explicitly
allows deferring this); no Device/Capability redesign (spec section 24,
explicitly next-phase work).

**Addendum - `MachineId` changed to a generated Guid**: Direct follow-up
question, same session ("why isn't tenantId/siteId/machineId a Guid?").
`TenantId`/`SiteId` were deliberately left as-is - explained why (they're
the literal `PartitionKey` for every tenant-scoped table, hand-typed in
the real running agent's `appsettings.json`, and used as real blob
capture folder paths - converting them would mean editing live
production-like config and re-migrating every table in the account, not
a quick change). `MachineId` was a much smaller, contained case (only
`tblMachines` and the `MachineId` references in
`tblAgentInstallations`, both brand new this session with no real data
beyond dummy rows) - user confirmed narrowing scope to `MachineId` only.

Changed `Machine`'s constructor to generate `MachineId =
Guid.NewGuid().ToString()` internally (dropped the `machineId`
parameter) - now matches `AgentRegistryEntity`'s pattern exactly, not
`Tenant`/`Site`'s caller-chosen-id pattern. `MachineDto.MachineId` is now
typed `Guid` (was `string`), same as `AgentRegistryDto.AgentId`.
`CreateMachineRequest` no longer accepts `MachineId` - the Function
layer no longer validates or forwards it, and `MachineManagementService.
CreateAsync` no longer does an existence check before create (a Guid
can't collide, so the `409`-on-collision path this class had for
`Tenant`/`Site`-style ids was removed entirely - `CreateAsync` is no
longer nullable). `GetAsync`/`UpdateAsync` still take `machineId` as a
plain `string` from the route, matching `AgentRegistryManagementService`'s
existing convention of not re-typing route-sourced ids as `Guid`.

**Verified for real**: rebuilt and restarted `func start`. Confirmed
`POST machines-admin` with no `MachineId` in the body now returns a
server-generated Guid; confirmed a stray `machineId` field in the body is
silently ignored (never read); confirmed `GET`/`PUT` by the new Guid id
both work. Wiped the 5 dummy `M001`-`M005` rows created before this
change (old scheme, incompatible with the new one) and recreated all 5
with the new Guid-based flow, preserving the same names/descriptions/
statuses (3 Active, 1 Offline, 1 Retired). Confirmed the real agent kept
heartbeating throughout - this change didn't touch `Vivnest.Agent`.

## ADR-054 — API key creation moved into the dashboard, behind a separate operator login

**Why:** Direct request ("i think crating of API keys should be done from
UI"), following on from a discussion about converting `TenantId`/`SiteId`
to generated Guids (declined - see ADR-053's addendum for why that stays
caller-chosen) and how a dashboard form would show Tenant/Site by Name
while still submitting by Id. Flagged before implementing: `POST
/apikeys`, and the `GET /tenants`/`GET .../sites` a Tenant/Site picker
needs, are all gated by the Azure Functions host key
(`AuthorizationLevel.Function`) - a materially different, more privileged
credential than the tenant `x-api-key` the dashboard's existing
`ApiKeyGate.tsx` collects (it can list/create every tenant and mint/
revoke keys for any of them). The dashboard had no concept of that key at
all before this. Confirmed with the user: build a genuinely separate
operator login (not an extension of the tenant one), and build a real
Tenant/Site picker rather than hardcoding the one real `Sana`/`1Fitz`
pair.

**New**: `OperatorKeyGate.tsx` - mirrors `ApiKeyGate.tsx`'s shape
exactly, but for the host key, stored under its own `localStorage` key
(`vivnest.operatorKey`, never mixed with the tenant session's
`vivnest.apiKey`). Unlike the tenant flow (validated via `GET /whoami`),
there's no dedicated "who am I" endpoint for the operator tier, so this
validates by making a real call (`GET /tenants`) and checking whether it
401s. `ApiKeysAdmin.tsx` - the actual screen: owns its own auth state
(renders `OperatorKeyGate` until a host key is present, entirely separate
from the tenant `apiKey`/`onAuthError` prop pair every other Admin screen
takes), a dependent Tenant -> Site dropdown pair (by Name; selecting a
Tenant loads its Sites), the existing keys list for the selected Tenant/
Site (`GET /apikeys?tenantId=&siteId=`, with a Revoke button per
`Enabled` key), and the create form (Name, `DevicesOnly` checkbox). A
created key's raw value is shown exactly once in a copy-and-dismiss box
(`CopyIcon`/`CheckIcon` from the existing icon set) with an explicit
"this will never be shown again" warning, since `tblApiKeys` only ever
stores a hash - there is no other endpoint that can retrieve it after
this response.

`api.ts` gained a parallel `operatorRequest<T>()` (sends
`x-functions-key`, the standard Azure Functions header for this tier,
instead of `x-api-key`) and `TenantAdmin`/`SiteAdmin`/`ApiKeySummary`/
`CreatedApiKey` types plus `getTenantsOperator`/`getSitesOperator`/
`getApiKeysOperator`/`createApiKeyOperator`/`revokeApiKeyOperator`.
`AdminDrawer.tsx` gained an "API Keys" item, set apart from the four
tenant-tier items above it by a divider (`.admin-drawer-divider`) - it's
the one item in that drawer backed by a fundamentally different
credential.

**A real bug found and fixed during verification**: `revokeApiKeyOperator`
initially reused `operatorRequest<void>()`, which unconditionally calls
`response.json()` - but `RevokeApiKey` returns `200 OkResult()` with no
body at all, so parsing threw `Unexpected end of JSON input` and the
whole page fell back to the page-level error view (the same class of bug
`deleteAgentRegistryEntry`/`deleteDeviceRegistryEntry` already route
around for their 204 responses, missed here because a 200-with-no-body
looks less obviously suspicious than a 204). Reproduced for real in the
browser: created a scratch key, revoked it through the UI, watched the
page break even though the network tab showed the revoke itself
succeeding server-side (`200 OK`) - the bug was purely client-side
response parsing, not a failed revoke. Fixed by giving
`revokeApiKeyOperator` its own raw `fetch()` that never calls `.json()`,
matching the existing DELETE-endpoint pattern exactly. Re-verified with a
second scratch key: revoke now completes cleanly, no error, list
refreshes to show it `Revoked`.

**Verified for real**, entirely in the browser against the real
`Sana`/`1Fitz` tenant: opened Admin > API Keys, logged into operator mode
with a dummy string (confirmed local `func start` doesn't enforce
`AuthorizationLevel.Function` at all, same as every other operator-tier
route tested earlier this session - the real host key only matters once
deployed), selected `Sana` then `1Fitz`, confirmed the existing-keys list
showed every real key created over the course of this session with
accurate `Enabled`/`Revoked` status (including the real, untouched
`Admin` key), created a real key through the form, confirmed the
one-time reveal box showed the actual value, revoked it (after the fix
above) and watched it disappear from the actionable list. Confirmed
logging out of operator mode returns to `OperatorKeyGate` while leaving
the underlying tenant session (`Sana / 1Fitz` header) completely
untouched - the two logins are genuinely independent. `tsc -b` and
`oxlint` both clean (only the same pre-existing `only-export-components`
warning pattern `ApiKeyGate.tsx` already has, now also on
`OperatorKeyGate.tsx` for the same reason - a component file also
exporting plain helper functions). Real local agent confirmed still
heartbeating throughout - this change is dashboard-only, no backend
changes.

## ADR-055 — `TenantId`/`SiteId` changed to generated Guids

**Why:** Direct follow-up to ADR-053's addendum (which explicitly kept
`TenantId`/`SiteId` caller-chosen, citing the real running agent's local
config and real blob capture paths as reasons this would need "careful,
deliberate planning" if it ever happened). The user manually wiped every
table in the real storage account (all 13 admin/reference/live-monitoring
tables except `tblApiKeys`, at their explicit direction after this
session's own wipe script - see the addendum after this ADR) specifically
to clear the way, then asked directly for `TenantId`/`SiteId` to become
Guids and for fresh records to be populated. With no data left to
migrate, the single biggest cost flagged in ADR-053's addendum (per-row
migration across every tenant-scoped table) no longer applied - what
remained was a genuinely small code change, confirmed before touching
anything: `TenantId`/`SiteId` are already carried as opaque strings by
every consumer (`SiteScope.PartitionKey`, `BaseEntity`, `ApiKeyEntity`,
`TenantContext`, every tenant-scoped entity), so the *only* code that
actually needed to change is where a Tenant/Site gets created.

**Domain layer**: `Tenant`'s constructor dropped the `tenantId` parameter
- `TenantId = Guid.NewGuid().ToString()` internally, exactly mirroring
`Machine`'s constructor from ADR-053's addendum. `Site`'s constructor
dropped `siteId` (kept `tenantId`, since a Site still needs to know which
Tenant it belongs to) - `SiteId` generated the same way.

**API layer**: `TenantDto.TenantId`/`SiteDto.TenantId`/`SiteDto.SiteId`
retyped `string` → `Guid` (same convention as `AgentRegistryDto.AgentId`
and `MachineDto.MachineId`) - the JSON wire shape is unchanged (a `Guid`
still serializes as a plain string), so this needed zero dashboard
changes despite `ApiKeysAdmin.tsx`/`api.ts` consuming `TenantAdmin`/
`SiteAdmin` types with `tenantId`/`siteId: string` fields.
`CreateTenantRequest`/`CreateSiteRequest` dropped their id fields
entirely. `TenantManagementService.CreateAsync`/
`SiteManagementService.CreateAsync` dropped the id parameter and (for
Tenant) the existence-check-then-409 path a Guid can't trigger -
`TenantManagementService.CreateAsync` is no longer nullable.
`SiteManagementService.CreateAsync` stays nullable, but only for "the
parent Tenant doesn't exist," not for a SiteId collision anymore.
`TenantsFunction`/`SitesFunction`'s `Create` handlers dropped id
validation/forwarding to match.

**Live-system consequences, handled explicitly, not silently**: this is
the part ADR-053's addendum specifically warned would need care.
1. The real local `Vivnest.Agent`'s `appsettings.json` had
   `Agent:TenantId`/`Agent:SiteId` hand-typed as the literal strings
   `"Sana"`/`"1Fitz"` - flagged directly to the user (heartbeat/event
   writes don't validate against `tblTenants`/`tblSites`, so the agent
   would have kept running regardless, just under a partition with no
   matching Tenant/Site record). Confirmed before touching it: updated
   both the source and build-output `appsettings.json` to the real
   generated `TenantId`/`SiteId` Guids, rebuilt, restarted the real
   agent. Verified live: the very next capture's blob upload path logged
   the new Guid pair, and `tblAgentHeartbeat`/`tblDeviceHeartbeat`
   immediately showed the new `{Guid}|{Guid}` (and `{Guid}|{Guid}|
   {AgentId}`) partition keys for real device rows.
2. `tblApiKeys` turned out to be empty too (the user's manual wipe
   included it, despite ADR-053's original scope explicitly excluding
   it) - so there was no stale `Admin` key scoped to the old `Sana`/
   `1Fitz` strings to reconcile; a fresh key was created scoped to the
   new Guid Tenant/Site instead, since without one the dashboard has no
   way to authenticate at all.
3. One stray `tblAgentHeartbeat` row landed under the old `Sana|1Fitz`
   partition key in the brief window between the config edit and the
   agent restart taking effect - deleted directly via `az storage entity
   delete` once confirmed as the only leftover.

**Verified for real**: backend (`dotnet build`, full solution) clean.
Created a real Tenant (`Name: "Sana"`) and Site (`Name: "1Fitz"`) via
`POST /tenants`/`POST /tenants/{tenantId}/sites` - confirmed both return
generated Guid ids, confirmed `GET` by the new Guid works for both,
confirmed `GET /tenants/Sana` (the old literal string) now 404s. Cross-
checked `tblTenants`/`tblSites` directly - `PartitionKey`/`RowKey`
exactly match the Guids returned by the API. After reconfiguring and
restarting the real agent: confirmed in the browser, logged into the
dashboard with the freshly-created key, that real Agent/Device data
renders correctly (1 Agent, 3 Devices, live status) - the full pipeline
from real device → real agent → real Cloud Functions → real dashboard
works end-to-end under the new Guid-identified Tenant/Site.

**Addendum - the table wipe this ADR builds on**: same session, prior to
this ADR. The user asked to empty every table except `tblApiKeys` and
repopulate a fresh Tenant/Site. First attempt (`wipe_tables.sh`) had a
real bug: Azure CLI emits Windows CRLF line endings in TSV output, and
`read -r PK RK` doesn't strip the trailing `\r`, so every `RowKey` passed
to `az storage entity delete` silently had a corrupted trailing
character - Azure Table Storage rejects control characters in
`PartitionKey`/`RowKey`, so **every single delete failed** (masked by
`>/dev/null 2>&1`, only surfaced as a `deleted 0, failed N` summary line
per table). Fixed by piping the query output through `tr -d '\r'` before
the delete loop. Re-verified against live row counts directly (not the
script's own success log) after rerunning - all 13 target tables
confirmed empty, `tblApiKeys` confirmed untouched with its 8 rows intact
at that point. The user then manually deleted the remaining tables
(including `tblApiKeys`, which the automated wipe had deliberately
excluded) before this ADR's Guid work began.

**Addendum - dashboard topbar regression, found and fixed same session**:
the dashboard header (`App.tsx`) shows `{tenantId} / {siteId}` next to
the logo, sourced from `GET /whoami`. That was fine when those were
caller-chosen strings (`Sana`/`1Fitz`) but turned the header into raw
Guids after this ADR - the exact "id-for-wire, name-for-display" problem
already solved for the API Keys screen (ADR-054), just missed here since
`WhoAmI` had no reason to carry a `Name` before. `WhoAmIResponse` gained
nullable `TenantName`/`SiteName`; `WhoAmIFunction` now looks up
`tblTenants`/`tblSites` via `ITenantStore`/`ISiteStore` after resolving
the calling key's `TenantContext` and returns their `Name` alongside the
existing `TenantId`/`SiteId` - safe to do at the tenant auth tier (not
the operator tier `TenantsFunction`/`SitesFunction` use) since a key can
only ever resolve its own Tenant/Site, so surfacing that Tenant/Site's
own `Name` crosses no boundary the key doesn't already cross by
returning the raw id. Nullable so an orphaned key (Tenant/Site since
deleted) degrades to showing the id instead of erroring.
`Vivnest.Dashboard/src/api.ts`'s `WhoAmI` interface and `App.tsx`'s
`site` state/header render both updated to prefer `tenantName ?? tenantId`
/ `siteName ?? siteId`. Verified for real: `GET /whoami` with the real
key returns `{"tenantName":"Sana","siteName":"1Fitz",...}`; browser-
confirmed the header reads `Sana / 1Fitz` again, not the raw Guids.
`tsc -b`/`oxlint` both clean (dashboard), `dotnet build` clean (backend).
excluded) before this ADR's Guid work began.

## ADR-056 — Machines and Agent Installations dashboard admin screens

**Why:** ADR-053 built the Machine/Agent/AgentInstallation domain model
and backend API (`Machines`/`AgentInstallations`/`AgentRegistry`
Functions) but deliberately left the dashboard untouched. The user asked
directly ("build now") for both a Machines screen (needed as a picker
prerequisite) and an AgentInstallations screen (the actual ask - "do we
need a UI for the AgentInstallation?" was answered "hold off unless
asked," then immediately asked for).

**Machines screen** (`MachinesAdmin.tsx`/`MachineFormModal.tsx`) mirrors
`DeviceTypesAdmin.tsx` almost exactly - list, filter-by-name, Add/Edit -
with one structural difference: no Delete action, because
`MachinesFunction` has no DELETE route at all (ADR-053's explicit "no
hard delete, identity should remain stable" choice). Status is edited
instead, via a dropdown shown only in edit mode (`Active`/`Offline`/
`Retired`/`Decommissioned`).

**Agent Installations screen** (`AgentInstallationsAdmin.tsx`) is not
simple CRUD, unlike every other admin screen so far - `AgentInstallation`
is a lifecycle (Install/Move/Uninstall), not a record with fields to
edit. Chose to show one row per *registered Agent* (from the existing
`AgentRegistry` list) with its current Machine if any, rather than a raw
list of `AgentInstallation` rows, since "which Agent is running where" is
the actual question this screen answers. There is no bulk "active
installation per agent" endpoint, so this fetches one
`GET /agent-installations-admin/active/{agentId}`-equivalent call per
Agent via `Promise.all` - accepted as fine at this scale (a handful of
agents), same reasoning already used for
`DeviceCapabilitiesQueryService`'s own O(N) scan.

**Shared Install/Move modal**: `InstallAgentModal.tsx` is used for both
verbs (`mode: "install" | "move"` prop) rather than two near-identical
components, since the fields (Machine picker, optional ContainerId/
ImageName/ImageVersion) and validation are identical - only the button
label and which backend call fires differ. Machine picker is by Name,
resolved to `MachineId` on submit - same id-for-wire/name-for-display
pattern as the API Keys screen's Tenant/Site picker (ADR-054) and the
WhoAmI topbar fix (ADR-055's addendum).

**`AdminDrawer.tsx`** gained two more real items ("Machines", "Agent
Installations") between "Agents" and the still-placeholder "Services"/
"Automations" rows - no divider needed here since both use the same
tenant `x-api-key` tier as every other item above the API Keys divider.

**Verified for real**, same discipline as every prior ADR this session
(no automated tests, no mocks): `tsc -b`/`oxlint` clean. Browser-tested
against real data - Machines screen loaded the real dummy Machine, Agent
Installations screen loaded the real dummy Agent/Installation pairing,
Uninstall worked, Install worked (after switching from `javascript_tool`
to the `computer` tool's ref-based click - `Element.click()` on the
modal's submit button silently failed to fire the React handler despite
the underlying `<select>` state being correct, even after a hard reload;
ref-based clicking fixed it immediately, confirmed via the network log
showing `POST .../install → 200 OK`), and Move worked (tested against a
second, scratch Machine "Mini PC (garage)" created solely to give Move a
real second destination, then cleaned up afterward - see below).

**Cleanup**: the scratch "Mini PC (garage)" Machine has no DELETE API
(same ADR-053 constraint noted above), so after moving the Agent back to
its original Machine via a direct `POST /agent-installations-admin/move`
call, the empty Machine row itself was removed directly via
`az storage entity delete` against `tblMachines` - the one point in this
ADR's work where the API surface's own limitation required dropping to
direct table access, consistent with how every other hard-delete gap in
this project has been handled so far.

## ADR-057 — Device/DeviceType/Capability domain classes; DeviceCapability introduced

**Why:** Explicit instruction: don't treat Azure Table entities as the
domain model - entities are a persistence representation, the domain
model should express business concepts independently, with the Table
mapping living in the management service (`ToDomain`/`ToEntity`), same
split `Machine`/`MachineEntity`/`MachineManagementService` already
established (ADR-053). `Device`, `DeviceType`, and `Capability` had
entities, DTOs, and flat CRUD services, but **no domain class at all** -
`DeviceRegistryManagementService`/`DeviceTypeManagementService`/
`CapabilityManagementService` built/mutated their Entity directly, the one
inconsistency this ADR fixes. The actual new capability - what triggered
this phase - is `DeviceCapability`: previously `DeviceRegistryEntity.CapabilityIds`
was a flat comma-separated list of Capability ids with no way to express
"which Agent executes this capability for this device" (`ExecutingAgentId`)
per assignment; only existed today as ad hoc inline fields on the MVP
`DeviceOptions` blob (`SinkCleanlinessRoiOptions.ExecutingAgentId`/
`ObjectDetectionRoiOptions.ExecutingAgentId`), never as a real persisted,
generic, repeatable record.

**Domain layer** (`Vivnest.Core/Domain`, plain classes, no Azure
dependency): `DeviceTypeDefinition` (not `DeviceType` - see the naming
collision note below), `Capability`, `Device`, `DeviceCapability`. All
four follow `Machine.cs`'s established shape: private ctor + validating
public ctor (id generated internally via `Guid.NewGuid()`, same
convention as every other master-list id in this codebase) + `Rehydrate`
static factory + explicit mutation methods (`Update`/`SetStatus`/`Remove`).

- `DeviceTypeDefinition`: `DeviceTypeId`, `Name`, `Description?`, `Status`
  (new `DeviceTypeStatus` enum: `Active`/`Inactive`, mirrors
  `TenantStatus`), `CreatedUtc`/`UpdatedUtc`. Global, not tenant-scoped -
  matches `DeviceTypeEntity`'s existing constant-partition choice.
- `Capability`: `CapabilityId`, `Name`, `CapabilityType` (existing enum).
  Deliberately kept to its *current* field shape - nothing asked this
  master list to grow, so the domain class doesn't invent fields the
  persistence layer has no use for yet (unlike `DeviceTypeDefinition`,
  which genuinely needed new fields per this ADR's own instructions).
- `Device`: `ISiteScoped` (`TenantId`/`SiteId`), `DeviceId`, `Name`,
  `DeviceTypeId`/`OwningAgentId` (string refs, no FK validation, same
  convention as everywhere else), `Location`/`Brand`/`Model`/`Firmware`,
  `Enabled`, `Settings`. **Drops `CapabilityIds`** - superseded by real
  `DeviceCapability` rows; kept in parallel would mean two competing
  sources of truth for the same fact, the opposite of the requested
  "smallest necessary change."
- `DeviceCapability`: the actual new domain concept. `ISiteScoped`, own
  `DeviceCapabilityId`, `DeviceId`/`CapabilityId` refs, `ExecutingAgentId`
  (empty = unset, no FK validation - the field this whole ADR exists
  for), `Enabled`, `Settings` (free-form map, same reasoning as
  `DeviceRegistryEntity.Settings` - houses future ROI-style params per
  assignment without new columns), `DeviceCapabilityStatus`
  (`Active`/`Removed`, new enum). Modeled after `AgentInstallation`, not
  the flat master lists - an assignment is a lifecycle (Assign/Unassign),
  not reference data, so it soft-removes (`Remove()`) rather than hard-
  deleting, preserving assignment history the same way Install/Move/
  Uninstall preserves installation history.

**A real naming collision, found and fixed during implementation**: the
domain class was initially named `DeviceType` (matching the spec's own
illustrative snippet) - this compiled to a genuine C# ambiguous-reference
error (`CS0104`) in *every* file with both `using Vivnest.Core.Domain;`
and `using Vivnest.Core.Enums;` in scope, since `Vivnest.Core.Enums.DeviceType`
(the fixed classification enum, ADR-047's own "two unrelated concepts,
same English word" split) already occupies that name. It silently broke
`Vivnest.Infrastructure/DataStores/Helpers/DeviceHeartbeatMapping.cs` -
unrelated existing code on the real heartbeat write path, not just the
new file - confirmed by a full solution build before touching anything
else. Renamed the domain class to `DeviceTypeDefinition` to resolve it;
`DeviceTypeEntity`/`DeviceTypeAdminDto`/`IDeviceTypeManagementService`
keep their existing `DeviceType*` names since those never collided.

**Application layer** (`Vivnest.Cloud/Admin`): `DeviceRegistryManagementService`
renamed to `DeviceService` (interface `IDeviceService`) - now builds/mutates
`Device` domain objects via `ToDomain`/`ToEntity` instead of touching
`DeviceRegistryEntity` directly. The underlying table/entity
(`tblDeviceRegistry`/`DeviceRegistryEntity`) **keeps its existing name** -
persistence naming is a repository concern, independent of this rename,
same precedent set by declining to rename `tblAgentRegistry` earlier this
session. `DeviceTypeManagementService`/`CapabilityManagementService` keep
their names (no renaming precedent to break) but now route through
`DeviceTypeDefinition`/`Capability` internally. New
`ICapabilityAssignmentService`/`CapabilityAssignmentService` owns the
`DeviceCapability` lifecycle (`AssignAsync`/`UpdateAssignmentAsync`/
`UnassignAsync`/`ListByDeviceAsync`) - validates Device and Capability
exist first (same pattern `AgentInstallationManagementService` already
established for Agent/Machine), enforces "at most one active assignment
per (Device, Capability) pair" the same way `AgentInstallationManagementService.InstallAsync`
enforces "at most one active installation per Agent."

**Persistence layer**: `DeviceTypeEntity` gains `Description`/`Status`/
`CreatedUtc`/`UpdatedUtc` - additive, backward-compatible (old rows
deserialize the new fields to defaults; nothing in the MVP runtime path
reads this entity at all, confirmed by grep before touching it).
`DeviceRegistryEntity` drops `CapabilityIds`. `CapabilityEntity`
unchanged. New `DeviceCapabilityEntity` (`tblDeviceCapabilities`,
tenant-scoped, `PartitionKey = TenantId|SiteId`, `RowKey = DeviceCapabilityId`)
+ `IDeviceCapabilityStore`/`AzureTableDeviceCapabilityStore` - mirrors
`AgentInstallationEntity`/`AzureTableAgentInstallationStore` exactly
(same problem shape, same solution, including the `GetActiveBy*` query
pattern for the "at most one active" invariant).

**Function layer**: `DeviceTypesAdminFunction`/`DeviceRegistryAdminFunction`
updated to match (Create/Update now pass `Description`/`Status`; Create/
Update drop `CapabilityIds` wiring). New `DeviceCapabilitiesAdminFunction`
(`device-capabilities-admin/assign|unassign`, `GET .../by-device/{deviceId}`,
`PUT .../{deviceCapabilityId}`) - Assign/Unassign are POST lifecycle
actions, not plain CRUD, same shape `AgentInstallationsFunction` already
established for Install/Move/Uninstall.

**Dashboard fix, required not optional**: dropping `CapabilityIds` from
`DeviceRegistryDto` would have crashed the existing Devices admin screen
(`DeviceRegistryAdmin.tsx`'s `d.capabilityIds.length` on `undefined`) and
silently orphaned its Capabilities checklist (`DeviceRegistryFormModal.tsx`) -
this is real, working dashboard code from ADR-048, not disposable scratch
UI, so leaving it broken would have violated "preserve existing MVP
behaviour" even though the checklist itself isn't MVP runtime code.
Removed the checklist and `capabilityIds` field from both files and
`api.ts`'s `DeviceRegistry`/`DeviceRegistryFields` types - assigning
capabilities to a device is `CapabilityAssignmentService`'s job now, with
no dashboard UI for it in this phase (same "backend first, UI later"
sequencing `AgentInstallation` followed in ADR-053/056). Separately,
`UpdateDeviceTypeRequest.Status` being a required field (matching
`MachineFormModal`'s own precedent) would have made every existing
`updateDeviceType` call 400 - fixed by adding a Status dropdown to
`DeviceTypeFormModal.tsx` (shown only when editing, exactly mirroring
`MachineFormModal.tsx`) and threading `description`/`status` through
`DeviceTypesAdmin.tsx`/`api.ts`.

**Verified for real**, same discipline as every prior ADR (no automated
tests, no mocks): `dotnet build` clean after fixing the naming collision.
Real curl round-trip against `func start`: DeviceType create (with
Description) → update (Status: Inactive) → update missing Status (400,
confirms the required-field validation) → Device create (confirmed no
`capabilityIds` in the response) → Capability create → Assign (with
`ExecutingAgentId`, real `Settings`) → duplicate assign on the same
(Device, Capability) pair (409, confirms the invariant) → assign against
a nonexistent CapabilityId (409) → list by device → Unassign (200,
`Status: Removed`) → Unassign again (404, confirms nothing double-fires)
→ re-assign after removal (200, new `DeviceCapabilityId`, confirms
history is preserved as separate rows, not mutated in place) → no API
key (401). Cross-checked `tblDeviceCapabilities` directly via
`az storage entity query` - both the `Removed` and `Active` rows exist
exactly as the API responses claimed. All scratch test records (Device,
Capability, DeviceType, both DeviceCapability rows) cleaned up afterward
- the two `DeviceCapability` rows via direct `az storage entity delete`
(no DELETE endpoint exists for it, deliberately, same "lifecycle record,
not disposable reference data" reasoning as `AgentInstallation`), the
rest via their real DELETE endpoints. `tsc -b`/`oxlint` clean. Browser-
verified against `http://localhost:5173` with real data: Device Types
screen - create with Description, edit shows the Status dropdown, Status
change persists and re-renders; Devices screen - create/list/delete work
with no checklist and no console crash (confirmed no stray `TypeError`
in the console after a forced reload, ruling out the
`capabilityIds.length`-on-`undefined` failure mode this ADR's dashboard
fix was written to prevent).

**Addendum - `Agent` domain class, same session**: this ADR's own layering
(Domain → Application → Persistence) listed `Agent` alongside `Device`/
`DeviceType`/`Capability`, but the initial pass deliberately skipped
extracting it - nothing being built required it, and it was flagged as a
scoped-out decision, not an oversight. Asked directly afterward whether
`AgentRegistryEntity` already *was* the `Agent` domain class (it wasn't -
it's the persistence entity `AgentRegistryManagementService` built/mutated
directly, same pre-ADR-057 shape `DeviceRegistryEntity` used to be), then
asked for it to be extracted for real symmetry with the diagram.

New `Vivnest.Core/Domain/Agent.cs` - same shape as every other domain
class this ADR introduced (private ctor + validating ctor with an
internally-generated Guid `AgentId` + `Rehydrate` + `Update`/`SetStatus`).
`CapabilityIds` kept as `IReadOnlyList<string>` (not re-modeled as a real
join) - this is the Agent's *own* declared capabilities (ADR-046),
unrelated to `DeviceCapability.ExecutingAgentId` (this ADR's actual new
concept, a capability *assignment* pointing at an Agent) - nothing asked
Agent's capability declaration to change, so it didn't.
`AgentRegistryManagementService` rewritten to route through `ToDomain`/
`ToEntity`/`ToDto`, same pattern as `DeviceService`/`MachineManagementService`.
`AgentRegistryEntity`/`AgentRegistryDto`/`IAgentRegistryManagementService`/
`tblAgentRegistry` all keep their existing names - same "table naming is
a repository concern, independent of the domain rename" reasoning as
`DeviceService`.

Two real backward-compatibility quirks already documented on
`AgentRegistryEntity` (rows that predate ADR-053 have a blank `Status`
and a `default(DateTime)` `CreatedUtc`, the latter rejected by the Azure
Table SDK on write since it deserializes as `Kind.Unspecified`) had to be
preserved exactly, not simplified away, since real rows depend on them:
blank `Status` resolves to `AgentStatus.Active` in `ToDomain`, and
`ToEntity` still backfills a `default` `CreatedUtc` with `UpdatedUtc` and
`SpecifyKind`s a real one to `Utc` before every write.

**Verified for real**: `dotnet build` clean. Restarted `func start`,
confirmed the two real pre-existing agents ("Dummy 2 Agent", "Living Room
Capture Agent") still list correctly through the new domain mapping -
the actual test of the backward-compat quirks above, since both rows
predate this change. Real create → update (`Status: Inactive`,
`FirmwareVersion`/`Type` change) → delete round-trip, confirmed via a
second `GET` that only the two original real agents remain. Separately
verified `CapabilityIds` still round-trips a real Guid through the new
`IReadOnlyList<string>` internal representation (create with one id →
response echoes it back → cleaned up).

## ADR-058 — Device lifecycle, Tenant/Site FK validation, Device Registry query filters

**Why:** Direct follow-up to ADR-057, working through a fuller "Phase 3 —
Device Domain Foundation" spec covering identity/lifecycle, Device↔Agent
authorization, capability-assignment validation, configuration/runtime-
state separation (already satisfied by ADR-057 - `DeviceCapability.Settings`
already holds capability-specific config, `Device` already holds only
connection facts), a Device Registry query surface, and runtime
integration (DeviceEvent/DeviceHeartbeat). Confirmed against the actual
code (not assumed) before building anything: several gaps were real, not
speculative -`DeviceService.DeleteAsync` was a genuine hard delete
(the opposite of "don't let a retired device disappear"), and neither
`OwningAgentId` nor `ExecutingAgentId` were validated against the calling
tenant/site at all.

**Testing decision, asked directly before implementing**: CLAUDE.md marks
"no automated test project" as a deliberate standing decision, verified
instead via real curl + real Azure Table Storage + browser for every ADR
so far. Explicitly asked whether to reverse that now; answered "keep
real-infra verification" - so this ADR is verified the same way as every
other one, no new test project.

**A. Device lifecycle** (`Vivnest.Core/Enums/DeviceStatus.cs`: `Active`/
`Disabled`/`Retired`) - replaces `Device.Enabled`/`DeviceRegistryEntity.Enabled`
entirely, not added alongside it (a bool and a three-state enum
overlapping would leave "Enabled: false" and "Status: Disabled"
ambiguous). `DeviceService.DeleteAsync` and the `DELETE
devices-registry-admin/{deviceId}` route are both **removed** - a
Device's identity must remain stable (historical `DeviceCapability`
assignments/`DeviceEvent`s may still reference its `DeviceId`), so
retiring one is `PUT .../{deviceId}` with `Status: Retired`, never a
hard delete - same reasoning `MachineStatus`/no-`DELETE`-route already
established for Machine (ADR-053). `CreateDeviceRegistryRequest` doesn't
accept `Status` - a new Device always starts `Active` server-side, same
as Machine.

**B. Device ↔ Agent Tenant/Site validation** - `DeviceService.CreateAsync`/
`UpdateAsync` now call `IsValidOwningAgentAsync` (new, private, uses the
already-injected `IAgentRegistryStore`) before writing: a non-empty
`OwningAgentId` must resolve to a real Agent in the *same* Tenant/Site as
the calling `TenantContext`, or the call returns `null`. Same authorization
boundary added to `CapabilityAssignmentService.AssignAsync`/
`UpdateAssignmentAsync` for `ExecutingAgentId`. Empty stays valid ("not
assigned yet," same convention as everywhere else) - only a *non-empty*
value that doesn't resolve is rejected. HTTP mapping follows existing
precedent rather than inventing a new code: Device's "doesn't exist" case
is 400 (matching `AgentInstallationsFunction.MoveAgent`'s existing
"doesn't exist" convention - no conflict semantics apply here), Capability
assignment's case stays 409 (folded into `AssignAsync`'s existing combined
"Device or Capability or already-assigned" message, since that already
established the collapsed-reasons pattern). `UpdateAsync`/
`UpdateAssignmentAsync` deliberately collapse "target doesn't exist" and
"reference invalid" into one outcome rather than threading a second error
channel through - same simplification `MoveAgent` already uses, and no
caller (dashboard or otherwise) distinguishes the two today.

**C. Device Registry query filters** - `IDeviceService.ListAsync` gained
optional `ownerAgentId`/`deviceTypeId` params, filtered in `DeviceService`
after the existing tenant/site-scoped fetch (in-memory filter, same "fine
at this project's actual scale" reasoning used everywhere else in this
codebase, e.g. `DeviceCapabilitiesQueryService`'s O(N) blob scan) -
answers "devices owned by this agent"/"devices of this type" on top of
the site-scoping every List already had. Exposed as `?ownerAgentId=`/
`?deviceTypeId=` query params on `GET devices-registry-admin`. **Not**
built as a separate `DeviceRegistry` class - the spec itself left this
open ("implement through existing services if it'd duplicate
responsibilities"), and a new class here would only wrap
`DeviceService`/`CapabilityAssignmentService`/`IAgentRegistryStore`
calls that already exist, so it was skipped.

**D. DeviceId reconciliation - explicitly a boundary, not a gap left
unaddressed.** The admin `Device.DeviceId` (a generated Guid from `POST
devices-registry-admin`) and the `DeviceId` `tblDeviceEvents`/
`tblDeviceHeartbeat` actually key on (whatever's hand-authored in a real
device's `device-config/*.json` blob) are **two unrelated identity
spaces** - nothing links an admin-registered Device to a real device's
blob identity, and nothing in this ADR changes that. This isn't a
regression introduced here - `DeviceRegistryEntity`'s own comment has
said "registering a device here does not configure a real device" since
ADR-048. Verified by grep that no code path assumes the two DeviceIds
are ever the same value, so there's no live conflict today - only a
future reconciliation question (making the admin Device model an actual
source of truth the blob config projects from) that this ADR deliberately
leaves for its own dedicated phase, consistent with this project's
"gradual evolution" / "second real consumer" principle (CLAUDE.md) - not
something to fold into a CRUD-validation pass.

**Not built, staying consistent with the spec's own "prepare for, don't
build yet" framing**: a Capability compatibility matrix (DeviceType →
allowed Capabilities) - explicitly deferred to a future phase by the spec
itself. Per-capability configuration schema classes - `DeviceCapability.Settings`
(ADR-057) is already the unblocked placeholder; no schema types were
needed to satisfy this ADR's scope.

**Verified for real**: `dotnet build` clean, `tsc -b`/`oxlint` clean.
Real curl round-trip against `func start`: Device create with an invalid
`OwningAgentId` (400) → create with a real one (200, `Status: Active`) →
update `Status: Disabled` → update `Status: Retired` → update with an
invalid `OwningAgentId` (400) → update with an invalid `Status` string
(400) → `DELETE` on the same route (404, confirms the route is gone) →
list filtered by the real `ownerAgentId` (includes it) → list filtered by
a different real agent's id (excludes it) → DeviceCapability assign with
an invalid `ExecutingAgentId` (409) → assign with a real one (200).
Cross-checked `tblDeviceRegistry` directly via `az storage entity query` -
`Status: Retired` persisted exactly as the API claimed. All scratch
records cleaned up afterward, Device/DeviceCapability rows via direct
`az storage entity delete` (no DELETE route for either, by design),
Capability via its real DELETE endpoint. Browser-verified against
`http://localhost:5173` with real data: Add form shows no Status field
(Create always starts Active); Edit form shows the Status dropdown
(Active/Disabled/Retired) and no Delete button (confirmed only one
`.icon-button` per row); setting Status to Retired via the UI persisted
and re-rendered correctly; no stray `TypeError` in the console after a
forced reload.

## ADR-059 — AgentCapability ("Phase 4"): Agent capability manifest, DeviceCapability execution validation

**Why:** Direct follow-up to ADR-057/058, closing the gap those ADRs
explicitly named but didn't fill: `DeviceCapability.ExecutingAgentId`
only ever checked that the referenced Agent *exists* in the tenant/site
(ADR-058) - it never checked that Agent can actually *run* the capability
being assigned. Proposed as its own domain concept -
`Capability → { DeviceCapability, AgentCapability } → { Device, Agent }`,
converging on `ExecutingAgentId` as the enforcement point. This is also
the natural conclusion of the "should we remove `AgentRegistryEntity.CapabilityIds`?"
question from earlier the same session: the answer at the time was "keep
it, `DeviceCapability` and it represent different concepts" (declared
manifest vs. per-device assignment) - but once `AgentCapability` exists as
a real join *for* that declared-manifest concept, the flat `CapabilityIds`
list is superseded the same way `Device.CapabilityIds` was in ADR-057,
so it's removed too, not kept in parallel.

**Domain** (`Vivnest.Core/Domain/AgentCapability.cs`) - "this Agent has
the ability to execute Capability X," independent of any device. Same
shape as every domain class this session (private ctor + validating ctor
with an internally-generated Guid `AgentCapabilityId` + `Rehydrate` +
`Remove()`), modeled after `DeviceCapability` - a declaration is a
lifecycle (Assign/Unassign), so it soft-removes rather than hard-deletes.
Deliberately has no `Settings`/`Enabled` the way `DeviceCapability` does -
nothing about "can this Agent run X" needs per-declaration configuration
or a separate on/off switch; `Status` (`Active`/`Removed`, new
`AgentCapabilityStatus` enum) covers it alone.

**`Agent.CapabilityIds` removed** (`Vivnest.Core/Domain/Agent.cs`,
`AgentRegistryEntity.CapabilityIds`, `AgentRegistryDto.CapabilityIds`,
`Create`/`UpdateAgentRegistryRequest.CapabilityIds`) - superseded by
`AgentCapability`. `AgentRegistryManagementService`/
`AgentRegistryAdminFunction` updated to match. Dashboard fix, required
not optional (same reasoning ADR-057's own dashboard fix documents):
`AgentRegistryFormModal.tsx`'s Capabilities checklist read/wrote the now-
removed field, so it was removed rather than left silently broken;
`AgentRegistryAdmin.tsx` no longer fetches Capabilities or renders
capability badges. No dashboard UI for Agent capability declaration in
this phase either - same "backend first" sequencing every other
lifecycle feature this session followed.

**Persistence**: new `AgentCapabilityEntity`/`tblAgentCapabilities`
(`PartitionKey = "{TenantId}|{SiteId}"`, `RowKey = AgentCapabilityId`) +
`IAgentCapabilityStore`/`AzureTableAgentCapabilityStore`, mirroring
`DeviceCapabilityEntity`/`AzureTableDeviceCapabilityStore` exactly
(including the `GetActiveByAgentAndCapabilityAsync` query for the "at
most one active declaration per (Agent, Capability) pair" invariant).

**Application**: new `IAgentCapabilityAssignmentService`/
`AgentCapabilityAssignmentService` (`AssignAsync`/`UnassignAsync`/
`ListByAgentAsync` - no `UpdateAssignmentAsync`, since a declaration has
no mutable fields to change besides its own lifecycle) - validates Agent
and Capability exist first, same pattern `CapabilityAssignmentService`
already established.

**The actual validation rule this ADR exists to add** -
`CapabilityAssignmentService.IsValidExecutingAgentAsync` (now taking
`capabilityId` as well as `executingAgentId`) - a non-empty
`ExecutingAgentId` must both (ADR-058) resolve to a real Agent in this
tenant/site AND (ADR-059) have an active `AgentCapability` declaration
for the *exact* `CapabilityId` being assigned - checked via
`IAgentCapabilityStore.GetActiveByAgentAndCapabilityAsync`, newly injected
into `CapabilityAssignmentService`. Both `AssignAsync` and
`UpdateAssignmentAsync` call it; `AssignAsync` folds a failure into its
existing 409 combined message (now covering "doesn't exist, doesn't
declare this capability, or already assigned"), same collapsed-reasons
convention as before.

**Function layer**: new `AgentCapabilitiesAdminFunction`
(`agent-capabilities-admin/assign`, `agent-capabilities-admin/unassign`,
`GET agent-capabilities-admin/by-agent/{agentId}`) - Assign/Unassign are
POST lifecycle actions, same shape `DeviceCapabilitiesAdminFunction`
already established.

**Verified for real**: `dotnet build` clean, `tsc -b`/`oxlint` clean. Full
real curl chain against `func start`, proving the actual enforcement, not
just that the endpoints respond: confirmed the two real pre-existing
agents still list correctly with no `capabilityIds` field → created a
real Device (`OwningAgentId` = a real agent) and Capability → attempted
`DeviceCapability` assign with that agent as `ExecutingAgentId` **before**
declaring the capability - rejected (409) → declared `AgentCapability`
(Agent, Capability) - 200 → duplicate declare - 409 (invariant holds) →
listed by agent - confirms it → **retried the exact same DeviceCapability
assign - now succeeds (200)**, proving the check is live, not
coincidental → unassigned the DeviceCapability, then unassigned the
`AgentCapability` (200) → unassign again - 404 → **retried the
DeviceCapability assign a third time - rejected again (409)**, proving
the validation re-checks on every call rather than caching a stale
result → no API key on `agent-capabilities-admin` - 401. Cross-checked
`tblAgentCapabilities` directly via `az storage entity query` -
`Status: Removed` persisted exactly as the API claimed. All scratch
records cleaned up (Device/DeviceCapability/AgentCapability rows via
direct `az storage entity delete`, Capability via its real DELETE
endpoint) - left one genuinely pre-existing real Device row
("Kitchen Camera") untouched, confirmed it predates this session's test
data and isn't something this ADR's cleanup owns. Browser-verified
against `http://localhost:5173` with real data: Agents admin screen
loads with no capability badges and no console crash; Edit form shows
no Capabilities checklist.

## ADR-060 — Capability assignment UI (completing "Phase 4")

**Why:** ADR-059 built the full `AgentCapability` model and the real
`ExecutingAgentId` validation rule, but with no way to exercise either
from the dashboard - every verification was curl. Explicit ask: give
Phase 4 a real UI, on the reasoning that a model that only "technically
works" via curl can still be awkward to administer in practice, and
building the UI is itself a validation of the model (it exposed nothing
new here, but was treated as the actual acceptance test for ADR-059).

**Shape, decided before writing code**: every existing Admin screen
(Capabilities/Device Types/Devices/Agents/Machines) is a flat list +
Add/Edit modal - there's no drill-down "detail page" anywhere in Admin.
Asked directly whether to introduce one (matching the literal mockups:
Agent/Device detail pages with Overview/Installation/Capabilities/Activity
sub-tabs) or fit capability management into the existing list+modal
shape. Chose the latter - a new "Manage Capabilities" icon-button per
Agent/Device row opens a modal scoped to that one entity. This exercises
the domain model exactly as much as a full detail-page rewrite would,
without introducing a new page-routing/navigation concept to Admin that
nothing else needs yet.

**Deliberately NOT symmetrical, per explicit instruction**: `AgentCapabilitiesModal.tsx`
and `DeviceCapabilitiesModal.tsx` are two separate components, not one
generic "Capability assignment" screen parameterized by entity type -
the two sides genuinely differ in shape. `AgentCapabilitiesModal` is a
plain list + Add/Remove (a declaration has nothing beyond its own
lifecycle - no `ExecutingAgent`, no `Enabled`, no per-assignment config).
`DeviceCapabilitiesModal` is the richer of the two: each row shows
Capability, an `Enabled`/`Disabled` status badge that's itself a button
(clicking toggles it via `updateDeviceCapabilityAssignment`), and
"Executed by: {Agent name}"; the Add form has two dependent dropdowns
(Capability, then Executing Agent) plus an Enabled checkbox. No separate
`Admin > Capability Assignments` cross-cutting screen was built either -
the Agent/Device rows are the primary assignment points, matching the
instruction not to centralize this the way `Admin > Capabilities`
centralizes capability *definitions*.

**The Executing Agent filter - the one genuinely new piece of logic,
not just UI plumbing for existing endpoints**: `DeviceCapabilitiesModal`'s
Add form's Executing Agent dropdown only lists Agents that have actually
declared the selected Capability via `AgentCapability` - fetched via
`Promise.all(agents.map(a => getAgentCapabilities(apiKey, a.agentId)))`
when the modal opens (same O(N)-at-this-scale reasoning
`AgentInstallationsAdmin.tsx` already established for its own per-agent
`Promise.all`), building an `agentId -> Set<capabilityId>` map client-side
- there's no server-side "which agents support capability X" endpoint,
so this is computed, not queried. This is the dashboard surfacing the
exact same rule `CapabilityAssignmentService.IsValidExecutingAgentAsync`
already enforces server-side (ADR-059) - the UI can't offer an agent that
would be rejected anyway, same "the picker only shows what would actually
work" reasoning `InstallAgentModal`'s Machine picker already uses.

**API layer** (`Vivnest.Dashboard/src/api.ts`) - new `AgentCapability`
type + `getAgentCapabilities`/`assignAgentCapability`/`unassignAgentCapability`.
New `DeviceCapabilityAssignment` type (not `DeviceCapability` - that name
is already taken by the unrelated, read-only live-monitoring
Capabilities-tab type sourced from the MVP config blob) +
`getDeviceCapabilityAssignments`/`assignDeviceCapability`/
`updateDeviceCapabilityAssignment`/`unassignDeviceCapability`. All six
use `request<T>()` (every one of these endpoints returns 200 with a JSON
body, never 204, unlike the DELETE-based endpoints elsewhere that needed
their own raw-`fetch` handling).

**New icon**: `PuzzleIcon` (`icons.tsx`) - a real Tabler-style outline
icon, not a repurposed existing one, since "manage capabilities" is a
distinct action from Edit/Delete on both `AgentRegistryAdmin.tsx` and
`DeviceRegistryAdmin.tsx`'s rows.

**Verified for real**: `tsc -b`/`oxlint` clean (no backend changes this
ADR - ADR-059's backend was already correct and unchanged). Browser-
verified end-to-end against `http://localhost:5173` and real Azure
data, confirming the network calls at every step (not just that the UI
rendered): opened `AgentCapabilitiesModal` for the real "Living Room
Capture Agent" → assigned "Motion Detection" (`POST assign → 200`) →
opened `DeviceCapabilitiesModal` for the real "Kitchen Camera" → selected
"Motion Detection" in the Add form → confirmed the Executing Agent
dropdown showed **only** "Living Room Capture Agent" ("Dummy 2 Agent AI"
correctly excluded, since it has no `AgentCapability` for Motion
Detection) → assigned it (`POST assign → 200`) → toggled the status
badge to Disabled (`PUT → 200`) → removed it (confirmed empty list, no
console `TypeError`) → cleaned up the `AgentCapability` declaration too.
Cross-checked both `tblAgentCapabilities`/`tblDeviceCapabilities`
directly via `az storage entity query` mid-test, confirming the UI's
state matched the real table exactly at each step. Note on tooling: the
`computer` tool's ref-based click failed to fire the React handlers on
both modals' buttons (opened nothing, no network call) - `javascript_tool`
dispatching a real `click()`/`change` event worked immediately every
time; this reverses which method was more reliable earlier in this
session (ADR-053's own verification found the opposite) - the lesson
holds as before: try the other method if one fails, neither is
universally reliable in this dashboard.

## ADR-061 — Rename `CapabilityType`: BuiltIn/Derived/System → Device/Service/System

**Why:** Reviewing the real `tblCapabilities` data (4 rows, all hand-
classified through the ADR-060 UI) surfaced a genuine inconsistency:
"Motion Detection" had been classified `Derived`, while
`DeviceCapabilitiesQueryService.BuildCapabilitiesAsync` - the one place
in the codebase with a real, working Built-in/Derived/System rule -
treats Motion Detection as `Built-in` (it's the sensor's own native
event, not a value computed from another capability's output). Asked
directly what the classification rule actually was; "how is computed"
turned out to be ambiguous in practice (a sensor's firmware-level event
vs. the logical event it produces can be argued either way), whereas
"who provides it" is unambiguous. Confirmed via grep beforehand that
`CapabilityType` has zero behavioral consequence anywhere in the backend
(validated on write, rendered as a badge, never branched on) - a pure
rename, not a semantics change to any decision logic.

**New meaning, same three-way cardinality**: `Device` = the device
itself provides it (Image Capture, Motion Detection, Power Monitoring).
`Service` = a separate process computes it from something else the
device produced (Object Detection, Image Classification). `System` =
platform-level, not the device or a service (Health Monitoring) -
unchanged.

**Deliberately untouched**: `CapabilitiesTab.tsx`'s own
`CAPABILITY_GROUPS = ["Built-in", "Derived", "System"]` and the matching
`Source` string constants in `DeviceCapabilitiesQueryService.BuildCapabilitiesAsync`
are a separate, unrelated vocabulary - the live per-device Capabilities
tab's grouping, sourced from the MVP device-config blob, with its own
established (and, per above, actually correct) Built-in/Derived/System
rule. This ADR renames only the Admin > Capabilities master-list
classification (`Vivnest.Core.Enums.CapabilityType`, `Capability.CapabilityType`,
`CapabilityAdminDto.CapabilityType`); unifying the two vocabularies is
still out of scope, same as ADR-042 originally noted.

**Changed**: `Vivnest.Core/Enums/CapabilityType.cs` - enum members
`BuiltIn`/`Derived`/`System` → `Device`/`Service`/`System`.
`CapabilitiesAdminFunction.cs` - both `CapabilityType must be one of: ...`
400 messages updated to name the new values. `CapabilityAdminDto.cs` -
doc comment only (the DTOs were always plain `string CapabilityType`, no
code change needed). Dashboard: `api.ts`'s `CapabilityType` union type,
`CapabilitiesAdmin.tsx`'s `TYPE_LABELS`/`TYPE_STATUS_CLASS` maps,
`CapabilityFormModal.tsx`'s `TYPE_OPTIONS` and default state - all
renamed identically, `CapabilitiesTab.tsx` untouched per above.

**Real data migrated, not left to break**: the 4 existing `tblCapabilities`
rows (`Image Capture`, `Motion Detection`, `Image Classification`,
`Object Detection`) had their `CapabilityType` string values updated via
direct `az storage entity merge` (`BuiltIn`→`Device`,
`Derived`→`Service`) *before* restarting `func start` - `CapabilityManagementService`
does `Enum.Parse<CapabilityType>` on read with no fallback, so a stale
row would have made `ListCapabilities` throw on the very next dashboard
load. Explicitly authorized: "update the tables data if you require."

**Verified for real**: backend rebuilt clean. `GET capabilities-admin`
against real Azure data confirmed all 4 rows now return the new type
strings (`Image Capture: Device`; the other 3: `Service`). Negative
check: `POST` with the old `CapabilityType: "BuiltIn"` now correctly
returns 400. Dashboard `tsc -b`/`vite build`/`oxlint` clean (lint's
remaining warnings are pre-existing and unrelated). Browser-verified
against `http://localhost:5173`: Capabilities admin list renders
`Device`/`Service` badges correctly for all 4 real rows; the Add form's
Type dropdown offers `Device`/`Service`/`System` with `Device` as
default.

## ADR-062 — Phase 5: Capability configuration, dependencies & compatibility

**Why:** ADR-057 explicitly deferred two things "to a future phase by the
spec itself": a DeviceType→Capability compatibility matrix, and a real
configuration schema (`DeviceCapability.Settings` was an untyped string
map with nothing validating it). Phase 4 (ADR-057/059/060) built
`Capability`/`AgentCapability`/`DeviceCapability` as bare join records - a
capability could be assigned to a Device and executed by an Agent, but
the model had no opinion on *whether that assignment should be allowed*
beyond "does the Agent declare this exact capability." This ADR is that
deferred future phase, given as a full 50-section spec directly by the
user. Followed the session's now-standard process for a change this
size: inspected the current Phase 4 code first, wrote a plan to
`.claude/plans/`, asked two clarifying questions (testing approach,
whether to include the Admin UI in this pass), got explicit plan
approval, then implemented.

**Two explicit questions asked before implementing, both answered
"Recommended":** (1) Testing approach - **kept real-infra verification**
(curl + real Azure + browser), consistent with the standing CLAUDE.md
decision and every prior phase, even though this spec's own §47
("Testing") asks for rule-level unit tests more insistently than any
prior phase (circular-dependency detection, schema validation). (2) UI
scope - **backend + Admin UI together** in one pass, matching Phase 4's
precedent, since the new rules aren't actually exercisable without a UI
to drive them.

**Two places this ADR reads the spec's own illustrative detail as
non-binding, per the spec's own top-line instruction "do not redesign
Phase 3 or silently change the Tenant/Site partitioning model":**
1. Spec §33's row-key example shows `tblCapabilities` with
   `PartitionKey = TenantId|SiteId`. That's not what's actually there -
   `CapabilityEntity` is deliberately global (constant partition key,
   ADR-042/057). This ADR keeps `Capability`/`DeviceType` global exactly
   as they are, and makes the two *new* relationship tables
   (`CapabilityDependency`, `DeviceTypeCapability`) global too, since a
   dependency or compatibility fact is a property of two pieces of
   shared reference data, not of any one tenant.
2. Spec §19 says "prefer `CapabilityStatus = Retired`" over deleting a
   referenced Capability. `DeviceCapability`/`AgentCapability` references
   are tenant-scoped; checking every tenant's rows before allowing a
   global Capability delete would be a new kind of cross-tenant scan this
   codebase has never done anywhere. `CapabilityManagementService.DeleteAsync`
   checks against the two *new global* tables only (cheap single-partition
   scans) and leaves tenant-owned references unvalidated - same "no FK
   validation on this id, by deliberate long-standing convention"
   boundary every other cross-entity id in this codebase already has.

**Domain** (`Vivnest.Core/Domain`): `Capability` extended in place with
`Status` (new `CapabilityStatus`: `Active`/`Retired`), `ConfigurationSchema`
(`IReadOnlyList<CapabilityConfigurationField>`), `ConfigurationSchemaVersion`
(int, informational only - no migration engine, per spec §9), and
`DefaultConfiguration` (`IReadOnlyDictionary<string,string>`, same shape
as `DeviceCapability.Settings`). All additive - old rows deserialize as
empty schema/version 1/empty defaults/blank `Status` treated as `Active`
(same "blank enum -> default" precedent `AgentRegistryManagementService`
already established) - **no data migration needed on the 4 real
`tblCapabilities` rows this time**. New `CapabilityConfigurationField`
value object (not its own entity/table - no independent lifecycle, per
spec §34's "don't create a table just because a relationship exists"):
`Name`, `Type` (new `CapabilityConfigurationFieldType`: `String`/`Number`/
`Boolean`), `Required`, `Minimum`/`Maximum` (Number only), `AllowedValues`
(String only), `DefaultValue` - the "simpler approach" the spec
explicitly permits instead of a third-party JSON Schema library, since it
validates directly against the flat `Dictionary<string,string>` shape
`DeviceCapability.Settings` already uses. New `CapabilityDependency`
(global: `DependencyId`, `CapabilityId`, `DependsOnCapabilityId`,
`DependencyType` - new enum, one member `Required` for now, spec §15
explicitly defers Optional/Alternative) and `DeviceTypeCapability`
(global: `DeviceTypeCapabilityId`, `DeviceTypeId`, `CapabilityId`, no
`Allowed` bool - row existence is the fact, same as `AgentCapability`/
`DeviceCapability`). Both hard-deletable (existence = the fact, no
history worth keeping) - mirrors `Capability`/`DeviceType`'s own
hard-delete convention, not the soft-remove-with-Status convention
`DeviceCapability`/`AgentCapability` use for assignment *lifecycle*.

**Persistence**: `CapabilityEntity` gained `ConfigurationSchema`/
`DefaultConfiguration` (JSON strings, same `System.Text.Json` convention
`CapabilityAssignmentService` already used for `Settings`),
`ConfigurationSchemaVersion`, `Status`. New `CapabilityDependencyEntity`/
`tblCapabilityDependencies` and `DeviceTypeCapabilityEntity`/
`tblDeviceTypeCapabilities` - both global (constant `PartitionKey`),
mirroring `CapabilityEntity`'s shape exactly. New
`ICapabilityDependencyStore`/`AzureTableCapabilityDependencyStore`,
`IDeviceTypeCapabilityStore`/`AzureTableDeviceTypeCapabilityStore`.

**Application services** (`Vivnest.Cloud/Admin`): new
`CapabilityConfigurationService` (pure logic, no store) -
`ApplyDefaults(capability, suppliedSettings)` merges supplied values over
`Capability.DefaultConfiguration` then each field's own `DefaultValue`;
`Validate(capability, settings, out errors)` checks required-missing,
wrong type, out-of-range (Number `Minimum`/`Maximum`), not-in-
`AllowedValues` (String). New `CapabilityDependencyService` -
`AddAsync` validates both capabilities exist, rejects self-reference and
duplicate edges, and runs a **cycle check**: BFS the existing global edge
set forward from `DependsOnCapabilityId` - if `CapabilityId` is
reachable, adding the edge would close a cycle, rejected. `RemoveAsync`
is a plain existence-check-then-delete - no invariant blocks removing a
dependency, since dependency validation only runs at `DeviceCapability`
assignment time, never continuously enforced (spec §42's explicit
"dependency = validation requirement, not automatic installation"). New
`CapabilityCompatibilityService` - same CRUD shape, existence +
duplicate checks only. `CapabilityManagementService.DeleteAsync` now
checks the two new stores for any reference to this `CapabilityId`
before deleting; if found, rejects with a "retire instead" message
(see the non-binding-detail note above for why only these two stores are
checked).

**`CapabilityAssignmentService` - the central change** (spec §21/44's
"complete assignment algorithm"). `AssignAsync` now runs, in order:
Device exists -> Capability exists -> Device has a `DeviceTypeId` set at
all (if not, rejected - "the system can't answer 'is this valid for this
DeviceType' with no DeviceType") -> Capability compatible with that
DeviceType (`IDeviceTypeCapabilityStore`) -> ExecutingAgent valid +
declares this Capability (already built, ADR-058/059, unchanged) -> at
most one active assignment per (Device, Capability) pair (existing) ->
each **direct** `CapabilityDependency` of this Capability is satisfied by
an active `DeviceCapability` on this same Device (only direct deps
checked - each capability already enforced its own direct deps when *it*
was added, so this doesn't walk transitively) -> supplied `Settings`
merged with `Capability.DefaultConfiguration`/field defaults via
`CapabilityConfigurationService.ApplyDefaults` -> validated via
`.Validate` -> create. `UpdateAssignmentAsync` is deliberately
**narrower** - it only re-validates ExecutingAgent and Settings (not
compatibility/dependencies), since those were already true when the
assignment was first created and don't change from an Update; this also
avoids retroactively breaking the real pre-existing assignments on
"Kitchen Camera" that predate this ADR's compatibility data. Spec §17's
"dependency doesn't require the same Agent" falls out for free - the
dependency check only looks at whether the dependency *DeviceCapability*
is active, never at who executes it.

**Typed result, not bare `null`** - `AssignAsync`/`UpdateAssignmentAsync`
used to return `DeviceCapabilityDto?` with every failure folded into one
combined 409 string. Spec §37 wants distinguishable messages, so
`ICapabilityAssignmentService` now returns `CapabilityAssignmentResult`
(`DeviceCapabilityDto? DeviceCapability, CapabilityAssignmentErrorCode?
Error, string? ErrorMessage`) - the Function layer switches on `Error` to
choose 400/404/409 with the specific message. **The wire format doesn't
change** - still a plain string body via `BadRequestObjectResult`/
`ConflictObjectResult`/`NotFoundResult`, per spec §37's "do not introduce
a new error response format" - only the message content became specific.
`CapabilityManagementService.DeleteAsync` got the analogous
`CapabilityDeleteResult` for the same reason (404 vs 409-referenced).

**New DTOs/routes**: `CapabilityAdminDto` gained `Status`/
`ConfigurationSchema`/`ConfigurationSchemaVersion`/`DefaultConfiguration`;
`Create`/`UpdateCapabilityRequest` gained the same (nullable/optional -
existing callers sending only `CapabilityName`/`CapabilityType` keep
working). New `CapabilityConfigurationFieldDto`. New
`CapabilityDependenciesAdminFunction`/`DeviceTypeCapabilitiesAdminFunction`
- routes `capability-dependencies-admin`/`device-type-capabilities-admin`
(GET all - tiny global lists, dashboard fetches whole and filters
client-side, same pattern already used for `capabilities`/`agents` in
the Phase 4 modals), `.../add` (POST), `.../{id}` (DELETE). Both follow
`CapabilitiesAdminFunction`'s exact shape (tenant `x-api-key` +
`DevicesOnly` 403, even though the underlying data is global - auth here
is about who may call the admin API, not about the data being
tenant-scoped).

**Dashboard**: `CapabilityFormModal.tsx` gained a Status dropdown
(edit-only, same "shown only when editing" pattern `DeviceRegistryFormModal`
established) and a repeatable Configuration Schema row editor
(Name/Type/Required/Min/Max/AllowedValues/Default, `+ Add field`) -
deliberately **no separate top-level Default Configuration editor**, to
avoid two UI spots meaning almost the same thing; each field's own
Default Value is the only place the UI sets a default (`Capability.DefaultConfiguration`
still exists server-side for direct API use). New `LinkIcon` (a new,
distinct icon for a new distinct action, same reasoning ADR-060 gave for
`PuzzleIcon`) + new `CapabilityRelationshipsModal.tsx` (Dependencies list
+ Compatible Device Types list, each a simple Add/Remove shape like
`AgentCapabilitiesModal`) triggered per-row from `CapabilitiesAdmin.tsx` -
only *direct* dependencies shown, no transitive-chain rendering (spec
§28's explicit minimum bar). `DeviceTypesAdmin.tsx`/
`AgentCapabilitiesModal.tsx`/Agent Detail deliberately **unchanged** -
compatibility is managed from the Capability side only, matching
ADR-060's "not symmetrical" precedent and spec §38's own scope note.

`DeviceCapabilitiesModal.tsx` is where the new rules actually become
visible: the Add form's Capability dropdown is filtered to what's
compatible with the Device's DeviceType (`getDeviceTypeCapabilities`
fetched whole, filtered client-side); selecting a Capability fetches its
direct dependencies and disables Assign with an inline "Requires X to be
enabled first" if any are unmet against the Device's already-active
assignments; a new `ConfigFields` component (shared between Add and a new
per-row "Configure" affordance on already-assigned capabilities) renders
one input per `ConfigurationSchema` field - text/number/checkbox, or a
`<select>` when the field declares `AllowedValues` - pre-filled from
`DefaultValue`.

**Verified for real** against `stvivnestagent2` and the real "Kitchen
Camera" device/"Object Detection" capability: gave Object Detection a
real schema (`model`: String required, `confidenceThreshold`: Number
required 0-1) - round-tripped correctly. Added `ObjectDetection ->
ImageCapture` dependency; the reverse edge and a real 3-node cycle
(`A->B`, `B->C` added, `C->A` attempted) both correctly rejected 409.
Added Camera compatibility for `ImageCapture`/`MotionDetection`/
`ObjectDetection`/`ImageClassification`. Confirmed the compatibility gate
is real by reassigning Object Detection to Kitchen Camera *before* any
compatibility rows existed (409) and *after* (200). Confirmed
configuration validation: `confidenceThreshold: 1.5` -> 400; `model`
omitted -> 200 with `model` filled from its schema default ("default").
Confirmed the dependency gate matches spec §48's exact scenario: unassigned
both ImageCapture and ObjectDetection, attempted ObjectDetection alone ->
409 `Required capability "Image Capture" is not enabled for this
device.`; assigned ImageCapture then ObjectDetection -> both 200.
Confirmed compatibility rejection with a throwaway "Power Control Test"
capability (no Camera compatibility row) -> 409, then deleted it
(no references, succeeded). Confirmed delete-blocked-by-reference on
Object Detection (has a dependency + compatibility row) -> 409 "retire
instead"; retired it (`Status = Retired`) -> 200; restored to `Active`
afterward. Backend `dotnet build` clean; dashboard `tsc -b`/`vite build`/
`oxlint` clean (only pre-existing unrelated warnings). Browser-verified
end-to-end: schema editor pre-fills real field data on Edit; relationships
modal shows "Requires: Image Capture" and "Camera"; Device capability
modal's Add dropdown correctly narrowed to only the one remaining
compatible+unassigned capability; dependency gate correctly blocked
Assign with the exact inline message when Image Capture was unassigned;
assigned Object Detection through the UI with real config values and
confirmed via curl they persisted exactly as entered; used the
"Configure" affordance on an already-assigned capability, edited its
settings, and confirmed the new values persisted after a page reload.
Note on tooling: setting a controlled React text input's `.value`
directly (bypassing React's patched native setter) silently reverts to
the last-rendered value instead of updating state - confirmed by seeing
schema defaults get saved instead of the values just "typed"; fixed by
using `Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,
'value').set.call(el, value)` before dispatching the `input` event, which
then round-tripped correctly - a browser-automation artifact, not a
product bug, but worth remembering for any future text-input
verification in this dashboard. Left the real "Kitchen Camera" device
with all 4 capabilities active, Object Detection carrying real
configuration (`model: "yolov8-real"`, `confidenceThreshold: "0.82"`) -
not scratch data reverted, a genuine demonstration of Phase 5 working.

**Addendum - the transitive-dependency example (spec §41), added for
real, not left as a curl-only demo:** spec §40/§41 walk through two
worked examples, Object Detection and Sink Cleanliness, the latter
specifically to demonstrate a transitive chain
(`SinkCleanliness -> ObjectDetection -> ImageCapture`). `SinkCleanliness`
is not a hypothetical - it's a real capability already built end-to-end
in `Vivnest.Agent` (`ClassifyCapability.SinkCleanliness`,
`SinkCleanlinessHandler`/`SinkCleanlinessWorker`, ADR-035/036) - but it
had never been added to the Admin `tblCapabilities` master list, so this
transitive chain had never actually existed in the Phase 5 model. Added
it for real: `Capability` "Sink Cleanliness" (`CapabilityType: Service`,
same classification as Object Detection - computed from another
capability's output), `ConfigurationSchema` = `regionOfInterest` (String,
optional) + `threshold` (Number, required, 0-1, default 0.8, matching
spec §41's literal example values) - deliberately a simpler shape than
the real runtime's own `SinkCleanlinessRoiOptions`/`SinkCleanlinessModelOptions`
(4 separate ROI ints + `ModelPath`/`ConfidenceThreshold`), since this
Admin schema is a deliberately separate, simpler system from the MVP
runtime config blob (see §46 in the original spec, "existing MVP
device-config boundary"), not a literal projection of it. Added
`CapabilityDependency` (`SinkCleanliness -> ObjectDetection`),
`DeviceTypeCapability` (`Camera` compatible with `SinkCleanliness`), and
an `AgentCapability` declaration ("Good 1Fitz Agent AI" declares
`SinkCleanliness`). Assigned it to the real "Kitchen Camera" device
through the dashboard UI (not curl) - the Add form correctly showed
*only* Sink Cleanliness as available (everything else already assigned),
showed no dependency gate (Object Detection was already active from the
earlier verification pass), rendered the new `regionOfInterest`/
`threshold` fields from its schema, and the assignment persisted exactly
as entered (`{"threshold":"0.85","regionOfInterest":"sink-counter-area"}`) -
confirmed via curl. Kitchen Camera now carries the full real chain,
Active: `Sink Cleanliness -> Object Detection -> Image Capture`.

## ADR-063 — Runtime configuration boundary: identity mapping + read-only projector

**Why:** The Admin domain (Tenant → Site → Device/Agent →
DeviceCapability/AgentCapability, ADR-042 through ADR-062) and the real
`Vivnest.Agent` runtime configuration (`appsettings.json` +
`device-config/*.json`) have been two completely disconnected systems
this whole time. ADR-058 already documented this in writing for Device
("two unrelated identity spaces... nothing links an admin-registered
Device to a real device's blob identity"). This session's runtime
inspection (reading the real `appsettings.json`, all 4 real
`device-config/*.json` files, and `Vivnest.Agent/Program.cs`'s config-
loading logic in full) confirmed the *exact same* problem exists for
Agent, never previously named: the real `appsettings.json`'s
`Agent:AgentId` (`5d6c8d6f-...`) matched none of the real `AgentRegistry`
rows registered through the Admin UI (`2039b5d5-...`, `a38c425f-...`).
Nothing an admin declared in `DeviceCapability` could ever mean anything
to the process that actually runs a device.

The user's own plan for closing this (adapted from a GPT-drafted
proposal): don't delete or replace the existing JSON yet. First an
**explicit, non-assumed identity mapping** (never guess the two ids are
equal), then a **read-only projector** proving the Admin domain *can*
produce the runtime shape, verified by a human eyeballing it against the
real file. Only once that's proven would a later phase wire the
projector into an actual write path and retire the JSON. This ADR is
that first step only.

**Two scope calls confirmed with the user before implementing:**
1. Map identity on **both** Device and Agent, not Device alone - the
   projector's `OwningAgentId` needs the Agent side resolved too or the
   projected config would carry a Guid the real Agent process doesn't
   recognize as itself.
2. This pass projects **Device identity + connection `Settings` only** -
   the fields that already map 1:1 today. Capability-level projection
   (Sink Cleanliness/Object Detection ROI + model config) is explicitly
   deferred: the real runtime shape (`RoiLeft`/`RoiTop`/`RoiRight`/
   `RoiBottom` ints, split across the device's own blob *and* a separate
   AI-agent per-agent blob, `AiClassificationOptions`) doesn't match the
   illustrative `regionOfInterest`/`threshold`/`model`/`confidenceThreshold`
   schema those two Capabilities got during the Phase 5 demo (ADR-062) -
   making it project correctly means redesigning those schemas and
   building capability-specific (not generic) projection logic across two
   different output files, real work deserving its own pass once this
   mechanism is proven.

**Domain**: `Device` (`Vivnest.Core/Domain/Device.cs`) gained
`RuntimeDeviceId` (string, default `""`) - the real `device-config/*.json`
blob's own `DeviceId` this admin Device corresponds to. `Agent`
(`Vivnest.Core/Domain/Agent.cs`) gained `RuntimeAgentId` (string, default
`""`) - the real `appsettings.json` `Agent:AgentId` this admin Agent
corresponds to. Both additive (`DeviceRegistryEntity.RuntimeDeviceId`/
`AgentRegistryEntity.RuntimeAgentId`, nullable, blank tolerated - same
"blank means not set yet" convention every other additive field in this
codebase uses, so **no data migration needed** on any real row). Both
**admin-typed, no FK/existence validation** - same deliberate "no
validation on this id" convention every other non-`OwningAgentId`/
`ExecutingAgentId` reference in this codebase follows; this mirrors
today's reality that runtime ids are themselves hand-authored, not looked
up anywhere. Deliberately **not** a separate join entity/table - a bare
1:1 string field is simplest and matches this project's own "don't
create a relationship table without its own properties/lifecycle"
principle (ADR-062's own reasoning for `CapabilityDependency`/
`DeviceTypeCapability`).

**`IDeviceConfigurationProjector`/`DeviceConfigurationProjector`**
(`Vivnest.Cloud/Admin`) - `ProjectAsync(tenant, deviceId)` loads the
`Device`, its `DeviceTypeDefinition`, and (if `OwningAgentId` is set) the
owning `Agent`, and produces a `ProjectedDeviceConfigDto`: `DeviceId`
(=`RuntimeDeviceId`, null if unset), `Name`, `Type` (the admin
`DeviceTypeDefinition.Name` matched case/whitespace-insensitively against
the real `Vivnest.Core.Enums.DeviceType` enum - e.g. "Motion Sensor" ->
`MotionSensor` - null if no match), `Enabled` (`DeviceStatus.Active`),
`Location`/`Brand`/`Model`/`Firmware`, `OwningAgentId` (the owning
Agent's `RuntimeAgentId`, null if unset or that Agent has no
`RuntimeAgentId` mapped), `Settings` (passed through as-is - already the
same shape `DeviceSettings` expects), and `Warnings` - a list naming each
unresolved gap (`RuntimeDeviceId` not set, `DeviceType` unmatched,
`OwningAgentId` unset/unmapped) rather than silently producing a
misleading preview or erroring out. **Read-only** - no Blob Storage
write, no write to any real `device-config/*.json` file, and
`LivenessInterval`/`WarningMultiplier`/`Schedule`/`Trigger`/
`SinkCleanliness`/`ObjectDetection`/`Sensors` are not projected at all
(explicitly out of scope, not half-built).

**Route**: new `GET devices-registry-admin/{deviceId}/projected-config`
on `DeviceRegistryAdminFunction.cs` - same tenant `x-api-key` +
`DevicesOnly` 403 shape every other route here uses. `POST`/`PUT
devices-registry-admin` now also accept `RuntimeDeviceId`
(optional/additive); `POST`/`PUT agents-registry-admin` now also accept
`RuntimeAgentId`.

**Dashboard**: new "Runtime Device Id"/"Runtime Agent Id" text fields in
`DeviceRegistryFormModal.tsx`/`AgentRegistryFormModal.tsx`, each with a
one-line hint explaining what it links to. New `LinkIcon`-triggered
"View Projected Config" action per row on `DeviceRegistryAdmin.tsx` opens
a new `ProjectedConfigModal.tsx` - read-only, pretty-printed JSON of the
identity/`Settings` fields plus a visible `Warnings` list (new
`.form-json-preview` CSS class, a bordered/monospace/scrollable `<pre>`
block).

**Verified for real** against the actual "Kitchen Camera" device and its
real `device-config/f7756a79-e507-4113-9b76-a9462b80a25d.json` file:
previewed the projection *before* linking anything - correctly returned
both warnings (`RuntimeDeviceId` not set; `OwningAgentId`'s Agent has no
`RuntimeAgentId`) with `DeviceId`/`OwningAgentId` both `null`. Set
`RuntimeAgentId` on the real "Good 1Fitz Capture Agent" to the real
`appsettings.json` value (`5d6c8d6f-4b8d-47e0-a56f-3c3e8cdb2d63`) and
`RuntimeDeviceId` on Kitchen Camera to the real device-config filename
(`f7756a79-e507-4113-9b76-a9462b80a25d`). Re-fetched the projection -
zero warnings, and every projected field (`DeviceId`, `Type: "Camera"`,
`Enabled: true`, `Brand`/`Model`/`Firmware`/`Location`, resolved
`OwningAgentId`) matched the real file exactly. The two fields *not* yet
reconciled (`Name`: admin says "Kitchen Camera", the real file says "Tapo
C120 Camera"; `Settings`: admin's is empty, the real file has real
`Host`/`Username`/`RtspUsername`) surfaced as genuine, visible
divergences - exactly what this tool exists to reveal, not a bug.
Confirmed 404 on a nonexistent `deviceId`. Confirmed `RuntimeAgentId`
round-trips on `GET agents-registry-admin`. Backend `dotnet build` clean.
Dashboard `tsc -b`/`vite build`/`oxlint` clean (only pre-existing
unrelated warnings). Browser-verified: the "View Projected Config" modal
renders the fully-linked JSON with no warnings; the Edit-device form
correctly pre-fills the "Runtime Device Id" field with the real linked
value after reload.

**Explicitly deferred, not started**: any actual write path (the
projector never touches Blob Storage or any real file); Sink
Cleanliness/Object Detection ROI+model capability-level projection (needs
a schema redesign, per the scope call above); auto-detection or
suggestion of a `RuntimeDeviceId`/`RuntimeAgentId` link (purely
admin-typed for now); retiring `device-config/*.json` or `appsettings.json`
(not even under discussion until the projector's output has been proven
against real production data over time).

## ADR-064 — Admin → Runtime Configuration Publishing: independent Agent/Device projection + a shared capability-projector registry

**Why:** ADR-063 proved the Admin domain *can* produce a correct preview
of a Device's runtime identity - the next question is what it actually
takes to let Admin **publish** real config, safely, without touching the
Phase 3/4/5 domain model, without deleting/bypassing
`device-config/*.json`, and with the existing MVP runtime working
unmodified throughout. Two earlier drafts of this design were rejected
during planning before any code was written: first a merge-patch of the
existing flat JSON shape directly (rejected once the user asked for a
generated, capability-shaped document instead of hand-preserving unknown
JSON keys); then a single Device-scoped pipeline whose capability
projections wrote into an *executing* Agent's blob as a side effect
(rejected once the user asked to move toward two fully independent
Agent/Device projection pipelines converging only at Blob Storage - see
the diagram the user provided). This ADR is that final, approved design.

**The capability-projection gap, resolved with a real worked example, not
asserted:** a generic `foreach DeviceCapability, serialize Settings` loop
cannot express that `ObjectDetection`/`SinkCleanliness` need part of their
settings on the *device's own* runtime entry (ROI - `RoiLeft`/`RoiTop`/
`RoiRight`/`RoiBottom`, matching `ObjectDetectionRoiOptions`/
`SinkCleanlinessRoiOptions` exactly) and part on the *executing agent's*
own document (model params - `ModelPath`/`ConfidenceThreshold`/
`ExpectedClasses`, matching `AiClassificationOptions.Devices[]` exactly) -
confirmed against the real `3a56ad98-...json` blob and the type comments
noting the two capabilities on one device can route to two *different*
executing agents. `ICapabilityRuntimeProjector` (below) is built
specifically to make this expressible.

**No domain-model changes** - `Device`/`Agent`/`DeviceCapability`
(`Vivnest.Core`) are untouched. Everything below is new Cloud-layer
services, one new store method, and (for the Device pipeline only) a new
Agent-side translation step.

**Architecture - two independent pipelines, shared capability registry:**

```
                         Admin
                           │
             ┌─────────────┴─────────────┐
             ▼                           ▼
          Agent                        Device
             │                           │
             ▼                           ▼
   Agent Configuration          Device Configuration
       Projection                   Projection
             │                           │
             ▼                           ▼
   Agent Runtime Config          Device Runtime Config
       Document                      Document
             │                           │
             ▼                           ▼
  agent-config/{agentId}.json   device-config/{deviceId}.json
             │                           │
             └─────────────┬─────────────┘
                           ▼
                  Azure Blob Storage
                           │
                           ▼
              Vivnest.Agent Container
```

Publishing a Device only ever writes the device blob; publishing an Agent
only ever writes the agent blob. Neither triggers the other - this is
what removes the multi-target write-ordering problem the rejected
mid-planning draft had to solve. Both pipelines share one
`ICapabilityRuntimeProjector` registry: a capability projector's
`DeviceEntry` output feeds the Device pipeline, its `AgentEntry` output
feeds the Agent pipeline, dispatched by `Capability.CapabilityName`
(free-text admin-typed master data, matched case/whitespace-insensitively,
same convention `MatchRuntimeDeviceType` already uses). **The registry
ships with zero concrete projectors in this pass** - confirmed against
the real `tblCapabilities` data during verification (Motion Detection,
Image Capture, Sink Cleanliness, Image Classification, Object Detection
all real, assigned rows) that none are simple/device-local enough to
safely ship untested; every one produces a "no runtime projector
registered" warning and is excluded from any published document rather
than guessed at. `Vivnest.Cloud/Admin/CapabilityProjection/
ICapabilityRuntimeProjector.cs` defines the contract
(`CapabilityProjectionResult { DeviceEntry, AgentEntry, Warnings }`,
`AgentCapabilityContribution { TargetRuntimeAgentId, RuntimeDeviceId,
CapabilityName, Settings }`) for whoever builds the first real one.

**Device Configuration Projection** - `IDeviceRuntimeConfigurationProjector`/
`DeviceRuntimeConfigurationProjector` (renamed from ADR-063's
`IDeviceConfigurationProjector`, same file locations). Identity/`Settings`
projection unchanged. New: runs the Device's Active `DeviceCapability`
rows through the shared registry, keeps each result's `DeviceEntry` into
a new `Capabilities` list on the (renamed) `DeviceRuntimeConfigurationDocumentDto`.
`AgentEntry` outputs are not this pipeline's concern - collected
independently by the Agent pipeline.

**Agent Configuration Projection** - new
`IAgentRuntimeConfigurationProjector`/`AgentRuntimeConfigurationProjector`.
Queries every `DeviceCapability` in the tenant/site whose
`ExecutingAgentId` is this Agent - a new store method,
`IDeviceCapabilityStore.GetByExecutingAgentAsync` (mirrors the existing
`GetByDeviceAsync`'s exact partition-scoped-query-filtered-client-side
shape, just filtering by `ExecutingAgentId` instead of `DeviceId` - store
plumbing, not a domain change). For each, resolves the owning Device
(warns/skips if its `RuntimeDeviceId` is unset), runs it through the same
registry, keeps each result's `AgentEntry`, and groups the results by
`RuntimeDeviceId` then `CapabilityName` into `AgentRuntimeConfigurationDocumentDto`
- deliberately the exact real `AiClassificationOptions.Devices[]` shape,
not a new format, since a Low-type agent's blob has an unrelated
`HomeAssistant` section this pipeline must never know or care about (no
Admin equivalent proposed for it, permanently out of scope). Rebuilt
fresh on every projection, so a capability reassigned or unassigned since
the last publish simply doesn't appear - removal is handled for free.

**Publishers, both hard-gated on non-empty `Warnings`:**
`IDeviceRuntimeConfigurationPublisher`/`DeviceRuntimeConfigurationPublisher`
writes a full, self-contained document to
`DeviceConfigBlob.BlobName(runtimeDeviceId)` (nothing else shares that
file, so a full overwrite is correct - PascalCase, no naming policy,
matching every other real config file in this codebase, deliberately
*not* the camelCase used only in the API-facing preview DTO which ASP.NET
Core serializes separately).
`IAgentRuntimeConfigurationPublisher`/`AgentRuntimeConfigurationPublisher`
does the opposite: downloads the existing blob (if any), replaces *only*
the top-level `AiClassification` key (matched by its real, exact,
case-sensitive name), leaves every other key completely untouched, and
re-uploads - since that blob is not exclusively Admin's.

**Credential-stripping guard (both publishers, `CredentialSettingsFilter`):**
`Device.Settings`/`DeviceCapability.Settings` are unguarded plain-text
dictionaries by deliberate design (ADR-050) - the Admin API already
accepts/returns credentials in them as an accepted risk. The *write path*
would make that materially worse: naively copying those dictionaries into
a generated document would push whatever's in Table Storage straight into
a live runtime blob, undermining ADR-038's local-only `.secrets.json`
boundary for that exact file. Both publishers strip any key matching a
known credential-shaped fragment (`password`, `accesstoken`, `secret`,
case-insensitive substring - covers `DeviceSettings.Password`/
`RtspPassword`/`HomeAssistant.AccessToken` and any future field) before
writing, surfacing an informational warning naming what was stripped and
why, instead of silently dropping it or silently publishing it. Full
secret management (non-secret config → Table/domain as today; secrets →
Key Vault reference → Runtime) is explicitly out of scope for this ADR -
this guard only prevents the new write path from making plaintext-in-blob
the standard while that's designed properly later.

**`Vivnest.Agent`: dual-shape Runtime Adapter (Device blob only)** - new
`DeviceConfigRuntimeAdapter.Adapt` (`Vivnest.Agent/Runtime/Configuration/`),
wired into `Program.cs`'s `TryLoadRemoteDeviceConfigsAsync` immediately
after each `device-config/*.json` blob is parsed. Detects shape by the
presence of a top-level `Capabilities` key; a legacy-shape blob (every
real device-config file today) is returned completely unchanged - no
flag-day migration, confirmed with the user up front. A new-shape blob is
flattened back into the exact identity fields `DeviceOptions` already
binds (`Capabilities` parsed but not yet translated into
`SinkCleanliness`/`ObjectDetection`/`Schedule`/etc. - nothing published
yet has any, so there's nothing to translate). **The Agent blob needs no
equivalent adapter** - `IAgentRuntimeConfigurationPublisher` writes
exactly the shape `Vivnest.Agent` already parses, so publishing an Agent
never changes what the Agent process needs to understand, only whether
that section is populated.

**Routes**: `POST devices-registry-admin/{deviceId}/publish-config` (new)
alongside the existing `GET .../projected-config`; new
`GET agents-registry-admin/{agentId}/projected-config` and
`POST agents-registry-admin/{agentId}/publish-config` (Agent had no
preview route before this ADR). All four return `200` with
`Published:false`/`Reason` when a gate blocks it (an expected outcome,
not an error) and `404` only if the Device/Agent itself doesn't exist.
(Both new `[Function]` names had to be renamed from the first draft's
`GetProjectedConfig`/`PublishConfig` to `GetDeviceProjectedConfig`/
`PublishDeviceConfig` and `GetAgentProjectedConfig`/`PublishAgentConfig`
- Azure Functions requires unique function names across the whole app,
not just per class; the collision was caught immediately by `func start`
refusing to register either duplicate.)

**Dashboard**: `ProjectedConfigModal.tsx` (ADR-063) extended with a
`Capabilities` block in the JSON preview and a "Publish" button, disabled
whenever `warnings.length > 0` (mirroring the backend's own hard gate).
New `AgentProjectedConfigModal.tsx`, same pattern, wired to a new
"View projected config" `LinkIcon` action on `AgentRegistryAdmin.tsx`
(which had no projected-config action before this ADR).

**Verified for real against live Azure data** (`stvivnestagent2`,
tenant "Sana" / site "1Fitz"), not just curl-shaped: created a throwaway
Device with no real corresponding blob and zero capabilities - projected
zero warnings, published successfully, downloaded the resulting blob and
confirmed its exact wire shape (`RuntimeDeviceId`/`Device{Connection}`/
`OwningAgentId`/`Capabilities`, PascalCase). **Ran the real
`Vivnest.Agent` process against it** - startup log confirmed
`Loaded 5 device config(s)` (the 4 real legacy-shape devices plus the new
one), the new device's `Type: Camera` was correctly read through the
adapter and routed into the same heartbeat/capture workers a legacy
device gets, zero exceptions, the 4 real devices completely unaffected.
Negative-tested the real "Kitchen Camera" (5 real Active
`DeviceCapability` rows, all correctly producing "no runtime projector
registered" warnings) - publish correctly refused, and a byte-for-byte
`diff` against the real `device-config/f7756a79-....json` on disk
confirmed the file was left completely untouched. Negative-tested
"Kitchen Motion Sensor" (`RuntimeDeviceId` unset) the same way.
Credential-stripping: created a device with `Password`/`Username` in
`Settings`, published successfully, confirmed the resulting blob has
`Username` but never `Password`, and the publish response's `Warnings`
named exactly which key was stripped and why. Agent side: both real
Agents correctly gated (Capture Agent - two real device assignments
missing `RuntimeDeviceId`/no registered projectors; AI Agent - missing
`RuntimeAgentId` itself); created a throwaway Agent, pre-seeded its blob
with an unrelated `{"HomeAssistant":{...}}` section by hand, published
against it, and confirmed the resulting blob has **both**
`HomeAssistant` (untouched) and a fresh `AiClassification` section -
proving the targeted-key-replace behavior for real, not just by code
review. `ConfigPublished` audit rows confirmed in both `tblDeviceEvents`
and `tblAgentEvents` with the correct `PartitionKey`/payload. All test
Devices/Agent/API key/blobs cleaned up (retired, deleted, or revoked)
after verification. Backend `dotnet build` clean across `Vivnest.Cloud`/
`Vivnest.Cloud.Functions`/`Vivnest.Agent`. Dashboard `tsc -b`/
`vite build`/`oxlint` clean. Browser-verified both modals end-to-end
(warnings render, Publish correctly disabled) against the live backend.

**Explicitly deferred, not started**: every concrete
`ICapabilityRuntimeProjector` implementation (`ObjectDetectionProjector`/
`SinkCleanlinessProjector` need the `Capability.ConfigurationSchema` data
on those two master rows redefined to the six/five real field names,
replacing the Phase 5 demo's illustrative `regionOfInterest`/`threshold`
schema - a data change, not a domain-model change); restart linkage
(publishing never enqueues an `agent-restart-commands` message - Agent
has no live-reload, and an unconfirmed automatic restart deserves its own
design); real secret-management architecture (Key Vault direction, named
but not designed here, per explicit instruction to ignore it for this
pass); retiring any hand-authored file (not even under discussion until
real capability projectors exist and have been proven in operation).

## ADR-065 — Phase 6C: first real capability projector/adapter (Image Capture) + configuration versioning

**Why:** The user provided a large, detailed "Phase 6C" spec (contract,
loader, adapter, startup sequence, identity validation, machine-swap
story, versioning, failure handling, migration steps), asking for the
runtime configuration contract to be designed and reviewed before any
code. Cross-checking that spec against the real codebase (a dedicated
research pass into Vivnest.Agent's worker/capability architecture) showed
most of it was already built and verified by ADR-063/064: the runtime
contract (DeviceRuntimeConfigurationDocumentDto/AgentRuntimeConfigurationDocumentDto),
the loader (TryLoadRemoteDeviceConfigsAsync), the adapter
(DeviceConfigRuntimeAdapter), startup integration and legacy fallback
(per-blob shape auto-detection, finer-grained than the spec's proposed
global toggle), identity validation (true by construction - the Agent
only ever requests the blob named after its own already-known identity),
and the machine-swap story (RuntimeAgentId already lives in local config,
independent of which machine runs it). Confirmed with the user: build on
that existing pipeline rather than a new unified per-agent document
shape; use a UTC publish timestamp for versioning, not a monotonic
counter. This ADR is the actual remaining work: the first real
ICapabilityRuntimeProjector/adapter pair, and configuration versioning.

**Image Capture, chosen deliberately as the hard proof case (per the
user's own recommendation):** unlike ObjectDetection/SinkCleanliness
(already nested, capability-shaped options on DeviceOptions), Image
Capture's real runtime shape is not nested at all - it's flat fields
directly on DeviceOptions itself (Schedule.Interval,
Schedule.Burst.Interval/.Duration, LivenessInterval, WarningMultiplier).
This proves the wire contract doesn't need to change per capability -
CapabilityDocumentEntryDto stays exactly the uniform shape ADR-064
already defined (CapabilityId/Name/Enabled/ExecutingAgentId/Settings).
What differs per capability is only where each side's own implementation
decides that entry's Settings land - a future ObjectDetectionProjector/
Adapter writes to a nested DeviceOptions.ObjectDetection;
ImageCaptureProjector/Adapter write to the device's own root fields
instead. No contract redesign needed - purely a per-capability
implementation detail on both sides, exactly the "capability-specific
adapters" the user's spec asked for. TriggerOptions.DeviceIds
(cross-device wiring, not really a capability setting) stayed out of
scope.

**Cloud: ImageCaptureRuntimeProjector**
(Vivnest.Cloud/Admin/CapabilityProjection/) - CapabilityName = "Image
Capture" (the real master row, confirmed already assigned to Kitchen
Camera and Kitchen Hub live). Parses DeviceCapability.Settings for five
required admin-typed keys (ScheduleIntervalMinutes, BurstIntervalSeconds,
BurstDurationMinutes, LivenessIntervalMinutes, WarningMultiplier) -
required, since a capability meant to fully specify capture cadence
shouldn't silently fall back to guessed defaults; missing/unparseable
values warn and block publish, same pattern every other projector uses.
The real "Image Capture" Capability.ConfigurationSchema was redefined to
these five fields via the existing admin CRUD route (a schema-data
change, not a domain-model change). Purely device-local - AgentEntry is
always null. Registered into the shared ICapabilityRuntimeProjector
collection in ServiceCollectionExtensions.cs.

**Agent: ICapabilityConfigRuntimeAdapter + ImageCaptureRuntimeAdapter**
(Vivnest.Agent/Runtime/Configuration/) - mirrors the Cloud-side registry
exactly: a small interface (CapabilityName, Apply(JsonObject
flattenedDevice, JsonObject capabilityEntry)), a lookup helper matching
the same case/whitespace-insensitive convention, and a hardcoded list (no
DI - this runs in Program.cs's config-loading phase, before the host is
built and any IServiceProvider exists). ImageCaptureRuntimeAdapter
re-validates every value independently even though the Cloud-side
projector already did (matching the user's explicit "Agent validation
protects runtime safety, never trust a blob just because Admin generated
it" instruction) and writes Schedule.Interval/Schedule.Burst.Interval/
.Duration/LivenessInterval/WarningMultiplier directly onto the flattened
device JsonObject before DeviceOptions binding.
DeviceConfigRuntimeAdapter.Adapt now dispatches each Capabilities[] entry
through this registry (previously: parsed but never translated). No
CameraCaptureWorker/DeviceHeartbeatWorker/any other worker code changed -
confirmed by the research pass and by real verification that both
already consume DeviceOptions off the existing IDeviceRuntimeStore seam,
unaware of where a field's value came from.

**Configuration versioning - PublishedUtc:** both publishers now stamp
PublishedUtc (UTC DateTime) at write time - DeviceRuntimeConfigWireDocument
gained the field directly; the Agent's blob gained a new top-level
ConfigurationPublishedUtc sibling key next to AiClassification (a second
key Admin now owns on that file), bound via a new root-bound
AgentConfigMetadataOptions (Vivnest.Core/Options/). Chosen over a
monotonic counter (confirmed with the user) - no shared counter state to
manage, no race between concurrent publishes, "newer wins" is just a
timestamp comparison. DeviceOptions/DeviceHeartbeat/AgentHeartbeat each
gained a ConfigurationPublishedUtc field, populated in
DeviceHeartbeatWorker.ProcessDeviceHeartbeat/AgentHeartbeatWorker.ExecuteAsync.
Admin-side consumption (desired-vs-running version comparison) is
explicitly future work - this ADR only makes the data reportable.

**Two real bugs caught by verification, not by review:**
1. DeviceHeartbeat/AgentHeartbeat (domain classes) gained
   ConfigurationPublishedUtc, but DeviceHeartbeatEntity/
   AgentHeartbeatEntity (the separate Table Storage entity classes) and
   their writers (DeviceHeartbeatWriter/AgentHeartbeatWriter) did not - a
   real, silent field-mapping gap between the domain and persistence
   layers that a compile-clean build never would have caught. Fixed by
   adding the field to both entities, both writers' explicit-field-by-field
   construction, and both ToModel() mapping extensions
   (DeviceHeartbeatMapping/AgentHeartbeatMapping,
   Vivnest.Infrastructure/DataStores/Helpers/) for GetAsync round-trip
   correctness too.
2. IConfiguration's default DateTime? binder parses a "Z"-suffixed JSON
   string by converting the value to the container's local timezone with
   Kind = Local (not by preserving Kind = Utc) - Azure Table SDK rejects
   anything but Kind = Utc outright, and the first real run threw
   NotSupportedException trying to persist the heartbeat. Fixed with
   .ToUniversalTime() (not DateTime.SpecifyKind(..., Utc), which would
   have silently corrupted the value by the timezone offset instead of
   just relabeling it) at both read points
   (DeviceHeartbeatWorker/AgentHeartbeatWorker).

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz"): redefined the real "Image Capture"
Capability.ConfigurationSchema via curl; created a throwaway Device with
only Image Capture assigned (real numeric settings), confirmed the
projector produced zero warnings, published successfully, downloaded the
resulting blob and confirmed the exact wire shape including PublishedUtc.
Ran the real Vivnest.Agent process against it twice (once catching each
of the two bugs above) - final run: zero errors, "Loaded 5 device
config(s)" (4 real legacy-shape devices + the new one), heartbeat
published successfully. Queried the real persisted DeviceHeartbeat row
directly: ExpectedLivenessInterval "00:01:00" and ConfigurationPublishedUtc
"2026-08-15T02:18:25.205822+00:00" both exactly matched the published
values - proof the adapter's actual bound runtime values took effect,
not just that the blob round-tripped. Negative-tested the real "Kitchen
Camera" (still blocked - four capabilities with no registered projector,
plus its own empty "Image Capture" Settings correctly producing the five
new required-field warnings) and the real Capture Agent (still blocked
by pre-existing, unrelated real data gaps - Kitchen Hub's incomplete
Image Capture settings, two devices missing RuntimeDeviceId, Motion
Detection with no projector) - neither publish attempt wrote anything,
confirmed by the gate refusing before any blob write. All test artifacts
(blob, Device, API key) cleaned up after verification. Backend
dotnet build clean across Vivnest.Cloud/Vivnest.Cloud.Functions/Vivnest.Agent.

**Explicitly deferred, not started**: the Agent-side
ConfigurationPublishedUtc reporting path (AgentConfigMetadataOptions/
AgentHeartbeatWorker) is implemented and code-reviewed but not exercised
end-to-end this pass - the real Capture Agent's publish is currently
blocked by pre-existing, unrelated real data gaps (not by anything this
ADR changed), and creating a second throwaway agent wouldn't exercise it
either, since the running process only ever loads its own identity's
blob; every other concrete capability projector/adapter (ObjectDetection,
SinkCleanliness, Motion Detection, Image Classification) - each gets its
own pass, per the user's explicit "one capability at a time" instruction;
ObjectDetection/SinkCleanliness specifically still need the cross-agent
AgentEntry write path (device ROI + executing-agent model params,
designed in ADR-064's worked example, never implemented); restart/hot-reload
linkage (still zero IOptionsMonitor usage anywhere in Vivnest.Agent,
confirmed); a hard-fail-on-missing-config startup mode (today's graceful
degrade is kept); per-agent scoped storage credentials; Admin-side
desired-vs-running version comparison UI/logic; any unified
single-document-per-agent redesign (confirmed with the user not to
pursue).

## ADR-066 — SchemaVersion validation + ObjectDetection/SinkCleanliness capability projectors

**Why:** A second GPT-drafted spec, cross-checked the same way as
ADR-065's, showed ~95% overlap with what was already shipped. The one
genuine gap: no explicit schema-version field on either wire document, so
the Agent would silently try to bind whatever shape a blob happened to
have rather than rejecting a document it doesn't recognize. Confirmed
with the user: fix that gap first, then move on to
ObjectDetection/SinkCleanliness - the cross-agent capability pair
ADR-064's worked example (§1a) designed but never implemented. Re-reading
the shipped AgentRuntimeConfigurationProjector/AgentRuntimeConfigurationPublisher
(ADR-064) confirmed the AgentEntry consumption path (registry lookup,
grouping by RuntimeDeviceId/CapabilityName, targeted AiClassification
write) is already fully generic across capabilities - zero changes
needed there; the only new code is the two projectors (Cloud) and their
two adapters (Agent).

**SchemaVersion/ConfigurationSchemaVersion:** new
RuntimeConfigurationSchemaVersions constants class
(Vivnest.Core/Constants/) - CurrentDeviceSchemaVersion = 1,
CurrentAgentSchemaVersion = 1, the single source both publishers and the
Agent adapter reference. DeviceRuntimeConfigWireDocument gained a
top-level SchemaVersion field; the Agent's blob gained a top-level
ConfigurationSchemaVersion sibling key next to AiClassification/
ConfigurationPublishedUtc, bound via the existing root-bound
AgentConfigMetadataOptions. DeviceConfigRuntimeAdapter.Adapt checks
SchemaVersion before flattening - absent is tolerated as version 1 (a
device published before this ADR), present-but-mismatched throws a new
UnsupportedConfigurationSchemaException naming the device and both
versions. Program.cs's TryLoadRemoteDeviceConfigsAsync wraps the Adapt
call in its own dedicated try/catch (previously only download/parse were
wrapped) so one device declaring an unrecognized schema is skipped with a
log line rather than aborting every other device via the method's outer
catch-all. AgentHeartbeatWorker gets an analogous one-time check (not
per-tick) that only logs a warning, since a mismatch there fails
individual field binding rather than corrupting scheduling.

**Cloud: ObjectDetectionRuntimeProjector/SinkCleanlinessRuntimeProjector**
(Vivnest.Cloud/Admin/CapabilityProjection/) - the actual cross-agent case
ADR-064's worked example designed for but ImageCaptureRuntimeProjector
(purely device-local) never exercised: ROI
(RoiLeft/RoiTop/RoiRight/RoiBottom, required ints) goes on the device's
own DeviceEntry.Settings; model parameters (ModelPath required string,
ConfidenceThreshold required double, plus optional comma-separated
ExpectedClasses on Object Detection only) go on the *executing* agent's
AgentEntry.Settings, TargetRuntimeAgentId = the resolved
executingRuntimeAgentId. A single Warnings list gates both halves
together deliberately - a device with ROI but no model (or vice versa)
is genuinely broken at runtime either way, so neither DeviceEntry nor
AgentEntry is produced if any required field is missing, rather than
letting a half-configured capability quietly publish as "working" on one
side. Both real "Object Detection"/"Sink Cleanliness" Capability rows'
ConfigurationSchema were redefined to these field names via the existing
admin CRUD route (a schema-data change, not a domain-model change),
replacing the Phase 5 demo's illustrative regionOfInterest/threshold
schema - same move ADR-065 made for "Image Capture". Both registered
into the shared ICapabilityRuntimeProjector collection in
ServiceCollectionExtensions.cs.

**Agent: ObjectDetectionRuntimeAdapter/SinkCleanlinessRuntimeAdapter**
(Vivnest.Agent/Runtime/Configuration/) - mirror
ImageCaptureRuntimeAdapter's shape (independent re-validation of every
value, never trust a blob just because Admin generated it) but write a
**nested** DeviceOptions sub-object instead of root fields, matching the
real ObjectDetectionRoiOptions/SinkCleanlinessRoiOptions shape exactly:
{Enabled, RoiLeft, RoiTop, RoiRight, RoiBottom, ExecutingAgentId}.
ExecutingAgentId is read straight off the capability entry's own
ExecutingAgentId field - already the resolved RuntimeAgentId by the time
it reaches the Agent (the Cloud projector resolved it), exactly the
value SinkCleanlinessHandler/the classify-request queue message already
expects. No ModelPath/ConfidenceThreshold/ExpectedClasses handling here
at all - those live entirely on the executing agent's own blob, read
directly via AiClassificationOptions binding with no adapter, exactly as
ADR-064 already established. Both added to
DeviceConfigRuntimeAdapter.DefaultCapabilityAdapters.

**One real bug caught by verification, not by review:**
AgentRuntimeConfigurationProjector's final DTO assembly looked up
`kvp.Value.TryGetValue("ObjectDetection", ...)`/`"SinkCleanliness"` in
the per-capability settings dictionary, but the dictionary is actually
keyed by each projector's real CapabilityName - "Object Detection"/"Sink
Cleanliness", with a space - since ADR-064's registry keys by the exact
Capability master-row name, not a code-identifier form of it. The lookup
therefore always missed, so both fields projected as null even when the
underlying assignment was fully configured with zero warnings; since
AgentRuntimeConfigurationPublisher writes those same DTO fields straight
to the wire document, publishing would have silently written null model
parameters for every device instead of the real ones - the previous
end-to-end verification never caught this because it exercised the
generic mechanism itself, not a real registered capability's actual
name. This is the first pass to touch it with real capability names, and
it surfaced immediately in the projected-config check. Fixed by matching
the dictionary lookups to the real capability names ("Object Detection"/
"Sink Cleanliness").

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz"): redefined both real Capability.ConfigurationSchema
rows via curl; created a throwaway Device with Image Capture (dependency
prerequisite), Object Detection, and Sink Cleanliness all assigned, and a
throwaway executing Agent with a fresh RuntimeAgentId declaring both
capabilities; confirmed zero warnings on both the device- and
agent-side projected-config (catching the key-name bug above on the
first pass, zero warnings but null settings - fixed, rebuilt, restarted
func, re-verified clean); published both sides successfully; downloaded
both resulting blobs directly from blob storage and confirmed the exact
wire shapes - device blob's Capabilities[] with ROI-only Object
Detection/Sink Cleanliness entries plus SchemaVersion: 1, agent blob's
AiClassification.Devices[] with a model-param-only entry (ModelPath/
ConfidenceThreshold/ExpectedClasses) plus ConfigurationSchemaVersion: 1.
Ran the real Vivnest.Agent process against the standing local-dev test
Capture Agent identity (5d6c8d6f-..., the pre-existing "Good 1Fitz
Capture Agent" dummy test agent, not a real production agent) - "Loaded 5
device config(s)", zero errors, the new test device's image capture
started normally alongside four real/legacy devices. Negative-tested
SchemaVersion validation: hand-uploaded a blob with SchemaVersion: 99 for
a second throwaway device, re-ran the Agent, confirmed a clear "[Startup]
... declares SchemaVersion 99 ... Skipping." log line for that one
device while the other five (including the valid ObjectDetection/
SinkCleanliness device) still loaded normally - "Loaded 5 device
config(s)" unchanged. Confirmed the real Kitchen Camera blob's
lastModified timestamp untouched (predates this pass) and legacy-shape
devices unaffected. All test artifacts cleaned up after: both device-config
test blobs and the agent-config test blob deleted, test Device retired
(not hard-deleted, per ADR-058), test Agent hard-deleted, both the
originally-created test API key and a second cleanup key (needed after
the first was revoked mid-cleanup) revoked, local func host stopped.
Backend dotnet build clean across Vivnest.Cloud/Vivnest.Cloud.Functions/
Vivnest.Agent throughout.

**Explicitly deferred, not started**: Motion Detection and Image
Classification still have no registered projector, so the real Kitchen
Camera stays blocked from publishing after this pass too - full
unblocking needs those last two projectors in a future pass; restart/hot-reload
linkage; a hard-fail-on-missing-config startup mode; per-agent scoped
storage credentials; Admin-side desired-vs-running version comparison
UI/logic; any unified single-document-per-agent redesign (confirmed with
the user not to pursue, same as ADR-065).

## ADR-067 — Motion Detection capability projector/adapter

**Why:** The next capability pass after ADR-066 - Motion Detection and
Image Classification are the two remaining capabilities blocking the real
Kitchen Camera from publishing. Research before planning found the two
aren't symmetric: Image Classification is a pure Phase 5 demo placeholder
with no Options class, no classifier interface, no worker anywhere -
building a projector for it would mean inventing a whole new
classification capability from scratch with nothing real to verify
against, so the user chose to scope it out of this pass. Motion Detection
is real and fully implemented already -
MotionSensorMonitorWorker/MotionSensorMonitorService
(Vivnest.Agent/Capabilities/MotionSensor/) drives standalone PIR sensor
devices (DeviceType.MotionSensor, e.g. the real Tapo T100 device
539cb3e1-... seen live in every Agent run this session), reading
LivenessInterval/WarningMultiplier/Schedule.Interval straight off
DeviceOptions - the same flat-field pattern ImageCaptureRuntimeProjector/
ImageCaptureRuntimeAdapter (ADR-065) already proved for Image Capture.

**Real field-collision risk found and resolved with the user before
implementation:** ImageCaptureRuntimeProjector already claims those same
root fields for Camera devices, and the real Kitchen Camera has both
Image Capture *and* Motion Detection assigned - if Motion Detection's
projector wrote those fields unconditionally, whichever capability's
Agent-side adapter ran last in the Capabilities[] loop would silently
overwrite the other's values, and a Camera device gets no real distinct
behavior from "Motion Detection" anyway (no video-based motion logic
exists in CameraCaptureWorker). Confirmed with the user: gate by device
type - the projector only produces a real entry for an actual
DeviceType.MotionSensor device; any other device type gets a clear
warning and is excluded from publish, same shape every other
unmet-requirement warning in this pipeline already uses.

**Device-type gating - the one new piece of machinery:**
ICapabilityRuntimeProjector.Project only receives the raw
DeviceRegistryEntity (DeviceTypeId, an FK - not the resolved runtime
DeviceType enum value); the resolution logic
(DeviceRuntimeConfigurationProjector.MatchRuntimeDeviceType) is private
to the device-level projector and only runs once per device, before
per-capability projection. Rather than threading a resolved type string
through the shared interface and both its callers
(DeviceRuntimeConfigurationProjector.ProjectCapabilitiesAsync,
AgentRuntimeConfigurationProjector.ProjectAsync) for one consumer,
MotionDetectionRuntimeProjector takes its own IDeviceTypeStore dependency
(already a registered service) and resolves DeviceTypeId itself,
duplicating the same ~5-line free-text-vs-enum match. Since
ICapabilityRuntimeProjector.Project is deliberately synchronous (every
implementation runs inline inside a foreach, no async enumeration exists
in either caller), this one Table lookup is resolved via
`.GetAwaiter().GetResult()` - confined entirely to this one file, safe
under the Isolated Worker host (no captured SynchronizationContext to
deadlock against), and avoids making every existing projector async for
a single consumer's need.

**Cloud: MotionDetectionRuntimeProjector**
(Vivnest.Cloud/Admin/CapabilityProjection/) - mirrors
ImageCaptureRuntimeProjector's device-local shape exactly
(AgentEntry always null). Required admin-typed keys:
LivenessIntervalMinutes, WarningMultiplier; optional
BatteryReportIntervalMinutes (maps to Schedule.Interval, which
MotionSensorMonitorWorker already falls back to a 2-hour default for
when unset - omitting it keeps today's behavior unchanged). The real
"Motion Detection" Capability.ConfigurationSchema was redefined to these
three field names via the existing admin CRUD route, replacing the
Phase 5 demo's illustrative schema - same move ADR-065/066 made for the
other Built-in/Service capabilities. Registered into the shared
ICapabilityRuntimeProjector collection in ServiceCollectionExtensions.cs.

**Agent: MotionDetectionRuntimeAdapter**
(Vivnest.Agent/Runtime/Configuration/) - mirrors
ImageCaptureRuntimeAdapter minus the Burst fields (Motion Detection has
no burst-capture concept), writing LivenessInterval/WarningMultiplier/
Schedule.Interval directly onto the flattened device JsonObject. No
device-type check needed here - by construction, the Cloud-side gate
already guarantees a "Motion Detection" Capabilities[] entry only ever
appears on a real DeviceType.MotionSensor device's document, same
"identity validation is true by construction" reasoning ADR-065
established for ExecutingAgentId. Added to
DeviceConfigRuntimeAdapter.DefaultCapabilityAdapters.

**A second real data gap found during verification, not by review:**
the real DeviceTypeCapability compatibility table
(Vivnest.Cloud/Admin/CapabilityCompatibilityService.cs, ADR-062) had
Motion Detection registered compatible with Camera only - never with
Motion Sensor, the device type that actually has real runtime behavior
behind it. Assigning Motion Detection to the first throwaway Motion
Sensor test device failed at the compatibility-check layer, before ever
reaching the new projector, with "Capability is not compatible with this
DeviceType." This is the same kind of leftover Phase 5/ADR-061 demo-data
mismatch the schema placeholders were - fixed the same way, as a
legitimate admin data change (a new DeviceTypeCapability row linking
Motion Sensor + Motion Detection via the existing
device-type-capabilities-admin/add route), not a code change. Left in
place after verification, unlike the throwaway test artifacts - it's
real enabling data, not test data.

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz"): redefined the real "Motion Detection"
Capability.ConfigurationSchema via curl; added the missing Motion
Sensor + Motion Detection DeviceTypeCapability compatibility row;
created a throwaway Device of DeviceTypeId "Motion Sensor" with valid
settings, confirmed zero warnings and a successful publish; downloaded
the resulting blob and confirmed Type: "MotionSensor", the admin-typed
Settings on the Motion Detection capability entry, and SchemaVersion: 1.
Ran the real Vivnest.Agent process against it - "Loaded 5 device
config(s)", zero errors, MotionSensorMonitorWorker started and logged
"Device adr067-test-motionsensor-0001 sleeping for 00:05:00" - exactly
the published LivenessIntervalMinutes: 5, proof the adapter's actual
bound runtime value took effect, not just that the blob round-tripped
(the subsequent reading failure was the expected result of the fake
test host having no real Tapo hardware behind it). **Negative test**:
assigned Motion Detection to a throwaway Camera-type device, confirmed
the exact warning "Motion Detection requires a Motion Sensor device, but
this device is a Camera." appeared and blocked publish. Confirmed the
real Kitchen Camera (read-only projected-config check, never published)
now shows that same clearer warning in place of the old generic "no
runtime projector registered," alongside its pre-existing unrelated data
gaps (Image Capture/Sink Cleanliness/Object Detection settings unset,
Image Classification still unregistered) - still correctly blocked
overall. All test artifacts cleaned up after: the device-config test
blob deleted, both throwaway Devices retired (not hard-deleted, per
ADR-058), the test API key revoked, local func host stopped. Backend
dotnet build clean across Vivnest.Cloud/Vivnest.Cloud.Functions/
Vivnest.Agent throughout.

**Explicitly deferred, not started**: Image Classification still has no
registered projector (no real implementation exists to project into -
out of scope per the user's explicit choice this pass), so the real
Kitchen Camera stays blocked from publishing even after this pass;
restart/hot-reload linkage; a hard-fail-on-missing-config startup mode;
per-agent scoped storage credentials; Admin-side desired-vs-running
version comparison UI/logic; any unified single-document-per-agent
redesign (confirmed with the user not to pursue, same as ADR-065/066).

## ADR-068 — Configuration Lifecycle (scoped): Desired/Published/Applied status + auto-restart on publish

**Why:** The user pasted a large "Phase 6D — Configuration Lifecycle &
Synchronization" spec proposing monotonic versioning, immutable
`versions/{n}.json` blobs + `current.json` manifests, true Agent-side
periodic polling with apply-without-restart, rollback, and
Desired/Published/Applied status reporting. Cross-checking against the
shipped ADR-063–067 pipeline found the spec's own "apply" mechanism (its
section 13) was already ~90% built: `DeviceHeartbeat`/
`AgentHeartbeat.ConfigurationPublishedUtc` (ADR-065) already reports
"what timestamp is baked into what the Agent is currently running" -
effectively AppliedVersion, keyed by UTC timestamp instead of an int -
and the `agent-restart-commands` queue +
`CommandPollingWorker`/`IAgentCommandPublisher` (pre-dating this session,
ADR-035/044) already provide a full, already-proven restart-to-reload
mechanism; it was just never triggered automatically after a publish.
Confirmed with the user: do not reopen ADR-065's timestamp-versioning
decision or move to immutable versioned blobs/rollback/true periodic
polling this pass - wide blast radius, reverses a prior explicit choice.
Scoped to two pieces: auto-restart on publish, and a computed
Desired/Published/Applied status (including a coarse Agent-level "config
load failed" signal, confirmed with the user, so Status can distinguish
`Failed` from `Pending`).

**Auto-restart on publish**: `DeviceRuntimeConfigurationPublisher`/
`AgentRuntimeConfigurationPublisher` (`Vivnest.Cloud/Admin/`) now inject
the already-registered `IAgentCommandPublisher` and call
`PublishRestartCommandAsync(runtimeAgentId)` right after a successful
blob write - `document.OwningAgentId` (Device publish, already resolved
to the owning agent's RuntimeAgentId by the projector) or
`document.AgentId` (Agent publish). Best-effort: a queue hiccup is logged
and swallowed, never fails a publish that already succeeded.

**Desired/Published/Applied status**: new `ConfigurationSyncStatus` enum
(`Vivnest.Core/Enums/`: `NeverPublished`/`Pending`/`UpToDate`/`Failed`/
`Unknown`) and `ConfigurationSyncStatusDto` (`Vivnest.Cloud/Api/Dtos/`),
attached as a new optional `SyncStatus` property on the existing
`DeviceRuntimeConfigurationDocumentDto`/`AgentRuntimeConfigurationDocumentDto`
via a `with` expression - reuses the `GET .../projected-config` routes
the dashboard already calls rather than adding new ones. "Desired" is
never re-fetched or persisted separately - it's just the same
already-projected document this response already carries. New
`IConfigurationSyncStatusService`/`ConfigurationSyncStatusService`
(`Vivnest.Cloud/Admin/`) computes it from two best-effort lookups: the
currently published blob's own `PublishedUtc` (a small `file`-scoped
read-only record per blob shape, downloaded and peeked - a 404 means
`NeverPublished`), and the latest heartbeat's
`ConfigurationPublishedUtc`/`ConfigurationLoadError` via the
already-existing `IDeviceHeartbeatReader`/`IAgentHeartbeatReader`
(`Vivnest.Cloud/Interfaces/`) - both readers already registered, no new
Table access pattern introduced. `RuntimeDeviceId`/`RuntimeAgentId`
(already resolved by projection, sitting right on the document) key the
heartbeat row directly, no extra registry lookup needed.

**Coarse Agent-reported apply failure (confirmed with the user:
Agent-level only, not per-device)**: `Program.cs`'s
`TryLoadRemoteDeviceConfigsAsync` accumulates
`UnsupportedConfigurationSchemaException` messages into a list (filtered
to devices this agent actually owns, checked via the raw pre-`Adapt`
document's own `OwningAgentId` - `Adapt` throws before producing a
flattened object, but the raw new-shape document's top-level
`OwningAgentId` survives untouched) and injects it as a sibling
`ConfigurationLoadErrors` key alongside the method's existing `Devices`
`IConfiguration` source - lands as a true root-level key next to
`ConfigurationSchemaVersion`/`ConfigurationPublishedUtc` since
`IConfiguration` merges every source into one flat tree regardless of
which call contributed which key, so no new plumbing was needed beyond a
new `AgentConfigMetadataOptions.ConfigurationLoadErrors` property.
`AgentHeartbeatWorker` joins it into `AgentHeartbeat.ConfigurationLoadError`
(join, not a structured list - "something needs investigating," the
per-blob detail already lives in the console log). Same four-file ripple
ADR-065's `ConfigurationPublishedUtc` addition already went through
(`AgentHeartbeat`/`AgentHeartbeatEntity`/`AgentHeartbeatWriter`/
`AgentHeartbeatMapping`) - deliberately, so the field doesn't silently
vanish between the domain and persistence layers the way ADR-065's own
"bug #1" did.

Device-level Status also checks the *owning agent's* heartbeat for this
same coarse error (not the device's own - devices don't get one) - an
admin investigating a stuck device is pointed at the right agent's
message, even though it can't say *which* of that agent's devices caused
it.

**One real bug caught by verification, not by review**: `ConfigurationSyncStatus`
is a genuine C# enum serialized straight through the API response - every
other "status" in this codebase is a plain string on its entity/DTO
(`DeviceRegistryEntity.Status`, `DeviceCapabilityStatus.ToString()`,
...), never an actual enum serialized via `System.Text.Json`, so there
was no ambient `JsonStringEnumConverter` anywhere in the pipeline to
catch this. Without one, the first real projected-config response came
back `{"status":0}` instead of `{"status":"NeverPublished"}` - would have
silently broken the dashboard's `ConfigurationSyncStatus` string-union
type. Fixed with `[JsonConverter(typeof(JsonStringEnumConverter))]`
directly on the enum declaration.

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz", reusing the standing `5d6c8d6f-...` test Capture
Agent identity): created a throwaway Motion Sensor device with Motion
Detection assigned, confirmed `NeverPublished` before any publish;
published, confirmed `PublishedUtc` set and `Status: Unknown` (no
heartbeat yet); ran the real `Vivnest.Agent` process and confirmed via
its own console log that the auto-enqueued restart command was received
and honored (`CommandPollingWorker`'s "Restart command received...
stopping application", issued at the exact publish timestamp) - proof
the auto-restart wiring works end-to-end, not just that a queue message
was sent; ran the Agent again, confirmed the heartbeat converged and
`Status` became `UpToDate` with `PublishedUtc == AppliedUtc` exactly.
**Failure test**: hand-uploaded a blob with a bumped `PublishedUtc` and
`SchemaVersion: 99`, ran the real Agent, confirmed it skipped only that
device (`Loaded 4` not `5`) while continuing to run every other device
normally, and confirmed the projected-config response showed
`PublishedUtc` (the new bad one), `AppliedUtc` (the old, still-good one -
proof the Agent kept running its last known-good config, never lost it),
`ApplyError` (the exact console-logged message), and `Status: Failed`.
All test artifacts cleaned up after: the blob deleted, the throwaway
Device retired, the test API key revoked, local func host stopped, and
the shared test Capture Agent's own heartbeat restored to a clean
`ConfigurationLoadError: null` state with one final good run (it's a
standing identity reused across every ADR this session, not a
throwaway). Backend dotnet build clean across
Vivnest.Cloud/Vivnest.Cloud.Functions/Vivnest.Agent throughout; dashboard
`tsc -b && vite build` clean.

**Explicitly deferred, not started** (all confirmed out of scope with the
user): monotonic version numbers; immutable `versions/{n}.json` blobs;
`current.json` manifest; rollback; true Agent-side periodic
staleness-polling independent of a publish event; configuration
fingerprint/hash; optimistic-concurrency/ETag guard on publish; per-device
(as opposed to Agent-level) failure attribution. All candidates for a
distinctly separate, larger future pass given the blast radius on
today's single-blob-per-id layout.

## ADR-069 — Configuration Lifecycle Pass 1: monotonic versioning, immutable blobs, manifest, hash, concurrency

**Why:** The remaining, larger half of the original "Phase 6D" spec -
real monotonic version numbers, immutable versioned blobs, a lightweight
manifest, a deterministic content hash, and optimistic concurrency on
publish. Deliberately not attempted as one pass: sequenced into three
(this one: the storage-layer foundation; a future rollback + audit trail
pass; a future true Agent-side periodic self-restart-polling pass, all
confirmed with the user), and the new layout runs **alongside** today's
flat blobs rather than replacing them (also confirmed with the user) -
every publish writes both shapes, the Agent tries the new manifest path
first and falls back to the flat blob exactly as it already falls back
for a device that predates ADR-064 entirely.

**Already true, reused as-is**: `AzureTableStore.UpdateAsync` already
enforces optimistic concurrency via `entity.ETag` (a `RequestFailedException`
412 on a stale write) - exactly the guard the spec's own concurrency
requirement asks for, already built, needing no new mechanism.

**Blob layout** (additive, alongside `{id}.json`):
`device-config/{runtimeDeviceId}/versions/{n}.json` (immutable - written
with `AzureBlobStorageClient.UploadAsync`'s new `failIfExists: bool`
parameter, `BlobRequestConditions { IfNoneMatch = ETag.All }`, a 409 on
collision) and `.../current.json` (a mutable pointer, freely overwritten).
Same shape under `agent-config/`. Discovery needed no new
`AzureBlobStorageClient` method - Blob Storage has no real directories,
so the new layout shows up in the exact same `ListBlobNamesAsync` listing
`TryLoadRemoteDeviceConfigsAsync` already enumerates, partitioned purely
by string shape (`EndsWith("/current.json")` picks manifests to follow;
`!Contains('/')` picks legacy flat entries; a bare `.../versions/{n}.json`
entry is never acted on directly, only ever read by URI from a manifest).
Manifest-driven devices are processed first so a device republished
through the new pipeline always wins over its own stale legacy blob
(still written on every publish) rather than the two racing on
enumeration order.

**New `DeviceConfigurationEntity`/`AgentConfigurationEntity`**
(`Vivnest.Core/DataStores/Entities/`, new `tblDeviceConfiguration`/
`tblAgentConfiguration`) - deliberately tracks only Published state
(`CurrentVersion`/`CurrentHash`/`PublishedUtc`), the one thing genuinely
new here. Desired stays exactly as ADR-068 defined it - never persisted,
always the live-projected document; Applied still lives entirely on the
heartbeat.

**Publisher changes** (`DeviceRuntimeConfigurationPublisher`/
`AgentRuntimeConfigurationPublisher`): compute a SHA-256 hash of the
*content-only* portion of the document (deliberately excluding
`PublishedUtc`/`SchemaVersion`/`ConfigurationVersion`/`ConfigurationHash`
themselves - hashing those would make the hash change on every publish
even when nothing an Admin actually controls did, defeating the entire
point of comparing hashes). Read the existing metadata row; a matching
hash is a **no-op** - `Published: false, Reason: "Configuration unchanged
since version {n}."`, reusing the existing publish-result shape rather
than a new one. A changed hash gets `NewVersion = CurrentVersion + 1`,
written to an immutable version blob, then `current.json`, then the
legacy flat blob (now additionally carrying `ConfigurationVersion`/
`ConfigurationHash`, so even an Agent build that only reads the flat path
can report them), then the metadata row (`UpsertAsync` on first publish,
`UpdateAsync` with the captured `ETag` afterward - the update path is
what enforces the concurrency guard). A 409 on the version blob write or
a 412 on the metadata write both retry the whole cycle from a fresh read
(bounded, `MaxPublishAttempts = 3`) - not a distributed transaction
(explicitly out of scope), so a losing concurrent attempt can leave one
immutable version blob orphaned (never referenced by any manifest), a
deliberate, tolerable cost for correctness over perfectly gap-free
version numbers. The Agent publisher's hashed/versioned content is
deliberately just the `AiClassification` section (the only thing Admin
actually controls on that blob) via a new self-contained
`AgentConfigWireDocument`, not a merge with whatever Agent-local sections
(e.g. a Low-type agent's `HomeAssistant`) happen to exist on the legacy
flat blob - those were never part of "desired state" at all, so they
must never affect whether a republish is considered a real change.

**Agent-side changes**: `DeviceConfigRuntimeAdapter.Adapt` now also
copies `ConfigurationVersion`/`ConfigurationHash` (mirrors its existing
`PublishedUtc` → `ConfigurationPublishedUtc` extraction exactly - both
the legacy flat blob and the new version blob are the *same*
`DeviceRuntimeConfigWireDocument` JSON shape, so no shape-detection
changes were needed). New nullable `ConfigurationVersion`/
`ConfigurationHash` fields, same four-file ripple `ConfigurationPublishedUtc`
(ADR-065) and `ConfigurationLoadError` (ADR-068) already went through
(`DeviceOptions`/`AgentConfigMetadataOptions`, `DeviceHeartbeat`/
`AgentHeartbeat`, their entities, their writers/mappings).
`ConfigurationSyncStatusService` tries the manifest first for
`PublishedVersion`/`PublishedHash`/`PublishedUtc`, falling back to the
flat blob's `PublishedUtc` peek (ADR-068, unchanged) on a 404; compares
version+hash directly when both sides have them (more precise than a
timestamp - an exact content match), falling back to the timestamp
comparison when either side is still on the legacy path - neither
comparison is a special case of the other.

**One real bug caught by verification, not by review**:
`ConfigurationSyncStatus` (ADR-068's own enum) is a genuine C# enum
serialized straight through this API - every other "status" in this
codebase is a plain string on its entity/DTO, never an actual enum
serialized via `System.Text.Json`, so there was no ambient
`JsonStringEnumConverter` anywhere in the pipeline to catch it. The first
real projected-config response after ADR-068 shipped would have
come back `{"status":0}` instead of `{"status":"NeverPublished"}` -
caught this pass during real-Azure verification (the very first
`NeverPublished` check), not by review at the time. Fixed with
`[JsonConverter(typeof(JsonStringEnumConverter))]` directly on the enum
declaration.

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz", reusing the standing `5d6c8d6f-...` test Capture
Agent identity): created a throwaway Motion Sensor device, confirmed
`NeverPublished` before any publish; published, confirmed
`versions/1.json`/`current.json`/the legacy flat blob all exist with
matching `ConfigurationVersion: 1` and identical hashes; republished with
*identical* settings, confirmed the no-op path fired (`"Configuration
unchanged since version 1."`, no `versions/2.json` written); republished
with *changed* settings (`LivenessIntervalMinutes` 5→10), confirmed
`versions/2.json` was created while `versions/1.json` remained
byte-for-byte unchanged (true immutability, not just "the API says so")
and `current.json` advanced to point at version 2; ran the real
`Vivnest.Agent` process and confirmed via `MotionSensorMonitorWorker`'s
own console log ("sleeping for 00:10:00") that the version-2 value was
what actually took effect at runtime, and confirmed the next
projected-config check showed `publishedVersion: 2, appliedVersion: 2`
with matching hashes and `Status: UpToDate`. **Concurrency test**: fired
two simultaneous publish requests after another settings change;
confirmed exactly one `versions/3.json` was created (no duplicate, no
data loss) and the losing request's response correctly reported
`"Configuration unchanged since version 3."` rather than erroring or
silently overwriting. **Legacy fallback test**: confirmed in the same
Agent run that the real Kitchen Camera, the real motion sensor, and the
real smart plug - none ever republished through the new pipeline - all
still loaded and processed normally via their legacy flat blobs
alongside the new manifest-driven test device. All test artifacts
cleaned up after: every blob (legacy, versioned, manifest) deleted, the
throwaway Device retired, the test API key revoked, local func host
stopped, and one final clean Agent run to drain a restart command left
queued by the concurrency test (the shared test Capture Agent identity
is reused across every ADR this session, not a throwaway). Backend
dotnet build clean across Vivnest.Cloud/Vivnest.Cloud.Functions/
Vivnest.Agent throughout; dashboard `tsc -b && vite build` clean.

**Explicitly deferred, not started** (confirmed with the user as
separate future passes): rollback (publish a new version whose content
matches an older one) and its audit trail (`CreatedBy`/`ChangeReason`);
true Agent-side periodic self-restart polling independent of a publish
event (today the loop still starts from an explicit publish - an
Agent that's been running since before a publish only picks it up via
the auto-enqueued restart, not on its own timer); a full migration of
existing production blobs to the new layout (deliberately never
attempted - "run alongside," not a forced migration); per-device (as
opposed to Agent-level) failure attribution.

## ADR-070 — Configuration Lifecycle Pass 2: last-known-good fallback, rollback, offline-catch-up verification

**Why:** A follow-up review of ADR-069 against the original 30-section
Phase 6D spec found the spec's own "most critical" requirement (section
14/28 - "do not replace a known-good configuration with a broken one")
wasn't actually satisfied: `Vivnest.Agent/Program.cs`'s
`TryProcessDeviceBlob` just dropped a device entirely on
`UnsupportedConfigurationSchemaException`, with nothing to fall back to.
Alongside that real gap, rollback (spec section 21, never built) and the
offline-while-config-changes scenario (section 20/29, plausible by design
but never actually tested under ADR-069's versioned model) were both
closed out in the same pass.

**Local last-known-good cache (the correctness fix)** - scoped to device
configs only: `TryLoadRemoteConfigAsync` (the Agent-level
`AiClassification` loader) has no schema-adapter/validation step at all,
just a generic catch-all that already degrades to local config, so
there's nothing to harden there. `TryProcessDeviceBlob` now writes the
raw (pre-adapt, pre-secrets-merge) device document to
`{AppContext.BaseDirectory}/config-cache/devices/{deviceId}.json` after
every successful load - the same directory class as `common-config.json`
and the secrets-sibling files, so no new volume mount is needed; it
survives a restart-command-triggered `docker restart` (same container,
per `CommandPollingWorker`'s own comment) but not a full redeploy (a new
container - no worse than today's cold-start behavior). On
`UnsupportedConfigurationSchemaException`, before dropping the device,
the cache is checked: if present, it's re-parsed and re-adapted
(`TryLoadCachedDeviceConfig`) and used instead, with `loadErrors` (and
therefore `AgentHeartbeat.ConfigurationLoadError`, ADR-068) still noting
the failure so Admin sees `Failed` status even though the device kept
running - just phrased as "...continuing on cached last-known-good
config" rather than a blunt drop. No cache (e.g. a device's very first
published version is already broken) falls through to the original
drop-the-device behavior unchanged - there's nothing to fall back to yet.

**Rollback** - `IDeviceRuntimeConfigurationPublisher`/
`IAgentRuntimeConfigurationPublisher` gained `RollbackAsync(tenant, id,
targetVersion)`. Unlike `PublishAsync`, content is read verbatim from
`versions/{targetVersion}.json` (a 404 there is a real error, not a
fallback case) rather than re-projected from live Admin state - a
rollback must reproduce exactly what that version contained, even if
live settings have since drifted for unrelated reasons. The
write-version-blob → overwrite-manifest → overwrite-legacy-flat-blob →
update-metadata-row retry cycle that `PublishAsync` had inline
(ADR-069) is now factored into a shared private `WriteVersionAsync`
helper (`Vivnest.Cloud.Admin.VersionWriteResult`) both methods call -
`PublishAsync` passes content computed from the live projection,
`RollbackAsync` passes content read from the old version blob, and a new
`bypassNoOpCheck` flag lets rollback always create a new version even
when content happens to already match what's published (a deliberate
rollback is a real event worth recording, matching the spec's own
"create a new desired version 20" framing - it doesn't reuse
`PublishAsync`'s hash-based no-op guard). New
`DeviceEventTypes.ConfigRolledBack`/`AgentEventTypes.ConfigRolledBack`
audit event types (payload includes `rolledBackFromVersion`) let the
audit trail (spec section 22) distinguish a rollback from a routine
publish - previously both would have shown as indistinguishable
`ConfigPublished` rows. New routes mirror `publish-config` exactly, one
extra path segment: `POST
devices-registry-admin/{deviceId}/rollback-config/{targetVersion:int}`,
`POST agents-registry-admin/{agentId}/rollback-config/{targetVersion:int}`.
Dashboard: a minimal number-input + "Roll back" button in
`ProjectedConfigModal.tsx`/`AgentProjectedConfigModal.tsx`, next to the
existing Sync Status block, reusing `publishResult`'s own rendering for
the outcome - no new list-versions endpoint, since the Admin already
sees the Published/Applied `v{n}` labels in the same modal (kept
deliberately minimal per the spec's own "don't build a full
diff/deployment UI yet," sections 22/25).

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz", standing test Capture Agent `5d6c8d6f-...`), three
scenarios:
- **Fallback**: published a good version 1 (`LivenessIntervalMinutes: 5`)
  for a throwaway Motion Sensor device, ran the real `Vivnest.Agent`
  process and confirmed the cache file was written; hand-uploaded a
  version 2 with an unsupported `SchemaVersion` directly to blob storage
  (bypassing the publisher, which can't itself produce an invalid
  version) and re-ran the Agent - console output showed "Continuing on
  cached last-known-good config," the device kept running on the cached
  `LivenessIntervalMinutes: 5` (confirmed via the worker's own "sleeping
  for 00:05:00" log line), and `GetDeviceProjectedConfig` showed
  `Published: v2, Applied: v1, Status: Failed` with the new fallback
  message - the exact Desired≠Applied/Failed shape the spec's acceptance
  test (section 28) describes. A second throwaway device with the same
  bad-schema version but **no** prior successful load correctly still
  degraded to the original "Skipping" (dropped) behavior - confirmed the
  fix only changes behavior when a cache actually exists.
- **Rollback**: published v1 (`LivenessIntervalMinutes: 10`) then v2
  (`20`) for a second throwaway device, rolled back to v1 via a real curl
  call - confirmed a **new** v3 was created with v1's exact content and
  hash, v1's own blob stayed byte-for-byte unchanged (MD5-verified before
  and after), `current.json` advanced to v3, the real Agent process
  picked it up (`LivenessIntervalMinutes: 10` took effect, not `20`), and
  the `tblDeviceEvents` audit row for v3 was a `ConfigRolledBack` event
  (`rolledBackFromVersion: 1`) distinct from the plain `ConfigPublished`
  rows for v1/v2. Also exercised end-to-end through the real dashboard UI
  (not just curl): opened the device's Projected Config modal, typed a
  target version into the new Rollback control, clicked "Roll back," and
  confirmed the rendered result showed the rolled-back settings and
  "Published to the real device-config file."
- **Offline-while-changed**: with the Agent not running, published two
  more versions in a row (v4 `LivenessIntervalMinutes: 30`, v5 `45`);
  starting the Agent showed it loaded v5 directly (`sleeping for
  00:45:00`), never touching v4 - confirmed the existing manifest-first
  design (current.json always points at latest) already satisfies this
  scenario correctly, exactly as expected by construction; no code change
  was needed here.

Backend `dotnet build` clean across Vivnest.Core/Cloud/Cloud.Functions/
Agent; dashboard `tsc -b && vite build` clean. All test artifacts
(throwaway devices, hand-uploaded blobs, the test API key) to be cleaned
up after documentation.

**Still not done from the original spec** (unchanged from ADR-069's own
list, confirmed still out of scope for this pass): true Agent-side
periodic self-restart polling independent of a publish event; a full
migration of existing production blobs to the new layout; per-device (as
opposed to Agent-level) failure attribution - the `ConfigurationLoadError`
surfaced via a device's `SyncStatus` is still the Agent's single
most-recent coarse message, which can show a different device's error if
multiple devices had issues in the same load cycle (observed directly
during this pass's own verification, a pre-existing ADR-068 design
choice, not a new regression).

## ADR-071 — Phase 7 Pass 1: AgentInstallation provisioning lifecycle + install tokens

**Why:** Phase 7 ("Agent Deployment & Provisioning") asks how an Agent
actually gets installed on a Machine and stays operationally connected to
Admin, without an administrator hand-typing every `RuntimeAgentId` and
config file forever. Three sub-areas were assessed against the real code
first: Machine replacement was already fully built
(`AgentInstallationManagementService.MoveAsync`, ADR-053), the Docker
deploy pipeline already worked end to end (`agents/{agentId}/deploy` →
queue → `Vivnest.Agent.Updater`), but `AgentInstallationStatus` only had
`Active`/`Removed` (no real lifecycle) and there was no self-registration
path at all. Sequenced into three passes; this is the foundation pass -
the lifecycle state machine and the credential a not-yet-trusted process
will use to register, with no new endpoints wired up to them yet (Pass 2).

**`AgentInstallationStatus`** (`Vivnest.Core/Enums/`) now models the
*provisioning* lifecycle only - `Pending` → `Installing` → `Installed` →
`Active`, with `Updating` as a re-entry from `Active` for a later version
bump, and `Decommissioned` (renamed from `Removed` - same terminal
meaning, matches the spec's own vocabulary; no migration story needed,
this is early-stage dev/demo data) as the terminal state. Deliberately
does **not** add a stored `Offline` value - `HealthMonitorService.IsAgentOffline`
already computes Online/Offline live from heartbeat staleness on every
read (confirmed by reading it directly, not assumed), and duplicating that
as stored installation state would just create a second, driftable source
of truth for the same fact. `AgentInstallation` (`Vivnest.Core/Domain/`)
replaced its single `Remove()` method with five named transitions -
`Register()`, `MarkInstalled()`, `MarkActive()`, `MarkUpdating()`,
`Decommission()` - matching one real lifecycle event each; only
`Decommission()` has a real caller so far (`Uninstall`/`Move`'s
retire-the-old-installation step), the other four are Pass 2's job to
call from the registration endpoint and the heartbeat pipeline. A real,
non-cosmetic bug this rename exposed:
`AzureTableAgentInstallationStore.GetActiveByAgentAsync`/
`GetActiveByMachineAsync` filtered literally on `Status == "Active"` -
with `Pending` now the default status for a brand-new installation, that
query would have missed every not-yet-active installation entirely,
letting an admin create two simultaneous installations for the same
Agent (breaking the "at most one active installation per Agent"
invariant ADR-053 established). Fixed by filtering on `Status !=
"Decommissioned"` instead - "active" now means "the current installation,"
not literally the `Active` enum value - and verified live (see below) that
a second Install attempt against a `Pending` installation is correctly
rejected with 409.

**Install tokens**: new `AgentInstallationTokenEntity` (new table
`tblAgentInstallationTokens`) mirrors `ApiKeyEntity`'s exact shape -
`PartitionKey` is the token's own SHA-256 hash (`ApiKeyHasher.Hash`,
reused directly rather than a second hashing helper), never the raw
value, giving Pass 2's registration endpoint an O(1) lookup with **no
tenant context at all** - the token itself is the trust, the same
reasoning `ApiKeyAuthenticator` already established for tenant keys, just
short-lived (24h `TokenLifetime`) and single-use (`Used` flag, set not
deleted, preserving a real audit trail of exactly when a Machine
registered) rather than long-lived. New `IInstallTokenService`/
`InstallTokenService` (`Vivnest.Cloud/Auth/`) mirrors
`ApiKeyManagementService.CreateAsync` exactly: random 32-byte secret,
hash stored, raw value returned exactly once in
`AgentInstallationCreationResult` (a new DTO wrapping the existing
`AgentInstallationDto` alongside `InstallToken`/`InstallTokenExpiresUtc`) -
there is no endpoint that can retrieve it again after that response, same
one-time-reveal convention `CreateApiKeyResponse` already established.
`InstallAsync`/`MoveAsync` (`AgentInstallationManagementService`) now
always create the new installation `Pending` and always issue a token -
even for `Move`, since the whole point of a Machine replacement is a
genuinely different physical box that has never had an Updater run on it
before, so it needs its own registration exactly like a first-ever
install does.

**Verified for real against live Azure data** (stvivnestagent2, tenant
"Sana" / site "1Fitz"): created a throwaway Agent + two Machines; Install
returned `Status: Pending` with a one-time token; confirmed
`active-by-agent` correctly finds the `Pending` installation (the store
fix above); confirmed a second `Install` on the same Agent is rejected
with `409` while the first is still `Pending` (proving the invariant holds
across the new lifecycle, not just the old `Active`-only one); confirmed
`Uninstall` sets `Decommissioned` with `RemovedUtc` set,
`active-by-agent` then `404`s, and a fresh `Install` is allowed again;
confirmed `Move` retires the old installation to `Decommissioned` while
creating a new `Pending` one with its own fresh token, `by-agent` showing
the complete, un-mutated history of all three installations
(`Decommissioned` → `Decommissioned` → `Pending`); queried
`tblAgentInstallationTokens` directly and confirmed each row's
`PartitionKey` is a real SHA-256 hex hash (not the raw token, which was
never persisted anywhere) with the correct `InstallationId`/`TenantId`/
`SiteId`/`ExpiresUtc`/`Used: false`. Backend `dotnet build` clean across
Vivnest.Core/Cloud/Cloud.Functions; dashboard `tsc -b && vite build`
clean (only `api.ts`'s types changed this pass - `AgentInstallationStatus`
union extended, `installAgent`/`moveAgent` now return
`AgentInstallationCreationResult`; no UI renders the token yet, that's
Pass 3).

**Explicitly deferred to Pass 2/3** (confirmed with the user as a
3-pass sequence before any code was written): the registration endpoint
itself, `Vivnest.Agent.Updater` self-registration, auto-deploy-on-Install,
the heartbeat hook that actually drives `MarkActive()`/`MarkInstalled()`,
real image-tag version enforcement, and any dashboard surfacing of the
new lifecycle states or the install token.

## ADR-072 — Phase 7 Pass 2: self-registration, auto-deploy, heartbeat-driven activation

**Why:** ADR-071 built the lifecycle state machine and the install-token
credential but wired nothing to them - this pass closes the actual gap
the spec named: "an administrator manually typing every runtime ID
forever." A fresh Machine can now go from "admin clicks Install" to "a
real Agent is registered, deploying, and confirmed running" with a single
token handed to whoever provisions the box - no admin hand-typing a
`RuntimeAgentId` into a config file to match one hand-typed into the
dashboard.

**Registration endpoint** (`POST agent-installations-admin/register`,
`AgentInstallationsFunction`) is the one route in this entire codebase
with **no tenant `x-api-key` check at all** - deliberately, not an
oversight. The caller (a fresh Updater, before it has any identity Cloud
recognizes) has nothing to present; the install token itself, validated
by `IInstallTokenService.ValidateAndConsumeAsync` (hash lookup, checks
not-`Used`/not-expired, marks `Used` on success so it can never be
replayed even if the caller never finishes), is the entire trust model.
`AgentInstallationManagementService.RegisterAsync` - also deliberately
taking no `TenantContext`, resolving tenant/site purely from the token
row - then: resolves the `Pending` installation, generates a fresh
`RuntimeAgentId` (`Guid.NewGuid()`, Cloud-generated, consistent with
"Cloud remains source of truth") **unless the Agent already has one**
(covers Move onto replacement hardware for an already-registered Agent -
only a genuinely new Agent gets a new identity), persists it via a new
focused `IAgentRegistryManagementService.SetRuntimeAgentIdAsync` (doesn't
require knowing the Agent's Name/Description/FirmwareVersion/Type just to
leave them alone, unlike the existing full `UpdateAsync`), transitions
the installation via `Register()` (Pending→Installing), enqueues a
`DeployCommandQueueMessage` through the existing
`IAgentCommandPublisher`, and returns
`{RuntimeAgentId, InstallationId, TenantId, SiteId, ImageVersion,
StorageConnectionString}` - the connection string comes back too, since
Cloud already knows it, closing the "hand-type everything" gap
completely rather than partially.

**`Vivnest.Agent.Updater`** gains `--installtoken <token>
--registrationurl <url>`, running before `Host.CreateApplicationBuilder`
(a plain `HttpClient`, deliberately outside DI, same as
`ApplySettingsOverridesFromArgs`'s own direct file I/O) so the same run
picks up the assigned identity. On success it writes the `RuntimeAgentId`
into **two** separate files with the same value for two separate
purposes: its own `updater.settings.json` (`Agent:AgentId`, for
`DeployPollingWorker`'s own message filter) and the local
`appsettings.json` it already mounts into the Agent container
(`Agent:TenantId`/`SiteId`/`AgentId`, `Storage:ConnectionString` - what
the real `Vivnest.Agent` process reads, previously always hand-typed).
Then deploys immediately (not waiting for `DeployPollingWorker`'s own
poll tick to pick up the message `RegisterAsync` already enqueued -
that happens too, redundantly but harmlessly, since
`AgentDeployer`'s pull/stop/rm/run is idempotent), and reports back via
`POST .../{installationId}/deploy-complete` (also no tenant key - same
trust model, the Updater still has none at this point, just what
`RegisterAsync`'s own response already handed back) so Cloud can call
`MarkInstalled()` without waiting for the first heartbeat.

**A real robustness gap found during this pass's own verification, not
by review**: the immediate post-registration deploy call was originally
unguarded, exactly like the pre-existing `--install` flag's own deploy
call. For `--install` that's fine (an attended, run-once operator
gesture where a hard failure is a useful, visible signal) - but for
unattended self-registration, letting a transient deploy failure (Docker
not up yet, a network blip) crash the *entire* Updater process would be
strictly worse than falling through to normal queue-polling, which
already has the identical deploy command queued and will retry it on its
own next tick. Wrapped in try/catch, logs a warning, falls through -
confirmed live (see below) that this is exactly what happens.

**Heartbeat-driven activation**: `HealthMonitorService.EvaluateAgentAndNotifyAsync`
gained one best-effort call (wrapped in try/catch, must never break real
notification processing) to a new
`AgentInstallationManagementService.NoteAgentHeartbeatAsync`, which
resolves the admin Agent by `RuntimeAgentId` (new
`IAgentRegistryStore.GetByRuntimeAgentIdAsync` - the reverse lookup
nothing previously needed, since a heartbeat only ever carries the
runtime identity), finds its active installation, and - if it's
`Installing`, `Installed`, **or** `Updating` - calls `MarkActive()`
directly. Deliberately collapses all three mid-provisioning states to
`Active` on a single real heartbeat rather than requiring
`MarkInstalled()` to have landed first: a heartbeat is unambiguous proof
the container is genuinely running regardless of which sub-state
preceded it, which makes the whole lifecycle self-healing against a
missed `deploy-complete` callback instead of fragile to one - confirmed
live (see below), where this pass's own Docker-less environment meant
`deploy-complete` never fired, yet a single real heartbeat still carried
the installation straight to `Active`.

**Verified for real against live Azure data and a real (non-Docker)
process**, end to end: created a throwaway Agent + Machine, `Install`
returned a `Pending` installation and a token; ran the real
`Vivnest.Agent.Updater.exe --installtoken ... --registrationurl ...`
standalone (no Docker) and confirmed the registration call actually
assigned a `RuntimeAgentId`, wrote both `updater.settings.json` and the
mounted `appsettings.json` correctly (`TenantId`/`SiteId`/`AgentId`/
`Storage:ConnectionString` present and correct in the latter), and the
installation reached `Installing`; **Docker itself is unavailable in this
verification environment** (`docker pull` fails with "failed to connect
to the docker API..."), so the actual `docker pull`/`run` step could not
be exercised - confirmed instead that the failure was caught cleanly
(the robustness fix above), logged, and the Updater fell through to
normal operation, where `DeployPollingWorker` picked up the identical
queued command and retried it (also failing the same way, also survived)
- this is real, useful verification of the resilience path even without
Docker, but the pull/run mechanics themselves remain unverified in this
pass and should be spot-checked wherever Docker is actually available.
Ran the real `Vivnest.Agent` process (also standalone, not
containerized) using the exact config the Updater wrote, confirmed a
real `AgentHeartbeat` was published, and confirmed the installation
flipped straight from `Installing` to `Active` afterward - the
heartbeat hook, end to end, for real. Negative cases: a garbage token,
the same token reused a second time, and a token with its `ExpiresUtc`
forced into the past (via direct table edit) all correctly returned
`400 "Invalid, expired, or already-used install token."` and did not
mutate any state.

**A real, unrelated bug surfaced during this verification, not caused by
it**: `Vivnest.Agent.Capabilities.Bridges.HomeAssistant.HomeAssistantCommandSender`'s
constructor unconditionally does `new Uri(settings.BaseUrl,
UriKind.Absolute)`, which throws and crashes the entire host at startup
if `HomeAssistant:BaseUrl` is unset - exactly the config shape a freshly
self-registered Agent has (this pass's own registration response
deliberately writes only `Agent`/`Storage`, no `HomeAssistant` section).
Worked around for this pass's own test file only (a dummy `BaseUrl`);
the real fix belongs to `Vivnest.Agent`, not Phase 7, and has been
flagged as a separate follow-up rather than fixed here.

Backend `dotnet build` clean across Vivnest.Core/Cloud/Cloud.Functions;
`Vivnest.Agent.Updater` build clean. All test artifacts (two throwaway
Agents, two Machines, installations, tokens, the test API key, the
scratch process directories) cleaned up after documentation.

**Explicitly deferred to Pass 3** (unchanged from ADR-071's own plan):
real image-tag version enforcement (`DeployCommandQueueMessage` still
carries no tag, `AgentDeployer` still always pulls `:latest`), and any
dashboard surfacing of the new lifecycle states, install token, or
version status.

## ADR-073 — Phase 7 Pass 3: real image-tag versioning + dashboard surfacing

**Why:** Pass 2 closed the registration/auto-deploy gap but every deploy
still pulled `:latest` unconditionally - there was no way to pin a
specific Agent build, no way to tell a stale Agent from a current one, and
none of Pass 1/2's own new lifecycle state was visible anywhere in the
dashboard. This pass makes `AgentInstallation.ImageVersion` (already
plumbed through Install/Move since ADR-053/071) an actual enforced Docker
tag end to end, and surfaces the result.

**Real image-tag plumbing**: `DeployCommandQueueMessage` gained `string?
ImageVersion` (`null` = today's `:latest` behavior, fully backward
compatible with any in-flight message from before this pass);
`IAgentCommandPublisher.PublishDeployCommandAsync` forwards it;
`AgentDeployer.DeployAsync` takes an optional tag and builds
`{Registry}/{ImageName}:{tag ?? "latest"}` instead of a hardcoded
`:latest`. Both places that ever enqueue a deploy command now resolve the
tag before publishing, from the *same* source (`AgentInstallation.
ImageVersion`), reusing the pattern established in different ways: the
registration endpoint (`AgentInstallationManagementService.RegisterAsync`)
already had `installationEntity.ImageVersion` in hand and just started
passing it through; the pre-existing `POST agents/{agentId}/deploy`
(`AgentsFunction.DeployAgent`) needed a new
`AgentInstallationManagementService.GetActiveImageVersionByRuntimeAgentIdAsync`
- because that route operates in the **RuntimeAgentId identity space**
(resolved via heartbeat, per `AgentQueryService.GetAgentAsync`), not the
admin AgentId space `AgentInstallation` is keyed by, so it has to reverse-
resolve through `IAgentRegistryStore.GetByRuntimeAgentIdAsync` (the same
lookup Pass 2 built) before it can find the active installation at all.
`scripts/build-and-push-agent.ps1` gained `-Version <tag>`: when given, it
tags and pushes both `vivnest-agent:$Version` and `vivnest-agent:latest`
(bakes `$Version`, not the git SHA, into `FirmwareVersion` this time) -
omitting `-Version` keeps today's SHA-`:latest`-only behavior unchanged,
purely additive.

**Version-status computation** (`AgentVersionStatus` enum,
`AgentVersionStatusDto`, `IAgentVersionStatusService`/
`AgentVersionStatusService`) mirrors `ConfigurationSyncStatusService`'s
own shape and reasoning exactly: `NeverDeployed` (no `ImageVersion` set at
all - nothing to compare), `Unknown` (a desired version is set, but no
heartbeat has ever reported a `FirmwareVersion`), `UpToDate`/`Outdated`
(exact `StringComparison.Ordinal` match against the latest
`AgentHeartbeat.FirmwareVersion` - deliberately **not** semver-aware: a
git-SHA build from before this ADR legitimately isn't the same thing as a
real semver `ImageVersion`, and collapsing that into anything but
`Outdated`/`Unknown` would hide a real, meaningful mismatch rather than
surface one). `desiredVersion` is passed in by the caller rather than
re-fetched, same "don't re-derive what the caller already has" convention
`ConfigurationSyncStatusService` follows for its own "Desired." Attached
at the Function layer (`AgentInstallationsFunction`'s four read routes),
not the management service, via a `with { VersionStatus = ... }`
expression - the same split `AgentRegistryAdminFunction` already uses for
attaching `SyncStatus`, keeping the service layer free of concerns that
only exist for the HTTP response shape.

**Dashboard** (`AgentInstallationsAdmin.tsx`): the binary "Installed"/"Not
installed" badge is now the real lifecycle status
(`Pending`/`Installing`/`Installed`/`Updating`/`Active`/`Decommissioned`,
color-mapped - `status-online` only for `Active`, `status-warning` for
every mid-provisioning state, `status-offline` for `Decommissioned`); a
new `VersionStatus` line shows Desired/Running under each row when
present, color-mapped the same way `ConfigurationSyncStatus` is
elsewhere; and Install/Move responses no longer discard `installToken` -
a one-time reveal dialog (mirrors `ApiKeysAdmin`'s own `createdKey` box:
monospace value, copy button, explicit "will never be shown again"
warning, expiry timestamp) now shows it immediately after a successful
Install or Move.

**Verified for real against live Azure data**: created a throwaway
Agent/Machine/Installation via the real local `func` host and a fresh
test API key; confirmed `GET active-by-agent` returns `versionStatus:
{desiredVersion, runningVersion, status}` correctly for all four states -
`NeverDeployed` (installed with no `ImageVersion`), `Unknown` (installed
with `ImageVersion` set, no heartbeat), `UpToDate` (a hand-inserted
`AgentHeartbeat` row with matching `FirmwareVersion`), and `Outdated`
(the same row edited to a different `FirmwareVersion`) - each transition
confirmed by direct table reads/writes against `stvivnestagent2`, not
inferred. Confirmed the queue-message shape by calling the real `POST
agents/{runtimeAgentId}/deploy` route and peeking the real
`agent-deploy-commands` queue: the enqueued message was exactly
`{"AgentId":"pass3-runtime-agent-001","IssuedAtUtc":"...","ImageVersion":
"2.0.0"}` - proving both the tag plumbing and the RuntimeAgentId→admin-
AgentId resolution work correctly together, for real, not just by code
reading. Backend `dotnet build` clean across
Vivnest.Core/Cloud/Cloud.Functions; `Vivnest.Agent.Updater` build clean
(the `AgentDeployer`/`DeployPollingWorker`/`Program.cs` tag-forwarding
changes). Dashboard `tsc -b && vite build` and `oxlint` both clean (only
pre-existing, unrelated warnings). Browser-verified against the live
dashboard: status badges, the Desired/Running version line, and the
install-token reveal dialog all render correctly against the same live
test data, confirmed via a real Move action through the UI (not just the
API) that both the version-status recomputation and the token reveal
fire correctly end to end.

This closes all three passes of Phase 7 (ADR-071/072/073) - the full
Machine/Agent/AgentInstallation provisioning lifecycle, self-registration,
auto-deploy, and real version tracking/enforcement, exactly as scoped in
the originally approved plan, with no further deferrals.

## ADR-074 — Phase 8 Pass 1: shared Agent health resolver

**Why:** A research pass ahead of Phase 8 ("Operational Management") found
that Device health was already a real, tiered, independent computation
(`DeviceStatusResolver` → `Online`/`Warning`/`Offline`/`Error`/`Unknown`),
but Agent health was still purely binary
(`HealthMonitorService`'s private `IsAgentOffline`, mirrored a second time,
by hand, inside `AgentQueryService.ToDto` for the dashboard - with a
comment on the second copy admitting it "mirrors" the first). This pass
gives Agent the same real tiering Device already has, and - just as
importantly - collapses the duplication into one shared source of truth,
the same fix `IDeviceStatusResolver` already represents for Device.

**Reused vocabulary, not a new one**: the new tier is expressed as the
*existing* `DeviceHeartbeatStatus` enum (`Online`/`Warning`/`Offline`/
`Unknown` - no `Error`, nothing self-reports that for an Agent today), not
a new `Healthy`/`Degraded` type. `Vivnest.Dashboard/src/StatusFilterChips.tsx`
hardcodes a `STATUS_ORDER` array and `Overview.tsx` an `ATTENTION_SEVERITY`
map, both already shared by the Agent and Device lists - a second
vocabulary would mean two parallel string sets flowing into the same
components. Reusing Device's own enum means Agent's new tiered status
needed **zero dashboard changes** - `AgentRow.tsx` already builds its CSS
class as `` `status-dot-${agent.status.toLowerCase()}` `` generically, so
it started rendering `Warning` correctly the moment the backend started
sending it, no frontend commit involved.

**New `Vivnest.Cloud/Interfaces/IAgentStatusResolver.cs` +
`Vivnest.Cloud/Rules/AgentStatusResolver.cs`** mirror `IDeviceStatusResolver`/
`DeviceStatusResolver`'s exact placement and "shared by HealthMonitorService
(notifications) and the query service (dashboard) so they can't drift"
reasoning, down to the same file split (interface in `Interfaces/`,
implementation in `Rules/`). `Determine(AgentHeartbeatEntity?)` returns
`Unknown` for a missing/null entity, `Online` up to
`HeartbeatInterval × AgentDegradedMultiplier`, `Warning` up to
`HeartbeatInterval × AgentOfflineMultiplier`, else `Offline`. Two new
`HealthMonitorOptions` fields (`AgentDegradedMultiplier` = 2,
`AgentOfflineMultiplier` = 5) carry the thresholds - configurable, not
hardcoded, per the spec's own instruction. The pre-existing
`AgentStaleMultiplier` (3×, `DeviceStatusResolver`'s own cascade check for
"is this device's owning agent too stale to trust") is untouched -
different question, already asymmetric from the agent's own check before
this pass, staying that way per its own existing comment.

**Real, deliberate behavior change**: today's Agent-offline notification
threshold was `1× HeartbeatInterval` with no multiplier at all - the
tightest, most trigger-happy threshold anywhere in the system (a single
missed heartbeat fired a Telegram alert). The new threshold moves that to
`5×`, with a real `Warning` state visible in the two bands between - fewer
false-positive alerts, more visibility into partial degradation. Called
out explicitly in the approved plan before building, not a silent
side-effect.

`HealthMonitorService.EvaluateAgentAndNotifyAsync` drops the private
`IsAgentOffline` method entirely and calls `IAgentStatusResolver` instead;
`OfflineDetectionRule`/`RecoveryDetectionRule` (unchanged - they already
only key off `Offline`/`Error` and `Online` respectively) mean `Warning`
naturally falls through without firing any notification, without needing
new gating logic. `AgentQueryService.ToDto` drops its own hand-mirrored
copy and calls the same resolver, exactly how `DeviceQueryService.ToDto`
already calls `IDeviceStatusResolver`.

**Verified for real against live Azure data**, across two genuine 5-minute
health-check timer ticks (not simulated): inserted a throwaway Agent
heartbeat row and, via direct `az storage entity replace` edits to
`LastHeartbeatUtc`, drove it through all three reachable bands -
`GET /agents/{agentId}` correctly returned `Online` (elapsed ~50s, 1-min
interval), `Warning` (elapsed ~3min), and `Offline` (elapsed ~10min) at
each step, with `StatusSinceUtc` computed correctly for each (recovery/
start time for `Online`, last-confirmed-alive heartbeat for `Warning`/
`Offline`). Then verified the full notification pipeline end to end
against the real, unmodified `HealthMonitorTimerFunction` (not a manual
trigger): parked the entity in the `Offline` band and confirmed
`NotificationState` flipped from `None` to `OfflineNotified` at the real
next 5-minute tick; then moved it back into the `Online` band (with a
wide `HeartbeatInterval` so a single static timestamp stayed fresh across
the next tick) and confirmed `NotificationState` flipped back to `None`
with `LastRecoveredUtc` set at the following real tick - `AgentOffline`
then `AgentRecovered`, genuinely detected by the unmodified production
timer schedule, not a shortcut. `dotnet build` clean across
`Vivnest.Core`/`Vivnest.Cloud`/`Vivnest.Cloud.Functions`. Test heartbeat
row and API key cleaned up (deleted/revoked) after verification.

## ADR-075 — Phase 8 Pass 2: config/version status on the main Agent/Device views

**Why:** ADR-068 and ADR-073 already compute Desired-vs-Applied config
status and Desired-vs-Running software version correctly - but only via
the separate projected-config endpoints, reached through a dedicated modal
click, not anywhere near the main `GET /agents`/`GET /devices` list an
operator actually looks at first. This pass surfaces both directly on
`AgentSummaryDto`/`DeviceSummaryDto`, closing the gap the spec's own list
mockup calls for (Status/Config/Version columns together).

**A cheaper path was found, not assumed**: `ConfigurationSyncStatusService.
GetAgentStatusAsync`/`GetDeviceStatusAsync` require a full *projected*
document (running the whole capability-projection pipeline) - but reading
the actual method bodies showed that's only ever needed to resolve
identity and gate on `Warnings`; the real Published-vs-Applied comparison
only touches fields already sitting on the heartbeat row
(`ConfigurationVersion`/`Hash`/`PublishedUtc`/`LoadError`) plus one blob
read. Two new methods, `GetAgentStatusFromHeartbeatAsync`/
`GetDeviceStatusFromHeartbeatAsync`, reuse the class's own existing
private `TryReadManifestAsync`/`TryReadPublishedUtcAsync`/`BuildStatus`
helpers directly against a heartbeat entity (`RowKey` already *is* the
runtime id both writers stamp there, per `ConfigurationSyncStatusService`'s
own long-standing comment - no extra registry lookup needed), skipping
the projection precondition entirely. Return type is non-nullable
(`ConfigurationSyncStatusDto`, not `?`) - there's no "incoherent
projection" case to gate on here, worst case is a real `NeverPublished`.

**Same shape for version status**: `IAgentVersionStatusService` gained
`GetStatusForRuntimeAgentAsync(tenant, runtimeAgentId, runningVersion, ct)`
alongside the existing `GetStatusAsync(tenant, agentId, desiredVersion,
ct)` - the existing method expects the *admin* AgentId and re-fetches the
heartbeat itself; `AgentQueryService` is already iterating heartbeat rows
(keyed by RuntimeAgentId) and already has `FirmwareVersion` in hand, so
the new overload resolves only the missing piece (desired version, via
the existing `AgentInstallationManagementService.
GetActiveImageVersionByRuntimeAgentIdAsync` reverse lookup from ADR-073)
instead of redundantly re-fetching a heartbeat the caller already read.
Both entry points now share one `BuildStatus` comparison helper inside
`AgentVersionStatusService`, factored out of the original `GetStatusAsync`
rather than duplicated.

`AgentQueryService`/`DeviceQueryService` compute these per row, in both
the list and single-entity methods - real per-row cost (one blob read for
config status, one table lookup chain for version status), accepted at
this scale (a handful of agents/devices) the same way
`DeviceCapabilitiesQueryService`'s own O(N) scan and
`AgentInstallationsAdmin`'s per-agent `Promise.all` already are, per the
spec's own "don't overbuild" instruction and this codebase's established
convention of computing derived state live rather than adding a
projection/cache table ahead of an actual performance problem.

**Verified for real against live Azure data**: `GET /agents` against the
real local-dev tenant correctly returned `versionStatus.status:
"Outdated"` for the real running Capture Agent (`desiredVersion: "1.0"`
vs `runningVersion: "local-dev"`, matching what Pass 3 of Phase 7 already
showed in the dashboard) and `"NeverDeployed"` for an agent with no active
installation. For `configurationStatus`, inserted a throwaway
`DeviceHeartbeatEntity` row and a matching real manifest blob
(`device-config/{id}/current.json`) in Azure Blob Storage, confirmed
`GET /devices/{id}` returned `"UpToDate"` with the correct
`publishedVersion`/`appliedVersion`/`configurationHash`; then edited the
heartbeat's own `ConfigurationVersion` behind the published version and
confirmed the same call correctly flipped to `"Pending"` with the mismatch
visible in both version numbers - both branches proven against a real
blob read and a real table row, not inferred from code reading alone. Hit
and fixed one test-tooling artifact along the way (not an app bug): `az
storage entity insert` writes bare `false`/`5` as plain strings/int64
without explicit `@odata.type` hints, which `Azure.Data.Tables`
correctly rejects on deserialize since the real entity's `bool`/`int`
fields expect real Edm types - fixed by adding explicit
`@odata.type=Edm.Boolean`/`Edm.Int32` hints to the test command, same
fix already used once before in ADR-072's own verification. `dotnet
build` clean across `Vivnest.Core`/`Vivnest.Cloud`/`Vivnest.Cloud.Functions`.
Test heartbeat row, manifest blob, and API key all cleaned up (deleted/
deleted/revoked) after verification.

**Deferred to later passes** (unchanged from the approved plan):
lifecycle-vs-operational separation (a Disabled device still shows
`Offline` rather than `NotApplicable`), Machine-level aggregation,
persisted operational events, and any dashboard rendering of the new
`ConfigurationStatus`/`VersionStatus` fields - both are present in the
API response today but not yet shown anywhere in the UI.

## ADR-076 — Phase 8 Pass 3: lifecycle/operational separation + Machine status

**Why:** The main Agent/Device list (`GET /agents`/`GET /devices`) is
driven entirely by heartbeat rows, never cross-referenced against the
Admin registry's lifecycle status - a Disabled device or Inactive agent
whose last heartbeat row is still sitting in the table (heartbeat rows
are never deleted) would keep showing a misleading `Offline` forever,
exactly the failure mode the spec calls out by name (`DeviceStatus =
Disabled`, `OperationalStatus = N/A`, never `Offline`). Separately,
Machine status was a manually-set Admin field, never derived from what's
actually installed on it - this pass closes both gaps.

**`DeviceHeartbeatStatus` gains a 6th value, `NotApplicable`** - reused
across Agent/Device/Machine rather than a separate enum per entity, same
"one shared vocabulary, not three parallel ones" reasoning ADR-074
already established for Agent's own tiering. `AgentQueryService`/
`DeviceQueryService` each gained a new `IAgentRegistryStore`/
`IDeviceRegistryStore` dependency and now do **one batch fetch** of the
tenant's registry rows per list call (a dictionary keyed by
`RuntimeAgentId`/`RuntimeDeviceId`), not a per-row lookup - `Status` is
forced to `NotApplicable` when the matching registry row is `Inactive`
(Agent) or `Disabled`/`Retired` (Device); a new `LifecycleStatus` field
carries the raw Admin value alongside it, always as its own field, never
collapsed into `Status` - so a UI can show *both* "why this device isn't
being monitored" and "what its lifecycle actually is" at once, per the
spec's own example.

**Machine status is derived, never stored**: a new
`AgentInstallationManagementService.GetMachineOperationalStatusAsync`
(placed here, not `MachineManagementService`, since it needs the exact
Machine→installations→Agent→RuntimeAgentId→heartbeat chain this service
already owns) walks the Machine's *active* installations
(`GetActiveByMachineAsync`, already existed), resolves each one's Agent
and heartbeat, and runs each through `IAgentStatusResolver` (ADR-074) -
`Unknown` if nothing's installed, `Online` only if every installed Agent
agrees, `Offline` only if every one does, `Warning` for any real mix.
Deliberately **not** "one offline Agent = Machine offline" - a Machine
can host several Agents, and one going down shouldn't hide that the
others are fine, per the spec's own worked example. Attached to
`MachineDto.OperationalStatus` at the Function layer
(`MachinesFunction`'s `WithOperationalStatusAsync`, a `with { ... }`
expression), the exact same pattern ADR-073 used for
`AgentInstallationDto.VersionStatus` - `MachineManagementService` itself
stays free of a concern that only exists for the HTTP response shape.

**A real bug found during this pass's own verification, not by
review**: `MachineDto.OperationalStatus` is the first place
`DeviceHeartbeatStatus` is ever serialized as the enum itself - every
other consumer (`DeviceSummaryDto.Status`, `AgentSummaryDto.Status`)
already stores it as `entity.Status.ToString()`, a plain string. Without
a `[JsonConverter(typeof(JsonStringEnumConverter))]` on the enum, the
first real API response came back as `"operationalStatus": 3` - a bare
integer - confirmed live, not assumed, then fixed. Exactly the same gap
`AgentVersionStatus`/`ConfigurationSyncStatus` already hit and fixed the
same way when they were first introduced.

Dashboard: `StatusFilterChips.tsx`'s `STATUS_ORDER` gained
`NotApplicable` (a real filter chip now appears whenever any Disabled/
Retired/Inactive entity exists - confirmed live against real tenant data,
4 real devices in the standing dev tenant already had this state);
`Overview.tsx`'s `ATTENTION_SEVERITY` was deliberately **not** touched -
`NotApplicable` should never trigger "needs attention," it's an
intentionally-inactive entity, not a problem. New muted/gray CSS
(`.status-notapplicable`, `.icon-badge-notapplicable`,
`.status-dot-notapplicable`, `.row-thumbnail-notapplicable`) reuses the
same palette `-unknown` already uses - deliberately not a distinct color,
since both mean "no live signal to show" and the adjacent
`LifecycleStatus` text is what explains why. `MachinesAdmin.tsx` shows
`operationalStatus` and `status` as two separate badges side by side,
reusing the existing `status-*` CSS classes directly via
`` `status-${operationalStatus.toLowerCase()}` `` - no new class map
needed, since `MachineOperationalStatus` reuses the exact
`DeviceHeartbeatStatus` vocabulary.

**Verified for real against live Azure data**: created a throwaway
Device (linked via `runtimeDeviceId` to a stale heartbeat row) and
confirmed it read `Offline` before linking a lifecycle status and
`NotApplicable` immediately after setting the registry entry to
`Disabled`; same test repeated for a throwaway Agent set to `Inactive`.
For Machine aggregation: a fresh Machine with no installations read
`Unknown`; installing one Agent with a stale heartbeat flipped it to
`Offline`; bringing that Agent's heartbeat current flipped it to
`Online`; installing a *second* Agent with a stale heartbeat onto the
same Machine flipped the result to `Warning`, not `Offline` - proving the
"mixed, not worst-case" aggregation rule for real, not just by code
reading. `dotnet build` clean across `Vivnest.Core`/`Vivnest.Cloud`/
`Vivnest.Cloud.Functions`; dashboard `tsc -b && vite build` + `oxlint`
both clean (only pre-existing, unrelated warnings). Browser-verified: the
Machines list renders both badges correctly against live data (including
several real pre-existing Machines with genuine mixed-health `Warning`
states), and the Devices list's new `NotApplicable` filter chip works
against real tenant data. All test artifacts (heartbeat rows, registry
entries, installations, install tokens, API key) cleaned up; the
throwaway test Machine itself was decommissioned (no delete route exists
for Machine, same as every prior pass's cleanup convention).

**Deferred to Pass 4** (unchanged from the approved plan): persisted
operational events (`AgentOffline`/`AgentRecovered`/`DeviceOffline`/
`DeviceRecovered`/`ConfigurationApplyFailed` as real `AgentEvent`/
`DeviceEvent` rows, not just Telegram messages), and dashboard rendering
of `ConfigurationStatus`/`VersionStatus` on the Agent/Device list and
detail views (both fields have existed in the API response since
ADR-075, still not shown anywhere in the UI).

## ADR-077 — Phase 8 Pass 4: persisted operational events + dashboard surfacing

Final pass of Phase 8. Two independent halves: real `AgentEvent`/
`DeviceEvent` rows for the offline/recovery/config-failure transitions
`HealthMonitorService` already alerts on via Telegram, and dashboard
rendering of the `ConfigurationStatus`/`VersionStatus` fields Pass 2
(ADR-075) added to the API but never surfaced in the UI.

**Persisted events reuse `AzureTableStore<T>` directly, not
`IAgentEventWriter`/`IDeviceEventWriter`**: those interfaces live in
`Vivnest.Infrastructure`, but `Vivnest.Cloud.csproj` only references
`Vivnest.Core` - confirmed by reading the `.csproj`, not assumed. Rather
than add a new cross-project reference for this one call site,
`HealthMonitorService` now constructs its own
`AzureTableStore<DeviceEventEntity>`/`AzureTableStore<AgentEventEntity>`
in its constructor from the same `TableServiceClient`/
`IOptions<TablesOptions>` it already receives - mirroring
`DeviceRuntimeConfigurationPublisher`'s existing precedent for writing
event rows without the Infrastructure-layer writer abstraction. New
`AgentEventTypes.AgentOffline`/`AgentRecovered`/`ConfigurationApplyFailed`
and `DeviceEventTypes.DeviceOffline`/`DeviceRecovered` constants; each
existing offline/recovery `_notifications.DispatchAsync` call in
`EvaluateAndNotifyAsync`/`EvaluateAgentAndNotifyAsync` gained one
additional persist call right alongside it, gated by the exact same
`NotificationState` transition - additive persistence, not a new
notification pipeline.

**No `DeviceEventTypes.ConfigurationApplyFailed`, deliberately** -
`ConfigurationLoadError` only ever lives on the Agent's own heartbeat,
never per-device (per `ConfigurationSyncStatusService`'s own existing
comment), so persisting it as a Device event would fan a single Agent-
level failure out across every Device that Agent owns. Reasoned from the
exact same precedent already in this file: `EvaluateAndNotifyAsync`'s
`agentCascade` early-return, which avoids the identical "one Agent
problem becomes N redundant Device events" outcome for `DeviceOffline`.
Built the Agent-level event only.

**New `LastNotifiedConfigurationLoadError` field** (`AgentHeartbeatEntity`,
`string?`) gates `ConfigurationApplyFailed` the same way
`NotificationState` gates Online/Offline - fire once when
`ConfigurationLoadError` transitions from unset (or a different value) to
a new value, not on every 5-minute health-check tick. Kept as an
independent field rather than folded into `NotificationState`, since
Online/Offline and ConfigurationApplyFailed are independent conditions
that can co-occur or diverge (an Agent can be Online with a bad config,
or Offline with none).

**A real concurrency bug found live, not by review**: `AzureTableStore<T>.
UpdateAsync` discarded the Azure Table Storage response's new ETag
instead of writing it back onto the in-memory entity. Harmless as long as
an entity is only updated once per request - but
`EvaluateAgentAndNotifyAsync` now calls `UpdateAsync` on the same
in-memory `AgentHeartbeatEntity` twice in one method (once via
`UpdateNotificationStateAsync`, once via
`UpdateLastNotifiedConfigurationLoadErrorAsync`); the second call's now-
stale ETag was rejected by Azure with a 412, silently caught and logged
by the per-agent `try/catch` already in `HealthMonitorService.RunAsync`,
with no visible symptom except the field never actually clearing.
Confirmed live: left a throwaway Agent's `ConfigurationLoadError`
resolved but the notified-error gate still stale, watched the next real
health-check tick fail to clear it, then fixed `AzureTableStore<T>.
UpdateAsync` to capture and write back `response.Headers.ETag`, rebuilt,
restarted the local func host, and confirmed at the following tick that
the field cleared correctly - proven by direct re-observation, not
inferred from the fix alone. Fix is three lines, changes nothing for any
existing single-update call site.

**Dashboard**: `AgentDetail.tsx`/`DeviceDetail.tsx` gained a Configuration
metric cell (status badge + Desired/Applied version text) and, for Agent
only, a Software cell (Desired/Running version text) - both reusing the
`ConfigurationStatus`/`VersionStatus` DTOs Pass 2 already put on the wire,
no new fetch. `AgentRow.tsx`/`DeviceRow.tsx` gained small inline "cfg"/
"ver" indicators next to the existing status dot, shown only when the
status isn't `UpToDate`/`NeverPublished`/`NeverDeployed` - a healthy row
stays uncluttered, matching `Overview.tsx`'s own "surface problems, not
everything" convention. `Overview.tsx` gained a `configRollup`/
`versionRollup` summary line ("Configuration: X/Y up to date · Software:
X/Y up to date"), excluding `NeverPublished`/`NeverDeployed` from the
denominator for the same reason those states don't count as "out of
date" anywhere else in Phase 8. `EventsFeed.tsx`/`DeviceEventList.tsx`
needed no changes at all - confirmed live that they already render
arbitrary event-type strings generically (`ConfigPublished`/
`CameraCaptured`/`MotionSensorReadingFailed` all render identically today
by type + entity + timestamp), so the five new event types appear
automatically once persisted.

**Verified for real against live Azure data**: hand-edited throwaway
Agent/Device heartbeat rows via `az storage entity replace` to force
Offline, then Online, transitions and confirmed real `AgentOffline`/
`AgentRecovered`/`DeviceOffline`/`DeviceRecovered` rows appeared in
`tblAgentEvents`/`tblDeviceEvents` at the correct ticks, gated correctly
by `NotificationState` (no duplicate rows on repeated stale ticks); set a
throwaway Agent's `ConfigurationLoadError` and confirmed a
`ConfigurationApplyFailed` row appeared once, not on every tick, and that
`LastNotifiedConfigurationLoadError` correctly gated re-firing (this is
where the `AzureTableStore<T>.UpdateAsync` bug above was found and
fixed). Hit the same heartbeat-staleness test-data pitfall as earlier
passes twice (a narrow `HeartbeatInterval` going stale again before the
next real tick, and a future timestamp producing a nonsensical `Online`
result) - both are test-data mistakes, not product bugs, fixed by
widening the interval and checking `date -u` for ground truth before
setting timestamps. `dotnet build` clean across `Vivnest.Core`/
`Vivnest.Cloud`/`Vivnest.Cloud.Functions`; dashboard `tsc -b && vite
build` + `oxlint` both clean. Browser-verified against live tenant data:
AgentDetail's Configuration/Software cells, DeviceDetail's Configuration
cell, AgentRow's "ver" indicator, and Overview's rollup line all render
correctly for the standing tenant's real (non-throwaway) Agent/Devices.
All test artifacts (heartbeat rows, five event rows, throwaway API key)
cleaned up.

This closes Phase 8 - all four passes (ADR-074 through ADR-077)
implemented, verified against real Azure data, and committed.

## ADR-078 — Phase 8 follow-up: Healthy/Degraded vocabulary, capability operational status, cross-tenant isolation verification

Three additions requested after Phase 8 was declared closed, on review
against the original spec's own wording.

**Vocabulary rename**: `DeviceHeartbeatStatus.Online`/`Warning` renamed to
`Healthy`/`Degraded` to match the spec's literal wording (`Offline`/
`Error`/`Unknown`/`NotApplicable` were already correct, untouched). Same
enum, same meaning, seven backend usage sites
(`AgentStatusResolver`, `DeviceStatusResolver`, `RecoveryDetectionRule`,
`OfflineDetection`, `HomeAssistantLivenessTracker`,
`AgentInstallationManagementService`'s Machine aggregation) plus the
dashboard's `STATUS_ORDER`/`ATTENTION_SEVERITY`/`MachineOperationalStatus`
literals. `AgentHeartbeatEntity` has no persisted `Status` field at all
(Agent status is always computed, never self-reported) - confirmed by
reading the entity before touching anything, so this half of the rename
was zero-risk. `DeviceHeartbeatEntity.Status` **is** a persisted,
self-reported string, but the standing tenant's real devices all already
read `Unknown` (stale heartbeats, agent idle) at rename time - confirmed
live via `az storage entity query` before assuming a migration was
needed, so none was.

**CSS**: `.status-online`/`.status-warning` (bare, and the `-dot`/
`-badge`/`-thumbnail` prefixed variants) are a shared good/caution color
pair reused by ~15 unrelated dashboard files (`ApiKeysAdmin`,
`ConfigurationSyncStatus`, `CapabilityStatus`, `AgentInstallationStatus`,
`EventSeverity` in `EventsFeed`, etc.) - confirmed by grepping for the
literal class names before touching `App.css`, not assumed from the enum
rename alone. Renaming those shared classes would have broken ~15
unrelated badges for no reason. Instead: `.icon-badge-online`/
`.row-thumbnail-online`/`.status-dot-online` (no other reuse found) were
renamed outright to `-healthy`; `.icon-badge-warning`/`.status-dot-warning`
(still needed by `EventsFeed`'s severity badge and `AgentRow`'s ver/cfg
indicators respectively) were left in place and new `-degraded` selectors
added alongside, reusing the same color tokens; new bare `.status-healthy`/
`.status-degraded` added for `MachinesAdmin`'s `operationalStatus` badge,
which already renders via `` `status-${operationalStatus.toLowerCase()}` ``.

**Capability operational status** (spec's own formula: Running iff Agent
Healthy AND capability enabled AND, where a runtime signal exists, that
signal agrees): new `CapabilityOperationalStatus` enum
(`Running`/`NotRunning`/`Unknown`), new `OperationalStatus` field on
`CapabilityServiceDto`, computed in `DeviceCapabilitiesQueryService` from
the same `AgentSummaryDto` lookup the existing tenant-ownership check
already does (cached, not a second call) plus - for the two capability
types that actually have one -
`DeviceHeartbeatEntity.SinkCleanlinessEnabled`/`ObjectDetectionEnabled`
as the "runtime reports active" signal. Every other capability row
(Camera, MotionSensing, PowerMonitoring, DeviceHeartbeat) has no distinct
runtime flag, so falls through to Running whenever Agent-healthy AND
enabled - matches the spec's own "don't overbuild, later capabilities can
emit richer health information."

**A real bug found live, not by review**: the first implementation
fetched the device's own heartbeat via a direct `GetAsync(partitionKey,
rowKey)` point lookup, assuming `DeviceHeartbeatEntity`'s PartitionKey was
`TenantId|SiteId` like `AgentHeartbeatEntity`'s. It's actually
`TenantId|SiteId|AgentId` - confirmed by reading a real row's
`PartitionKey` in Table Storage. The AgentId segment isn't known ahead of
a lookup by deviceId alone, so the point lookup silently returned `null`
every time regardless of what was written to Azure - every capability
fell through to "no runtime signal" and read `Running` even when the test
data said otherwise, with no exception, no error, nothing to notice
without checking the actual returned value against what was just written.
Fixed to scan-and-filter by `RowKey`, the exact shape
`DeviceQueryService.GetDeviceAsync` already uses for the identical
"find a device heartbeat row by id, AgentId unknown" problem - re-verified
live afterward: forcing `SinkCleanlinessEnabled=false` on the real Tapo
C120 Camera's heartbeat correctly produced `NotRunning` for that one
capability while `ObjectDetection` (left `true`) stayed `Running`, proving
the two signals are read and applied independently, not proven by re-reading
the fix's logic alone.

**A second thing found live, worth recording as a verification-methodology
note, not a product bug**: `az storage entity merge --entity
Key=true`/`Key=false` writes `Edm.String`, not `Edm.Boolean`, unless
`Key@odata.type=Edm.Boolean` is also passed - confirmed by inspecting the
resulting row's rendered type (quoted vs. unquoted) after each attempt.
Silently left the real Tapo C120 Camera's `SinkCleanlinessEnabled` as a
string for several requests during this pass's own testing before being
caught and corrected with the explicit type annotation. Every future
boolean-field edit against this table via `az storage entity merge` in
this codebase's test/verification workflow needs the explicit
`@odata.type=Edm.Boolean` annotation - noted here so the next pass doesn't
rediscover it the same way.

**Cross-tenant isolation, verified explicitly rather than assumed
inherited**: created a throwaway second Tenant/Site/API key
(`Pass5-IsolationTest-TenantB`) and a throwaway Agent heartbeat row under
it, then confirmed live: the standing tenant's key never lists the
throwaway tenant's Agent (or vice versa) on `GET /agents`; the throwaway
tenant's key gets zero results from `GET /devices`/`GET /events` (no
cross-tenant rows exist to leak, and none did); a direct ID-guessing
attempt - each tenant's key fetching the *other* tenant's real entity by
its exact known id via `GET /agents/{id}` and `GET /devices/{id}/capabilities`
- returned 404 in both directions, not the entity. All of this already
worked before this pass (every query service scopes through
`SiteScope`'s single shared `TenantId|SiteId` partition-key convention,
untouched by Phase 8), but hadn't been exercised as its own explicit test
this phase - this closes that gap with real, not inferred, evidence.
Cleanup: throwaway heartbeat row deleted, both throwaway API keys revoked;
the throwaway Tenant/Site themselves were left in place (no delete route
exists for either, same as every prior pass's convention for
non-deletable entities).

`dotnet build` clean across `Vivnest.Core`/`Vivnest.Cloud`/
`Vivnest.Cloud.Functions`; dashboard `tsc -b && vite build` + `oxlint`
both clean (only pre-existing, unrelated warnings). Browser-verified: the
Overview/Agent/Device rows and filter chips render the new vocabulary
correctly; the Capabilities tab shows the new operational-status badge
alongside the existing Enabled/Disabled one for a real device
(Tapo C120 Camera), correctly reading `Unknown` while its Agent is
genuinely offline. All real entities this pass touched
(1Fitz Capture Agent's heartbeat, 6C Test Device's Status,
Tapo C120 Camera's `SinkCleanlinessEnabled`) were restored to their
pre-pass values before finishing.

## ADR-079 — Phase 9 Pass 1: command persistence + Cloud dispatcher + RestartAgent wrapped

Phase 9 is the platform's first ability to actively *command* Agents,
not just observe them: `Admin → Command → Agent → Handler/Capability →
Event`. Research (one Explore pass plus direct reads of every file it
cited) found `RestartAgent` already fully wired end to end today
(queue, Agent-side worker, HTTP route, dashboard button) but completely
fire-and-forget — no persisted record of a command ever having been
issued, received, or completed. Pass 1 builds the persistence +
dispatch + completion-detection substrate every future command type
will reuse, and reroutes `RestartAgent` through it as the first real
consumer — deliberately moved ahead of Pass 2's originally-planned
position (a Plan-subagent critique of the draft design flagged this as
the right sequencing: Restart reuses 100% already-proven plumbing, so
it exercises only the genuinely new pieces against a known-good
baseline, rather than compounding new-command risk with new-plumbing
risk in the same pass).

**Command state lives in new `tblAgentCommands`; the queue stays
delivery-only** — mirrors `AgentInstallationEntity`/
`AgentInstallationManagementService` (ADR-071) exactly: `PartitionKey =
"{TenantId}|{SiteId}"`, `RowKey = CommandId` (generated Guid), status
stored as `.ToString()`, orchestration-in-service/CRUD-in-store split.
New `AgentCommandStatus`: `Pending, Dispatched, Received, Executing,
Succeeded, Failed, Expired, Cancelled` (`Cancelled` — enum value only,
not wired to any action this phase, matching the spec's own "where
practical" wording and this pass's actual needs). New `AgentCommand`
domain class with named transitions (`MarkDispatched()`,
`MarkReceived()`, `MarkSucceeded(result)`,
`MarkFailed(errorCode, errorMessage)`, `MarkExpired()`, etc.) plus a
validating constructor that stamps `CreatedUtc`/`ExpiresUtc` (default
5-minute expiry). `AgentCommandEntity` deliberately extends
`AgentEntity`, not `BaseEntity` — its inherited `AgentId` property *is*
the target agent, avoiding a redundant `TargetAgentId` field, the same
convention `AgentInstallationEntity` already established (caught and
self-corrected mid-implementation after first writing the redundant
field and then checking the actual inheritance chain).

**`ICommandDispatcher`/`CommandDispatcher`** (`Vivnest.Cloud/Admin`) is
the single write path: resolves the target Agent for the caller's
tenant (404 if not found, no row ever created), validates
Tenant/Site/Device/Capability ownership where the command type carries
one (`ValidateAsync` — this pass's own logic, though currently only
`RestartAgent`'s trivial "no extra target" path is exercised; the
`ExecuteCapability` branch is already written for Pass 3, see below),
checks the new **per-Agent concurrency policy** (`IsAgentBusyAsync` —
reject a new *disruptive* command, `RestartAgent`/`ApplyConfiguration`,
if the Agent already has one `Received`/`Executing`), then persists the
command **exactly once**, reflecting whichever state is
terminal-for-this-request (`Failed` on a validation rejection,
`Dispatched` on a successful enqueue) rather than a Create-then-Update
pair on the same in-memory object. This was a deliberate, specific
choice: `AzureTableStore<T>.UpdateAsync`'s ETag handling was the
subject of a real bug fixed in ADR-077, and `UpsertAsync` (used for
Create) never captures the response ETag either — a same-method
Create-then-Update would hit the identical class of bug again. One
`CreateAsync` call per `DispatchAsync` invocation sidesteps it
entirely.

**Delivery is split by *consumer*, not by command type** — re-read
ADR-024 directly before assuming anything: its actual rule is "one
queue per consumer," not "one queue per command," and Restart's queue
is already separate from Deploy's for exactly that reason (two
different processes consume them). `RestartAgent` stays on the
existing `agent-restart-commands` queue/`CommandPollingWorker`,
untouched in shape — deliberate risk-aversion, since a documented
production incident is already attached to that path in ADR-024 and
this pass adds several new moving parts elsewhere; touching the one
delivery-critical recovery path at the same time would conflate two
different kinds of risk. A new shared queue, `agent-commands`
(`AgentCommandQueueMessage(CommandId, AgentId, CommandType)`), is built
now for `RefreshConfiguration`/`ApplyConfiguration`/`ExecuteCapability`
to use in Pass 2/3 — all three will share one new
`AgentCommandPollingWorker`, so one queue for them is consistent with
ADR-024's rule, not a workaround of it. `RestartCommandQueueMessage`
gained an optional `CommandId` so the existing queue can now carry a
tracked command's id without a shape change for any in-flight message.

**`CommandPollingWorker` gained exactly one new thing**: a best-effort
`PUT .../commands/{commandId}/status {status:"Received"}` call right
before `_lifetime.StopApplication()`, guarded by try/catch and a short
implicit timeout — the process is about to exit regardless, so a failed
callback is logged and ignored, never blocking the restart itself.

**Completion for `RestartAgent` is confirmed via the next heartbeat,
never self-reported** — the process dies before it could report its
own success. A best-effort hook,
`IAgentCommandManagementService.EvaluateAgentCommandsAsync`, sits right
next to `AgentInstallationManagementService.NoteAgentHeartbeatAsync`'s
existing call inside `HealthMonitorService.EvaluateAgentAndNotifyAsync`
(same try/catch style ADR-072 already established there) — this method
already fires both per-tick *and* immediately per-heartbeat via
`AgentHeartbeatChangedFunction`'s queue trigger, so completion detection
is near-instant. Rule for `RestartAgent`: any `Dispatched`/`Received`
command for that Agent where the heartbeat's `StartedUtc` is newer than
the command's `DispatchedUtc` is marked `Succeeded` — proof a genuinely
new process started after the command was issued.

**A dedicated `CommandExpiryTimerFunction`/`ICommandExpiryService`**
(`Vivnest.Cloud.Functions/Timer`, `Vivnest.Cloud/Services`) mirrors the
existing `AgentEventRetentionTimerFunction` pattern exactly — a
standalone timer calling one focused service, not folded into
`HealthMonitorService`'s already tightly-scoped per-tick job (a direct
Plan-subagent correction of the original draft, which had proposed
piggybacking the sweep there). Full unpartitioned `GetAllAsync()` scan,
consistent with `HealthMonitorService.RunAsync`'s own existing
unpartitioned scans — this codebase's already-accepted scale
assumption, not a new one. Flips anything still
`Pending`/`Dispatched`/`Received`/`Executing` past its `ExpiresUtc` to
`Expired`.

**Idempotency is Cloud-authoritative**: the status-transition PUT
(`AgentCommandManagementService.UpdateStatusAsync`) only applies if the
command's current persisted status isn't already terminal
(`Succeeded`/`Failed`/`Expired`/`Cancelled`); a duplicate or
late-arriving call is a silent no-op that returns the already-persisted
result. This is the guard that actually matters — it survives an Agent
restart, unlike any in-process dedup, which wasn't built this pass since
the Cloud-side guard alone is sufficient.

**New HTTP surface** (`AgentCommandsFunction`): `GET
/agents/{agentId}/commands` is dashboard-facing, normal tenant
`x-api-key` auth. `GET /agents/{agentId}/commands/{commandId}` and `PUT
.../status` are Agent-facing instead — no tenant key exists on the
Agent, so `tenantId`/`siteId` travel explicitly (query params / JSON
body) and are trusted directly, the same precedent
`AgentInstallationManagementService.ReportDeployCompleteAsync` already
established for Agent/Updater-originated calls. `POST
/agents/{agentId}/restart` (`AgentsFunction`) now calls
`ICommandDispatcher.DispatchAsync` instead of publishing directly,
returning the created `AgentCommandDto` (202) in place of the old bare
accepted response. `RequestedBy` is hardcoded to the literal
`"Dashboard"` at this layer — no per-user identity exists in this
codebase (ADR-012: permissions are a plain bool until a second
dimension is real), so this isn't a fabricated user system, just an
honest placeholder.

**`ExecuteCapability`'s full authorization chain is already written in
`CommandDispatcher.ValidateAsync` this pass**, even though nothing
dispatches that command type yet — for `ImageCapture` (Built-in, no
`DeviceCapability` row): `TargetAgentId == Device.AgentId` or reject
`WRONG_AGENT`; for a real Derived capability: read
`IDeviceCapabilityStore.GetActiveByDeviceAndCapabilityAsync` directly
(the same lookup `CapabilityAssignmentService.AssignAsync` already
uses) — null → `CAPABILITY_NOT_ASSIGNED`, `ExecutingAgentId !=
TargetAgentId` → `WRONG_EXECUTING_AGENT`. A Plan-subagent critique
caught and corrected an earlier, wrong idea here — reusing
`IsValidExecutingAgentAsync` — which checks a different thing entirely
(Agent-declares-eligibility, not the live Device+Capability assignment).
Landing this now, unused, means Pass 3 only needs to wire an Agent-side
handler and a dispatch entry point, not re-derive the authorization
logic.

**Real Azure verification**: dispatched a real `RestartAgent` command
(`POST /agents/{agentId}/restart`) against an already-running local
`Vivnest.Agent` process (started first, allowed to publish several
heartbeats, establishing an old `StartedUtc` before dispatch — an
earlier attempt that started the Agent process *after* dispatching
produced an ambiguous `Succeeded`-with-`ReceivedUtc:null` result,
because a brand-new process's first-ever heartbeat already satisfies
the `StartedUtc > DispatchedUtc` completion rule almost immediately,
short-circuiting past the `Received` checkpoint before either the
poll tick or the callback could land — re-run with corrected ordering to
get an unambiguous result). With the corrected ordering: confirmed live
`Pending → Dispatched` (`POST` response, 202) → `Received` (`GET` after
~20s, `ReceivedUtc` populated, matching the `CommandPollingWorker` log
line) → the process genuinely exited (`StopApplication()` observed via
process exit code 0) → a **separately started** new `Vivnest.Agent`
process's first heartbeat flipped the command to `Succeeded`
(`CompletedUtc` populated, `StartedUtc` still `null` on the DTO — a
known, accepted gap: the heartbeat's own `StartedUtc` isn't copied back
onto the command row this pass, only used to *evaluate* the transition;
nothing currently reads it back off `AgentCommandDto`). Confirmed
directly against the underlying `tblAgentCommands` row via `az storage
entity show`, not just through the HTTP DTO. Also verified: the
`agent-restart-commands` queue message correctly carried the new
`CommandId` field; the queue was empty afterward (message deleted on
receipt, per `CommandPollingWorker`'s existing non-retrying design).

**Expiry sweep verification surfaced two real bugs, neither in the
sweep's own core logic**: hand-crafting an already-expired
`tblAgentCommands` row directly via `az storage entity insert` and
waiting through several real 1-minute cron ticks
(`CommandExpiryCronSchedule`) - confirmed actually firing each time via
the Timer extension's own status blob in `azure-webjobs-hosts`, not
assumed - the row stayed `Dispatched`, never `Expired`. First:
`builder.Services.Configure<CommandExpiryOptions>(...)` was simply
missing from `Vivnest.Cloud.Functions/Program.cs` - every sibling
options type (`HealthMonitorOptions`, `DeviceEventRetentionOptions`,
etc.) has one, this one didn't, an oversight not caught by the compiler
since `IOptions<T>` still resolves with defaults when nothing configures
it. Harmless in this instance only because `CommandExpiryOptions.Enabled`
defaults `true`, so a config-driven disable in
`local.settings.json`/`CommandExpiry__Enabled` would have silently done
nothing - fixed by adding the missing `Configure<CommandExpiryOptions>`
call, confirmed via a clean rebuild and host restart. Second, and the
one actually blocking the sweep: `az storage entity insert` without an
explicit `@odata.type=Edm.DateTime` annotation writes an ISO-looking
date string as `Edm.String`, not `Edm.DateTime` - the same class of gotcha
ADR-078 already found for booleans (`Key@odata.type=Edm.Boolean`), now
hitting `AgentCommandEntity`'s `DateTime`-typed fields instead. Worse
than the boolean case: `CommandExpiryService.RunAsync` makes exactly one
`QueryAsync<T>` call across the *entire* table with no per-row try/catch
(deliberately, mirroring `HealthMonitorService.RunAsync`'s own
unpartitioned-scan shape) - the strongly-typed SDK's deserialization
failure on the one malformed test row silently aborted the whole sweep
for every command in the table, every single tick, with nothing logged
to say why. Confirmed by re-writing the same row's three DateTime fields
with explicit `@odata.type=Edm.DateTime` annotations - the very next
cron tick correctly flipped it to `Expired`. Not a product bug: every
real command row is written exclusively through `CommandDispatcher`/
`AgentCommandManagementService`'s typed `ToEntity()` mapping, which the
Azure Table SDK always serializes as proper `Edm.DateTime` - this failure
mode is only reachable by writing malformed data directly via `az`
tooling, exactly what this pass's own verification did. Recorded here as
a verification-methodology note (same spirit as ADR-078's), not a
code fix: worth remembering that this sweep has no per-row isolation, so
a future pass touching real production data by hand should mind the
same gotcha.

`dotnet build` clean across `Vivnest.Core`/`Vivnest.Cloud`/
`Vivnest.Cloud.Functions`/`Vivnest.Agent` (two file-lock build failures
along the way — stale `dotnet.exe` processes from earlier in this
session still holding assembly handles open — resolved by killing the
locking PIDs, unrelated to the code itself). Cleanup: the throwaway API
key was revoked, the hand-crafted expired test command row was deleted,
and the test `Vivnest.Agent` process was stopped; the real
`RestartAgent` command row (`de61d267-...`, genuinely dispatched against
the real standing tenant's Agent and genuinely succeeded) was left in
place as legitimate command history, matching this codebase's existing
convention for real lifecycle rows (e.g. `AgentInstallation`'s `Pending`
rows).

## ADR-080 — Phase 9 Pass 2: RefreshConfiguration + ApplyConfiguration

The first genuinely new Agent-side command handlers built on Pass 1's
substrate (`ICommandDispatcher`, `tblAgentCommands`, the heartbeat-
correlation completion hook), and the first Cloud-to-Agent commands to
flow over the shared `agent-commands` queue built in Pass 1 but unused
until now. Deliberately not live config hot-reload — the Agent has no
in-place reconfiguration mechanism today, config loads once at process
startup — both commands resolve to "download+validate the target
version, then either report Succeeded immediately (unchanged) or
restart to adopt it (changed)," reusing the exact self-restart mechanism
`CommandPollingWorker` already uses for `RestartAgent`.

**Scoped to the Agent's own configuration only this pass** —
`ApplyConfiguration`'s payload nominally allows a `TargetDeviceId`
(per the original spec/plan), but Pass 2 doesn't accept one from the
caller and `CommandDispatcher` never resolves a device-level
`TargetVersion`. This was a deliberate scope call, not an oversight:
research surfaced that device-level support *is* mechanically feasible
(`IDeviceRuntimeStore.GetDevices(id)` already exposes a device's
currently-loaded `ConfigurationVersion`/`ConfigurationHash`), but the
Cloud-side completion hook would need meaningfully more branching —
`AgentCommandManagementService.EvaluateAgentCommandsAsync` takes an
`AgentHeartbeatEntity`, and confirming a device-targeted apply would
mean also reading the most recent `DeviceHeartbeatEntity` for the
target device, a new dependency this service doesn't have today. Given
the plan's own "confirm during implementation whether Refresh covers
Agent-level only or Agent+Devices" note already flagged this as
unresolved, this pass resolves it toward the narrower scope — same
reasoning Pass 1 applied to scoping the heartbeat-completion hook to
`RestartAgent` only. The generic `TargetDeviceId` ownership-check branch
`CommandDispatcher.ValidateAsync` already had (unused by `RestartAgent`)
is left in place, unused by these two command types either, as
groundwork for whenever device-level support is added.

**Both commands normalize to one Cloud-computed payload shape**,
`AgentConfigCommandPayload{TargetVersion}` (new, `Vivnest.Core/Constants`
— a genuine Agent/Cloud shared wire type, not two independently-declared
copies, since it crosses the process boundary as a value object the way
`ConfigurationManifest` already does): `RefreshConfiguration` resolves
`TargetVersion` from whatever's currently published
(`AgentConfigurationEntity.CurrentVersion`) at dispatch time; for
`ApplyConfiguration`, the caller supplies an explicit
`ConfigurationVersion` (new `ApplyConfigurationRequest` DTO,
Cloud-only), which `CommandDispatcher.ValidateAsync` checks against the
same `CurrentVersion` before ever persisting a row — reusing
`RollbackAsync`'s own established precedent for "does version N exist"
(a version blob's existence is inferred from `1..CurrentVersion`, not a
separate Blob round-trip, since versions are only ever created
monotonically and never deleted). Either way, `CommandDispatcher`
overwrites whatever payload the caller sent with this normalized shape
before persisting — the Agent-side handlers and the completion hook
both read the one shape regardless of which command produced it.

**Agent-side, a single shared base class does the actual work** —
`ConfigVersionCommandHandlerBase` (`Vivnest.Agent/Runtime/Commands`),
with `RefreshConfigurationCommandHandler`/`ApplyConfigurationCommandHandler`
as thin `CommandType`-only subclasses. Not two duplicated
implementations: once Cloud has normalized both commands down to the
same `{TargetVersion}` payload, the two command types are genuinely
identical on the Agent side — compare `TargetVersion` against
`AgentConfigMetadataOptions.ConfigurationVersion` (already
IConfiguration-bound at startup from whatever agent-config blob loaded,
Pass 1's own research confirmed this is a pure pass-through, no local
hashing/versioning logic exists client-side); equal → report `Succeeded`
directly via the status-callback PUT, no restart; different → confirm
the target version's blob is real (`AzureBlobStorageClient.DownloadAsync`
on `AgentConfigBlob.VersionBlobName`, 404 → `Failed(VERSION_NOT_FOUND)`)
→ report `Executing` → `_lifetime.StopApplication()`.

**New `ICommandHandler`/`AgentCommandPollingWorker`** — a deliberate
sibling to `IEventHandler<T>`/`EventDispatcher`, matching the shape
Pass 1's plan called for: string-keyed by `CommandType` rather than
CLR-generic-keyed (a queue envelope carries a string), and unlike
`EventDispatcher`'s intentional many-handlers-per-event-type design,
exactly one `ICommandHandler` is expected per `CommandType` -
`AgentCommandPollingWorker` builds a `Dictionary<string, ICommandHandler>`
at startup and looks up a single match. `AgentCommandPollingWorker`
itself mirrors `CommandPollingWorker`'s shape closely (same 15s poll
interval, same delete-before-process non-retrying design, same
ownership-check-and-discard for a queue every agent shares) but adds one
new step `CommandPollingWorker` never needed: after the ownership check,
it fetches the command's full detail via
`GET /agents/{agentId}/commands/{commandId}` (built in Pass 1, unused
until now) before dispatching, since the queue envelope only carries
`CommandId`/`AgentId`/`CommandType`, not the payload a handler needs.
Reuses `CommandPollingWorker.cs`'s own internal `CommandStatusUpdateBody`
directly (same assembly, same namespace) rather than declaring a second
identical record — the Agent/Cloud cross-process duplication convention
doesn't apply within one assembly.

**A real bug, found live**: the Cloud-side completion hook's status
filter (`AgentCommandManagementService.EvaluateAgentCommandsAsync`),
copied verbatim from Pass 1, only matched commands in `Dispatched`/
`Received`. `RestartAgent` never reports `Executing` (`CommandPollingWorker`
goes straight from `Received` to the process dying), so that was
sufficient for Pass 1 - but `RefreshConfiguration`/`ApplyConfiguration`'s
handler explicitly reports `Executing` before restarting, so by the
time the post-restart heartbeat arrived, the command was already
sitting in `Executing`, not `Dispatched`/`Received` - silently never
matched. Confirmed live: a real `ApplyConfiguration` command stayed
stuck at `Executing` forever despite the Agent correctly reporting a
matching `ConfigurationVersion` on every subsequent heartbeat. Fixed by
widening the filter to include `Executing`; re-verified live afterward
that the same stuck command transitioned to `Succeeded` on the very
next heartbeat once the fix was deployed, with no other change needed.

**Real Azure verification**: the real standing tenant's real Agent
(1Fitz Capture Agent) had never been through the versioned
`agent-config` publish flow — confirmed live (`AgentConfigurationEntity`
404, no `ConfigurationVersion`/`ConfigurationHash` keys on its flat
blob) rather than assumed, and a real attempt to publish one via the
existing `POST agents-registry-admin/{agentId}/publish-config` endpoint
genuinely failed on pre-existing, unrelated data-quality warnings on
this tenant's real device configuration (out of scope to fix here, and
risky to touch given how much of this session's prior verification
depends on this same tenant's data staying intact). Verified instead
with hand-crafted but realistically-shaped test data: a real
`ApplyConfiguration(ConfigurationVersion: 1)` against this state was
first confirmed correctly rejected (`VERSION_NOT_FOUND`, never
dispatched) - genuine evidence of the version-exists check, no synthetic
data involved. Then a hand-crafted `AgentConfigurationEntity`
(`CurrentVersion: 1`) plus matching `versions/1.json`/`current.json`
blobs (shaped to match `AgentConfigWireDocument`'s real fields,
including `ConfigurationVersion`/`ConfigurationHash` at the top level -
a first attempt that omitted them was caught immediately: the restarted
Agent's heartbeat reported `ConfigurationVersion: null`, never matching,
which is exactly how a real gap in the *existing*, pre-Pass-2 manifest-
publish flow would manifest too, worth knowing even though fixing that
production gap is out of scope here) let the full cycle run for real:
`ApplyConfiguration` against the already-running Agent correctly
detected the version mismatch, restarted it, and (after the status-filter
fix above) reached `Succeeded` once the post-restart heartbeat reported
`ConfigurationVersion: 1`; a second `RefreshConfiguration` dispatched
while already at that version reported `Succeeded` immediately with no
restart (`Result: "Already at version 1."`), confirming the no-op path
independently. `agent-commands` queue confirmed empty afterward. Cleanup:
the hand-crafted `AgentConfigurationEntity` row and both test blobs were
deleted (restoring the Agent to its genuine pre-test "never published"
state), the throwaway API key was revoked, and the test `Vivnest.Agent`
process was stopped. The real command rows (one correctly-rejected
`ApplyConfiguration`, one successful `ApplyConfiguration`, one
successful `RefreshConfiguration`) were left in place as legitimate
history, same convention as Pass 1.

`dotnet build` clean across `Vivnest.Core`/`Vivnest.Cloud`/
`Vivnest.Cloud.Functions`/`Vivnest.Agent`.

## ADR-081 — Phase 9 Pass 3: ExecuteCapability(ImageCapture) + authorization chain

Closes out the four Phase 9 command types: `ExecuteCapability`, scoped to
`ImageCapture` only, matching every one of the spec's own worked
examples. Unlike Pass 2's two commands, most of the validation logic
here was already written in Pass 1 (`CommandDispatcher.ValidateAsync`'s
`ExecuteCapability` branch) - this pass wires an Agent-side execution
handler to it for the first time, which is exactly what exposed a real,
previously-undetected bug in that Pass 1 code (below).

**Agent-side, reuses the motion-triggered-capture path verbatim** — the
new `ExecuteCapabilityCommandHandler` publishes
`DeviceTriggeredEvent(deviceId, DeviceType.Camera, "Command",
DateTime.UtcNow)` via the Agent's existing `IEventDispatcher`; the
already-registered, unchanged `CaptureOnTriggerHandler` (built for
motion-triggered bursts) does the real work - wakes
`CameraCaptureWorker`'s loop, calls `ICameraCaptureExecutor.CaptureAsync`
for an immediate capture, which already flows into
`CameraCaptureCompletedEvent` → `CameraCaptureHandler` → a persisted
`DeviceEvent: CameraCaptured`. The command handler reports `Succeeded`
right after publishing, optimistically - actual capture completion is
async and confirmed separately by that `DeviceEvent`, not by the
command's own status (matches the plan's own framing: "report Succeeded
with a result referencing the capture"). Any `CapabilityId` other than
`ImageCapture` reaching the handler - correctly authorized by Cloud (a
real `DeviceCapability` assignment exists, `ExecutingAgentId` matches),
but with nothing built to execute it yet - reports
`Failed(CAPABILITY_UNAVAILABLE)`. A defense-in-depth local check
(`IDeviceRuntimeStore.GetDevice`) confirms the target device is
genuinely one this process has loaded before publishing an event
nothing would handle, reporting `Failed(DEVICE_NOT_FOUND)` if not -
not the primary authorization (Cloud's dispatch-time check is), just a
second, cheap confirmation.

**A real bug, found live, in code written back in Pass 1**: the
Derived-capability branch of `CommandDispatcher.ValidateAsync`
(`assignment.ExecutingAgentId != targetAgentId`) compares two values
from *different identity spaces* and would never have matched for any
real, correctly-assigned capability. Confirmed by reading, not guessed:
`DeviceCapability.ExecutingAgentId` is validated at assignment time
(`CapabilityAssignmentService`) against `IAgentRegistryStore` - the
*admin* AgentId space (`tblAgentRegistry`'s own RowKey), the same
boundary `DeviceService.IsValidOwningAgentAsync` uses for
`Device.OwningAgentId`. But `targetAgentId` throughout
`CommandDispatcher` (and every `/agents/{agentId}/...` route) is always
a *RuntimeAgentId* - confirmed by tracing `DeviceSummaryDto.AgentId`
back to `DeviceHeartbeatEntity.AgentId`, which is what the sibling
`ImageCapture` branch (`device.AgentId != targetAgentId`) correctly
compares against, since that one *is* in the RuntimeAgentId space. Real
data on the standing tenant made this concrete: `DeviceCapability` rows
carry admin AgentIds like `a38c425f-7ef3-4b78-818a-66fe52dfc962`,
while every dispatch call passes a RuntimeAgentId like
`5d6c8d6f-4b8d-47e0-a56f-3c3e8cdb2d63` for the *same* Agent - directly
comparing them, as the original code did, could never succeed for any
genuinely correct assignment. Fixed by reverse-resolving `targetAgentId`
to its admin AgentId first, via `IAgentRegistryStore.GetByRuntimeAgentIdAsync`
- the exact same identity-space-crossing lookup
`AgentQueryService`/`AgentInstallationManagementService` already use
for this precise reason - before comparing against
`assignment.ExecutingAgentId`. `CommandDispatcher` gained a new
`IAgentRegistryStore` dependency for this.

**Completion hook extended with the opposite rule from Pass 2's**: for
`ExecuteCapability`, a heartbeat with `StartedUtc` newer than
`DispatchedUtc` while the command is still `Received`/`Executing` means
an unexpected crash - this command type never causes a restart on its
own success path (it self-reports `Succeeded`/`Failed` directly and
stays alive) - so it's marked `Failed(AGENT_RESTARTED)` immediately
rather than `Succeeded`, implementing the spec's own "Agent restart
during command execution does not falsely mark the command successful"
acceptance test directly.

**New `POST /agents/{agentId}/execute-capability`** (body
`{TargetDeviceId, CapabilityId}`) - deliberately Agent-centric like
every other command route (the URL names the target Agent), not
device-centric, so the caller states both which Agent should execute
and which Device/Capability, exercising `ValidateAsync`'s full
authorization chain (including deliberately-wrong-agent tests) the same
way the other two commands' routes already do.

**Real Azure verification, the fullest of the three passes**: a real
`ExecuteCapability(ImageCapture)` dispatched against the real running
Agent and its real Tapo C120 Camera produced a genuine new capture -
confirmed via both the command reaching `Succeeded` *and*, independently,
a real new `DeviceEvent: CameraCaptured` row with `TriggerReason:
"Command"` and a real blob at the expected path. `WRONG_AGENT` verified
against a real second Agent in the tenant, targeting a device it
doesn't own. The identity-space bug fix was verified in both directions
using a hand-crafted `DeviceCapability` row against the real camera
device (the tenant's actual pre-existing `DeviceCapability` rows all
reference devices with no live heartbeat - a pre-existing data gap
unrelated to this pass, confirmed by cross-referencing device ids
against the live `/devices` list before reaching for synthetic data):
first with a deliberately mismatched `ExecutingAgentId` (rejected
`WRONG_EXECUTING_AGENT`, proving the fix's negative branch), then
updated to the real Agent's actual admin AgentId (passed validation,
correctly reaching the Agent and reporting `Failed(CAPABILITY_UNAVAILABLE)`
- proving the fix's positive branch *and* the "authorized but nothing
built to execute it" path in one real round trip). `AGENT_RESTARTED`
verified by hand-crafting a `Received`-status command with a
`DispatchedUtc` before the real running Agent's actual `StartedUtc` -
the very next real heartbeat correctly flipped it to
`Failed(AGENT_RESTARTED)`. Cleanup: the hand-crafted `DeviceCapability`
row and the `AGENT_RESTARTED` test command row were deleted, the
throwaway API key was revoked, the test `Vivnest.Agent` process was
stopped, and `agent-commands` was confirmed empty. The four real command
rows this pass genuinely produced (one successful `ImageCapture`, one
`WRONG_AGENT` rejection, one `WRONG_EXECUTING_AGENT` rejection against
real data, one `CAPABILITY_UNAVAILABLE`) were left in place as
legitimate history.

`dotnet build` clean across `Vivnest.Core`/`Vivnest.Cloud`/
`Vivnest.Cloud.Functions`/`Vivnest.Agent`.

## ADR-082 — Phase 9 Pass 4: reliability verification pass

No new command types this pass — dedicated to exercising the spec's own
reliability/concurrency acceptance criteria against real Azure data and
fixing whatever the previous three passes' completion hook, idempotency
guard, or expiry sweep get wrong under real conditions, per the plan's
own Pass 4 framing. Several scenarios were already proven as a
byproduct of Passes 1-3's own real verification (wrong-Agent/Tenant/Site
rejection at dispatch, capability-not-assigned and wrong-executing-agent
rejection, `AGENT_RESTARTED`, a real restart-causing `ApplyConfiguration`
correctly waiting for the post-restart heartbeat's `ConfigurationVersion`)
and weren't re-tested from scratch here - this pass focused on the
scenarios genuinely untested until now: an offline Agent, expired
commands still sitting in a queue, and duplicate status updates.

**A real bug, found during review, not live-triggered**: neither
`AgentCommandPollingWorker` (Refresh/Apply/ExecuteCapability, built
Pass 2) nor `CommandPollingWorker` (RestartAgent, Pass 1's own queue)
ever checked whether Cloud still considered a fetched command live
before acting on it. Both fetch-then-execute unconditionally - a stale
message sitting in a queue while the Agent was offline (already
`Expired`, or resolved some other way) would be executed for real the
moment the Agent finally came back online and drained its backlog: a
genuine, unwanted restart, or a genuine, unwanted capture, for a command
Cloud already considers closed. This is distinct from - and not
protected by - the existing Cloud-side idempotency guard
(`AgentCommandManagementService.UpdateStatusAsync`'s terminal-status
check): that guard only protects the *recorded* status from a stale
update after the fact, it was never a defense against the Agent
re-running the underlying side effect in the first place. Directly
implements the spec's own "Expired command is never picked up even if
somehow still in the queue" acceptance criterion, which reads as a
general principle across all four command types, not one scoped to only
the newer three - so both workers were fixed, not just
`AgentCommandPollingWorker`.

**Fix, `AgentCommandPollingWorker`**: `AgentCommandDetails` (the Agent's
local mirror of `AgentCommandDto`) gained `Status`/`ExpiresUtc` fields -
present purely for this worker's own pre-execution check, not for any
`ICommandHandler` to read. `ProcessCommandAsync` now checks both
immediately after fetching the command and *before* even reporting
`Received`: already-terminal (`Succeeded`/`Failed`/`Expired`/`Cancelled`)
or past `ExpiresUtc` → log and discard, no handler ever invoked, no
status callback ever sent - a discarded command leaves no trace of
having been picked up at all.

**Fix, `CommandPollingWorker`**: gained a new best-effort
`TryIsAlreadyResolvedAsync` check (one extra `GET` to the same
`/agents/{agentId}/commands/{commandId}` endpoint the other worker
already used, reading a new minimal local `CommandStatusCheck{Status,
ExpiresUtc}` record) run right before the restart decision - deliberately
fails open (proceeds with the restart) on any error, matching this
worker's existing best-effort philosophy elsewhere: a missed check
occasionally allowing one stale restart through is a much smaller
problem than a transient network blip permanently blocking a genuine
restart request.

**Real Azure verification**: dispatched both a real `RestartAgent` and
a real `ExecuteCapability(ImageCapture)` against the real Agent while it
was genuinely offline (confirmed not running via process list, not
assumed) - both correctly stayed `Dispatched` through the real 5-minute
window with no further progress, then both flipped to `Expired` via the
real cron-driven sweep, confirmed via polling, not forced. Both queue
messages confirmed still sitting undelivered (`dequeueCount: 0`)
afterward. The Agent was then started for real - its log showed both
workers correctly discarding their respective stale message
(`"...is already Expired; discarding without executing."` /
`"...is already resolved or expired; discarding without restarting."`),
the process was confirmed still running afterward (proving the
restart was genuinely skipped, not just logged), both command rows
stayed `Expired` untouched, and both queues confirmed empty (the
messages were still deleted on dequeue, per each worker's existing
delete-first design - just never acted on). A fresh, valid
`ExecuteCapability` dispatched against the now-online Agent immediately
afterward reached `Succeeded` normally, confirming the new pre-checks
don't block genuinely live commands - a deliberate regression check
against the fix itself. Idempotency verified directly: a duplicate `PUT
.../status` attempting to flip an already-`Succeeded` real command
(from Pass 3) to `Failed` returned the unchanged original `Succeeded`
state, not the attempted one.

Cleanup: the throwaway API key was revoked and the test `Vivnest.Agent`
process was stopped; no synthetic table/blob artifacts were created
this pass (every test used real dispatch calls against the real
standing tenant), so there was nothing else to restore. The three real
command rows this pass produced (two genuine expiries, one genuine
success) were left in place as legitimate history.

`dotnet build` clean across `Vivnest.Core`/`Vivnest.Cloud`/
`Vivnest.Cloud.Functions`/`Vivnest.Agent`.

## ADR-083 — Phase 9 Pass 5: Admin UI

The final pass of Phase 9 — surfaces Passes 1-4's command model in the
dashboard, no backend changes. `api.ts` gained `AgentCommand`/
`CommandStatus` types and four functions: `getAgentCommands(agentId)`,
`refreshAgentConfiguration(agentId)`, `applyAgentConfiguration(agentId,
version)`, `executeDeviceCapability(agentId, deviceId, capabilityId)`.
The three POST functions deliberately mirror `restartAgent`/
`deployAgent`'s existing shape exactly (raw `fetch`, not the generic
`request<T>()` helper) since the 202 response body is discarded, same
as those two originals. `applyAgentConfiguration` deliberately has no
`targetDeviceId` parameter — Pass 2 (ADR-080) never built device-level
`ApplyConfiguration` support, so the UI must not offer a capability the
backend can't fulfill.

**`CommandHistory.tsx`** (new) mirrors `DeviceEventList.tsx`'s
fetch-on-mount, three-state loading/error/empty template. Takes an
optional `deviceId` prop: absent (mounted on `AgentDetail`) shows every
command for the Agent; present (mounted on `DeviceDetail`) client-side
filters the same tenant-scoped `getAgentCommands` list to
`targetDeviceId === deviceId` — a dedicated per-device endpoint wasn't
built, matching the plan's own explicitly-permitted alternative for
what's expected to be low per-device command volume. Status badges
reuse the existing shared `.status-*` classes (`Succeeded`→`-online`,
`Failed`→`-error`, `Dispatched`/`Received`/`Executing`/`Pending`→
`-warning`, `Expired`/`Cancelled`→`-unknown`) — the same "reuse the
shared palette" convention ADR-078 established, no new status CSS.

**`AgentDetail.tsx`**: "Refresh configuration"/"Apply configuration"
buttons added to the existing `.detail-header-actions` row, alongside
Restart/Deploy. Refresh follows the same state-triplet +
`ConfirmDialog` pattern the existing Restart button already uses. Apply
needs a version number as required user input, which `ConfirmDialog`
has no support for (its own code comment explains it deliberately
replaced `window.confirm()` specifically because native dialogs can't
be styled — `window.prompt()` would face the same objection) — rather
than extending `ConfirmDialog` into a general input-capable modal for
one numeric field, Apply uses a lightweight inline reveal row (a
toggle-visible number `<input>` + Apply/Cancel buttons, new
`.apply-config-row`/`.apply-config-input` CSS) directly under the
header. `CommandHistory` mounted at the bottom of the component, after
"Devices on this agent".

**`DeviceDetail.tsx`**: new "Capture now" button, gated to
`device.deviceType === "Camera" && !devicesOnly` — the first
`.detail-header-actions` row this component has had (unlike
`AgentDetail`, it didn't have one before this pass). Calls
`executeDeviceCapability(apiKey, device.agentId, deviceId,
"ImageCapture")` behind the same `ConfirmDialog` pattern. `CommandHistory`
mounted for all device types (not just Camera — a non-Camera device
could still show `ExecuteCapability` history from a future capability),
gated by `!devicesOnly`, passing `deviceId` for the client-side filter.

Neither new button needed its own `devicesOnly` gate check beyond what
was added: `AgentDetail` itself is never reachable by a `devicesOnly`
API key (`App.tsx` forces `activeView` to `"devices"` whenever
`devicesOnly` is true, so the Agent-view branch that renders
`AgentDetail` is never taken) — consistent with `AgentDetail`'s
pre-existing Restart/Deploy buttons, which also carry no `devicesOnly`
check of their own for the same reason.

**Verification**: `tsc -b`, `vite build`, and `oxlint` all clean (no
new warnings beyond this file's pre-existing, unrelated
`only-export-components` warnings in three other files). Live browser
verification against the real Sana/1Fitz tenant was not performed this
pass — it requires a valid dashboard API key, and none was available in
this session; asked the user, who chose to skip it rather than provide
one. This is a real gap relative to every prior pass's discipline: the
new buttons and `CommandHistory` panel are confirmed to compile and
render-path-check via TypeScript/build tooling only, not confirmed
working end-to-end against a live Agent. Flagged here rather than
silently passed over.

## ADR-084 — Removed the ADR-038/064 credential-stripping publish guard

**Reverses part of ADR-064** (and, by extension, narrows what ADR-038's
local-only `.secrets.json` boundary actually enforces). `CredentialSettingsFilter`
(`Vivnest.Cloud/Admin/CredentialSettingsFilter.cs`, deleted this ADR) used
to strip any Settings key matching `password`/`accesstoken`/`secret`
(case-insensitive substring) out of both publishers'
(`DeviceRuntimeConfigurationPublisher`, `AgentRuntimeConfigurationPublisher`)
output before writing a version blob, replacing it with a warning pointing
at the local secrets-file mechanism instead. That call is now gone from
both publishers — `document.Settings`/`document.Capabilities`/each
device's `ObjectDetection`/`SinkCleanliness` settings are published
verbatim, credential-shaped keys included.

**Why:** explicit, repeated user instruction, made three times with
increasing directness, ending in a direct statement rather than a
question: *"there is no need to a separate .secret file, all sensitive
fields will be published in the config to blob."* Raised the specific
risk twice before making this change — that every publish writes a new
**immutable** `versions/{n}.json` blob (by design, for Rollback), so a
credential published once is retrievable from that old version forever,
with no way to retroactively purge it even after a later publish stops
including it — and the user chose to proceed anyway, for their own
Azure account and their own test tenant, with no third party affected.
This is the user's call to make about their own resource; recorded here
so a future reader doesn't mistake the gap for an oversight.

**Consequence, stated plainly:** `RtspPassword` and any other
credential-shaped Settings key is now permanently retained in every
future published config-blob version, in plain text, for as long as
those blobs exist. The local `.secrets.json` mechanism
(`TryLoadLocalAgentSecrets`/`TryMergeLocalDeviceSecrets` in
`Vivnest.Agent/Program.cs`) still exists and still works exactly as
before — it was never load-bearing for this change, it's just no longer
the only path for a credential to reach a running Agent. If this ever
needs to be un-done, the ADR-064 write-up above (and this file's own git
history) has the original `CredentialSettingsFilter` implementation.

## ADR-085 — Encrypt-in-place: a shared symmetric key replaces ADR-084's plaintext publish

Immediately superseded ADR-084's "publish credentials as plain text"
outcome, at the user's follow-up request: *"can we add a symetric key to
encrypt and decrypt the sensitive fields within the config?"* Same
publish-time classification ADR-064's `CredentialSettingsFilter` used
(key name contains `password`/`accesstoken`/`secret`, case-insensitive),
but the action taken is now encrypt-in-place rather than strip or
pass-through: a matched Settings value is replaced with AES-256-GCM
ciphertext under a shared symmetric key, wrapped as `"enc:v1:" +
base64(nonce(12) + tag(16) + ciphertext)`. This still answers the
ADR-084 risk directly — an old immutable version blob is only as
readable as whoever holds the key, not whoever holds blob-storage
read access — without reintroducing the local-`.secrets.json`-only
constraint ADR-038 originally imposed and the user explicitly didn't
want.

**New shared primitive, `Vivnest.Core/Security/CredentialCipher.cs`** -
referenced by both Cloud (`Vivnest.Cloud`) and Agent
(`Vivnest.Agent`), since both need the identical AES-GCM
encrypt/decrypt logic and the same `IsCredentialField` classification
Cloud uses at publish time. `EncryptFields(settings, key)` is the
Cloud-side entry point (replaces the old `CredentialSettingsFilter.Strip`
call sites in both publishers 1:1). `DecryptInPlace(node, key)` is the
Agent-side entry point - deliberately does NOT need to know which keys
are Settings dictionaries: it walks the entire downloaded JSON tree and
decrypts any string value carrying the `"enc:v1:"` prefix, since only
`Encrypt` itself ever produces that prefix. This is what lets one
decrypt call, applied to the whole blob, correctly reach `RtspPassword`
under `Device.Connection.Settings`, a future credential under a
capability's own `Settings`, and any credential a Derived capability's
`ObjectDetection`/`SinkCleanliness` settings might one day carry, with
zero adapter-specific decrypt code anywhere.

**Key provisioning: independent, out-of-band, never inside the blob it
protects.** New `CredentialEncryptionOptions.Key`
(`Vivnest.Core/Options`), a base64 AES-256 (32-byte) key, bound
separately on each side:
- **Cloud**: `CredentialEncryption:Key` (`CredentialEncryption__Key` in
  `local.settings.json`, real Azure Function App this would be a real
  App Setting) - `Vivnest.Cloud.Functions/Program.cs` registers
  `Configure<CredentialEncryptionOptions>` alongside every other Options
  class. Both publishers gained a constructor
  `IOptions<CredentialEncryptionOptions>` parameter and a
  `TryGetEncryptionKey` helper (mirrored between the two, same pattern
  as `MaxPublishAttempts`) - a missing or malformed key blocks publish
  outright with a `"Cannot publish: ..."` result, the same gate shape
  `Warnings.Count > 0` already uses. A security control that silently
  degrades to plaintext on misconfiguration isn't one.
- **Agent**: `CredentialEncryption:Key` in the local-only
  `common-config.secrets.json` (ADR-038's existing shared-secrets file -
  one more sensitive leaf alongside `Messaging.ConnectionString`, same
  file, same never-committed guarantee).

**Agent-side load-order wrinkle, and how it's resolved.** The
Agent-level config blob (`agent-config/{agentId}.json`) is decrypted at
download time, before it's inserted as a config source - but that
requires the key to already be parsed *before* that download happens.
The key only lives in `common-config.secrets.json`, whose loader
(`TryLoadLocalSharedSecrets`) previously ran *after* the agent-config
fetch in `Program.cs`'s top-level sequence. Fixed by moving
`TryLoadLocalSharedSecrets` to run first, followed immediately by a new
`TryParseCredentialEncryptionKey` read - safe to reorder because
`common-config.secrets.json` and the agent-config blob have zero key
overlap (confirmed: the former only ever carries
`Messaging.ConnectionString` and now `CredentialEncryption.Key`, neither
of which the agent-config blob defines). `TryLoadLocalAgentSecrets`
(the per-agent secrets file, unrelated to this key) stays in its
original position. Every other config-fetch function
(`TryLoadRemoteConfigAsync`, `TryLoadLocalConfig`,
`TryLoadRemoteDeviceConfigsAsync` → `TryProcessDeviceBlob`) now takes
the parsed `byte[]? credentialEncryptionKey` as a parameter and calls
`CredentialCipher.DecryptInPlace` (via a small `DecryptConfigBytes`
wrapper for the two blob-shaped callers) before the bytes reach
`IConfiguration` or `DeviceConfigRuntimeAdapter.Adapt`. Device-blob
decryption happens on `deviceObjectRaw`, before `Adapt()` runs and
before `TryWriteDeviceConfigCache` - so `ImageCaptureRuntimeAdapter`'s
verbatim `Connection` → `Settings` copy sees plaintext, and the
last-known-good local cache stores plaintext too (consistent with every
other local-only file in this pipeline already being unencrypted at
rest - the local disk was never the thing ADR-084's risk was about).

**Missing/invalid key is additive, not fatal, exactly like every other
local-file-optional convention in `Program.cs`.** No key configured on
the Agent → downloaded config keeps its `"enc:v1:..."` values verbatim
(unusable, but the Agent still starts); shows up immediately as a
broken `RtspPassword` rather than a silent wrong-value bug, since
`enc:v1:...` obviously isn't a valid RTSP password.

**Files**: `Vivnest.Core/Options/CredentialEncryptionOptions.cs` (new),
`Vivnest.Core/Security/CredentialCipher.cs` (new),
`Vivnest.Cloud/Admin/DeviceRuntimeConfigurationPublisher.cs`,
`Vivnest.Cloud/Admin/AgentRuntimeConfigurationPublisher.cs`,
`Vivnest.Cloud.Functions/Program.cs`, `Vivnest.Cloud.Functions/local.settings.json`,
`Vivnest.Agent/Program.cs`, `Vivnest.Agent/common-config.secrets.json`.
`CredentialSettingsFilter.cs` (deleted under ADR-084) stays deleted -
nothing in this ADR resurrects strip-and-warn.

**Verification**: backend builds clean (`dotnet build` across the whole
solution). Real-Azure verification done interactively with the user:
republished Kitchen Camera, downloaded the resulting version blob
directly from Storage and confirmed `RtspPassword` shows
`"enc:v1:55As4r4/..."` ciphertext (`Host`/`RtspUsername` untouched, not
credential-shaped). A standalone throwaway console app referencing
`Vivnest.Core` confirmed `CredentialCipher.TryDecrypt` on that exact
ciphertext, under the real configured key, round-trips back to the
original password. A full Agent process run got through config-loading
cleanly (no decrypt-failure log lines) but then hit the pre-existing,
unrelated `TablesOptions`/missing-shared-config-blob crash from earlier
in this session - not a regression from this ADR, just not yet
independently confirmed via a full successful Agent startup.

## ADR-086 — CredentialEncryption:Key moved from common-config.secrets.json to appsettings.json

**Immediately superseded ADR-085's key-provisioning detail** (not its
core design - encrypt-in-place, `CredentialCipher`, the publish-time
guard are all unchanged), at the user's follow-up question: *"cant we
have the symetric key in the appsettings... this action happens when we
publish the agent?"* ADR-085 originally put the Agent-side key in the
local-only `common-config.secrets.json` (ADR-038's shared-secrets file)
because that was the existing "local-only, sensitive" convention
already in place - but it forced `TryLoadLocalSharedSecrets` to be
reordered ahead of the agent-config fetch in `Program.cs`'s top-level
sequence, purely so the key would be parsed before the blob download
that needs it.

**Why appsettings.json is actually the more consistent home, not a
regression.** `Storage:ConnectionString` already lives there, in plain
text, git-tracked - it has to, since it's needed to reach Blob Storage
in the first place, before any remote or local config source can be
loaded (ADR-037 confirmed this exact constraint for `Storage:ConnectionString`
itself). `CredentialEncryption:Key` has the identical bootstrap
constraint: it has to be in hand before the agent-config blob is
downloaded, so its `"enc:v1:"` values can be decrypted before that blob
becomes a config source. Putting it in the same file as
`Storage:ConnectionString` doesn't create a new exposure - anyone with
git/repo access to read the key already has the connection string
sitting right next to it, which is already broader access (full
read/write to every blob in the account, not just the ability to
decrypt one field inside blobs they could already read). The
`.secrets.json`-only constraint ADR-038 established was specifically
about keeping camera/device credentials out of git; the encryption key
was never itself one of those - it's closer in kind to
`Storage:ConnectionString` than to `RtspPassword`.

**What changed, concretely.** `Vivnest.Agent/appsettings.json` gained a
`CredentialEncryption:Key` field, same value as before. The now-empty
`Vivnest.Agent/common-config.secrets.json` was deleted - it had held
nothing else. `Program.cs`'s `TryParseCredentialEncryptionKey` now reads
`builder.Configuration["CredentialEncryption:Key"]` directly at the very
top, before the `LoadLocalSettings` branch - no extra loading call
needed, since `Host.CreateApplicationBuilder` already loads
`appsettings.json`/env vars before any of this file's code executes.
`TryLoadLocalSharedSecrets`/`TryLoadLocalAgentSecrets` moved back to
their original position (after the fetch block), restoring the exact
pre-ADR-085 structure there - they no longer have any reason to run
early, since neither carries the key anymore.

**What this does *not* solve, flagged directly to the user rather than
left implicit:** this is still a single static key with no rotation
story. Rotating it means updating `appsettings.json` on every Agent by
hand, and any blob already published under the old key (including old
immutable versions, and anything `RollbackAsync` might republish
verbatim later) becomes silently undecryptable the moment the key
changes, since ciphertext carries no key-generation identifier. The
user separately asked about a Cloud-served, centrally-rotatable key
instead - discussed and deliberately deferred as its own future ADR
(needs an Agent-facing authenticated endpoint, which doesn't exist
today since Agents only ever do direct blob/queue access, plus a
key-versioning scheme) rather than built as a bolt-on here.

**Files**: `Vivnest.Agent/appsettings.json`,
`Vivnest.Agent/common-config.secrets.json` (deleted),
`Vivnest.Agent/Program.cs`.

**Verification**: `dotnet build` clean.

## ADR-087 — Agent display Name sourced from the Admin registry, not a locally-typed value

**Why:** the dashboard's Agent Name has quietly had two independent
sources since ADR-051: `AgentRegistryEntity.Name` (typed once into the
Admin "+ Add Agent"/Edit form, stored in `tblAgentRegistry`) and
`AgentOptions.Name` (typed separately into each Agent's own local
`appsettings.json`, self-reported onto every `AgentHeartbeat`). The
dashboard's `AgentSummaryDto.Name` has always come from the *second* one
(`AgentQueryService.ToDtoAsync` reads `AgentHeartbeatEntity.Name`, never
the registry) - confirmed directly against this session's own "Sandeep"
tenant: Admin showed "Capture Agent," `appsettings.json` had "Capture
Agent - Test," and the dashboard showed the *second* string. Raised by
the user mid-session: *"the Agent name should be shown from the
tableAgent name property not from the appsettings's agent name... or
the name should be populated to the agent config, and that should be
sent from the agent side to tables."* The second framing is what got
built - keep the existing self-report-to-heartbeat mechanism, just fix
*where* the Agent gets the value it self-reports.

**The fix runs the Admin registry's Name through the exact same
publish/fetch pipeline every other Admin-owned value already uses -
zero new mechanism.** `AgentRuntimeConfigurationProjector.ProjectAsync`
already fetches the `AgentRegistryEntity` (`agent`, line 37, needed for
`RuntimeAgentId`) - `agent.Name` was sitting right there, now threaded
into a new `AgentRuntimeConfigurationDocumentDto.Name` field so both
`PublishAsync` and `RollbackAsync` (which both start from a fresh
`_projector.ProjectAsync` call, so `RollbackAsync` also gets the
*current* registry Name, not whatever the old rolled-back-to version
happened to carry - Name was never itself versioned/hashed content, same
treatment as `PublishedUtc`) get it for free with no second registry
lookup in the publisher. `AgentRuntimeConfigurationPublisher` writes it
as one more top-level sibling key (`NameKey = "Name"`, alongside
`ConfigurationPublishedUtc`/`ConfigurationSchemaVersion`/etc.) on both
the new versioned blob (`AgentConfigWireDocument.Name`) and the legacy
flat-blob merge - the exact established pattern for "Admin-owned value
that isn't part of `AiClassification`."

**Agent side: bound via the existing root-bound `AgentConfigMetadataOptions`
(ADR-065), not a new Options class or a nested `Agent:Name` re-binding
trick.** `AgentConfigMetadataOptions` already exists specifically for
top-level sibling keys on this same blob - adding `Name` there is
one line, consistent with every other field on it, and
`AgentHeartbeatWorker` already injects `IOptions<AgentConfigMetadataOptions>`
(`_configMetadata`) for `ConfigurationPublishedUtc`/`ConfigurationSchemaVersion`
checks. Changed line 91 from `Name = _agentOptions.Name` to
`Name = _configMetadata.Name ?? ""` - the only Agent-side code change
needed. Null (not yet published through this pipeline) reports as an
empty string on the heartbeat, same as a brand-new Agent showing blank
until Admin actually publishes once - not a new gap, `AgentSummaryDto`
already required a heartbeat entity to exist at all.

**Removed, not kept as a fallback:** `AgentOptions.Name` (the property)
and `appsettings.json`'s `Agent:Name` key are both deleted outright, not
left in as a "use if config metadata is null" fallback. A fallback would
have kept the exact two-sources problem this ADR exists to fix, just
demoted to a corner case - confirmed via full-repo grep that nothing
else read `AgentOptions.Name` before removing it.

**Files**: `Vivnest.Cloud/Api/Dtos/AgentRuntimeConfigurationDocumentDto.cs`,
`Vivnest.Cloud/Admin/AgentRuntimeConfigurationProjector.cs`,
`Vivnest.Cloud/Admin/AgentRuntimeConfigurationPublisher.cs`,
`Vivnest.Core/Options/AgentConfigMetadataOptions.cs`,
`Vivnest.Core/Options/AgentOptions.cs`,
`Vivnest.Agent/Runtime/Shell/AgentHeartbeatWorker.cs`,
`Vivnest.Agent/appsettings.json`.

**Verification**: `dotnet build` across the whole solution clean, no
warnings. Real-Azure verification (publish an Agent, confirm the
version/legacy blobs both carry the new `Name` key, confirm the next
heartbeat reports it and the dashboard reflects the Admin-set name) not
yet done as of this write-up - next step once the user re-publishes.

## ADR-088 — Image Capture's three minute-based fields renamed to seconds

**Why:** flagged by the user during the from-scratch walkthrough
(captured in a memory note at the time, deliberately deferred until
testing was done): the Capability's own schema shipped with
`ScheduleIntervalMinutes`/`BurstDurationMinutes`/`LivenessIntervalMinutes`,
inconsistent with `BurstIntervalSeconds` sitting right next to them on
the same schema. Renamed all three to their `*Seconds` equivalents so
the whole capability is unit-consistent - `BurstIntervalSeconds` itself
is untouched, it was already right.

**Two call sites hold the field names as literal string constants, both
updated identically:** `ImageCaptureRuntimeProjector.cs`'s five
`RequiredKeys` (Cloud, validates admin-typed Settings against these
exact names before projecting) and `ImageCaptureRuntimeAdapter.cs`'s
three `TryGetSeconds` calls (Agent, re-validates independently per this
project's "never trust a blob just because Admin generated it"
discipline - decision-log.md ADR-065). The Adapter's now-unused
`TryGetMinutes` helper (`TimeSpan.FromMinutes`) was deleted outright,
not left as dead code - all three call sites that used it now call the
already-existing `TryGetSeconds` (`TimeSpan.FromSeconds`) instead, so
there is no minutes-parsing code path left anywhere in this capability.

**Real values updated to match, not just the field names.** Both the
Capability catalog's `ConfigurationSchema`/`DefaultConfiguration` (Admin
> Capabilities > Image Capture) and Kitchen Camera's own live
`DeviceCapability.Settings` assignment need the same rename plus the
seconds-equivalent values the user specified earlier in this session for
this exact device (*"in my initial it was like this"*):
`ScheduleIntervalSeconds=900` (15 min), `BurstIntervalSeconds=30`
(unchanged), `BurstDurationSeconds=600` (10 min),
`LivenessIntervalSeconds=300` (5 min), `WarningMultiplier=3`
(unchanged, not a time field). This is a live-data change, not a code
change - tracked separately from this ADR, done interactively via the
Admin API once the user's local Functions host is back up.

**Files**: `Vivnest.Cloud/Admin/CapabilityProjection/ImageCaptureRuntimeProjector.cs`,
`Vivnest.Agent/Runtime/Configuration/ImageCaptureRuntimeAdapter.cs`.

**Verification**: `dotnet build` across the whole solution clean, no
warnings.

## ADR-089 — `Platform` prefix on baked-in, unconditional Agent workers

**Why:** the user asked for a naming convention to make baked-in
platform services (see the "Baked-in platform services vs.
Capability-catalog-driven behavior" section of `current-architecture.md`,
added alongside ADR-088) instantly recognizable when searching the
codebase, as distinct from real Capability-driven workers
(`CameraCaptureWorker`, `SinkCleanlinessWorker`, etc.). A namespace-based
distinction was floated first but doesn't actually hold -
`DeviceHeartbeatWorker` lived in `Capabilities/DeviceHealth`, not
`Runtime/Shell` alongside the other five baked-in workers - so a name
prefix is the only reliable signal. User confirmed: *"Platform prefix is
good."*

**Renamed, class + file, all six of `Vivnest.Agent`'s unconditionally-
registered `Program.cs` hosted services** (the ones outside any
`if (agentType == ...)` branch, with zero dependency on the Capability/
DeviceCapability catalog):
- `AgentHeartbeatWorker` → `PlatformAgentHeartbeatWorker`
- `DeviceHeartbeatWorker` → `PlatformDeviceHeartbeatWorker`
- `AgentMetricsWorker` → `PlatformAgentMetricsWorker`
- `CommandPollingWorker` → `PlatformCommandPollingWorker`
- `AgentCommandPollingWorker` → `PlatformAgentCommandPollingWorker`
- `LogShippingWorker` → `PlatformLogShippingWorker`

`DeviceHeartbeatWorker.cs` was renamed in place (`Capabilities/DeviceHealth/`)
rather than relocated to `Runtime/Shell/` - the rename was scoped to the
name only, not a reorganization the user didn't ask for.

**Scoped to `Vivnest.Agent` only.** Cloud-side services with an
analogous "runs unconditionally, not Capability-gated" role
(`HealthMonitorService`, `DeviceEventRetentionTimerFunction`,
`AgentEventRetentionTimerFunction`, `CommandExpiryTimerFunction`) were
deliberately left unrenamed - the user's ask was about searching the
Agent codebase for baked-in vs. Capability-driven workers specifically;
extending the convention Cloud-side wasn't requested and the Cloud side
doesn't have the same worker/handler naming collision risk the Agent
side does (`SinkCleanlinessWorker`, `CameraCaptureWorker`, etc. all live
in `Vivnest.Agent`).

**Mechanical, not behavioral.** Renames only - class declarations,
constructor names, `ILogger<T>` type parameters, physical filenames
(via `git mv` to preserve history), `Program.cs`'s six
`AddHostedService<T>()` calls, and every genuine source/comment
reference across the solution (~15 files, found via a repo-wide
word-boundary regex search, then filtered to drop incidental substring
matches like `AgentCommandPublisher`/`AgentCommandDto`/`AgentHeartbeat`
domain class which share a name fragment but aren't the renamed types).
No logic changed.

**Files**: `Vivnest.Agent/Capabilities/DeviceHealth/PlatformDeviceHeartbeatWorker.cs`,
`Vivnest.Agent/Runtime/Shell/PlatformAgentHeartbeatWorker.cs`,
`Vivnest.Agent/Runtime/Shell/PlatformAgentMetricsWorker.cs`,
`Vivnest.Agent/Runtime/Shell/PlatformCommandPollingWorker.cs`,
`Vivnest.Agent/Runtime/Shell/PlatformAgentCommandPollingWorker.cs`,
`Vivnest.Agent/Runtime/Shell/PlatformLogShippingWorker.cs`,
`Vivnest.Agent/Program.cs`, plus comment-only updates in
`Vivnest.Agent.Updater/DeployPollingWorker.cs`,
`Vivnest.Agent/Capabilities/Bridges/HomeAssistant/HomeAssistantConnectionTracker.cs`,
`Vivnest.Agent/Capabilities/Bridges/TapoHub/TapoHubLivenessWorker.cs`,
`Vivnest.Agent/Capabilities/Camera/SinkCleanlinessWorker.cs`,
`Vivnest.Agent/Capabilities/SmartPlug/SmartPlugPowerStateChangedHandler.cs`,
`Vivnest.Agent/Runtime/Commands/ICommandHandler.cs`,
`Vivnest.Agent/Runtime/Shell/AgentLogBufferLoggerProvider.cs`,
`Vivnest.Agent/Runtime/Shell/IAgentLogBuffer.cs`,
`Vivnest.Agent/Runtime/Shell/INetworkUsageTracker.cs`,
`Vivnest.Cloud/Admin/AgentRuntimeConfigurationPublisher.cs`,
`Vivnest.Core/Options/AgentConfigMetadataOptions.cs`,
`Vivnest.Dashboard/src/AgentMetricsChart.tsx`,
`docs/architecture/current-architecture.md`.

**Verification**: `dotnet build` on `Vivnest.slnx` (whole solution)
clean, 0 warnings, 0 errors, after stopping the two stale local dev
processes (a running `Vivnest.Agent.exe` and the local Functions host)
that were holding the previous build's DLLs locked.

## ADR-090 — Agent container registry moved from `vivnestagentacr` to `vivnestagent2acr`

**Why:** user-initiated registry migration - `vivnestagent2acr` (created
2026-08-16, resource group `rg-vivnest-2`) replaces `vivnestagentacr`
(`rg-vivnest-dev`) as the real registry the Agent image is built and
deployed from, going forward. User confirmed this is a **permanent
switch**, not a one-off push to test the new registry.

**Every hardcoded reference to the old registry updated to the new
one:**
- `scripts/build-and-push-agent.ps1` - `$RegistryName`.
- `scripts/update-agent.ps1` - `$Image` and its own doc comment.
- `Vivnest.Agent.Updater/AgentDeployer.cs` - the `Registry` const (ADR-039's
  login-step/pull/run shared source of truth for the registry hostname).

**A fresh ACR repository-scoped pull token was minted on the new
registry** (`az acr token create --repository vivnest-agent
content/read`, same ADR-039 pattern exactly - pull-only, single
repository, no ACR admin credentials) - the old token was scoped to
`vivnestagentacr` and has no access to `vivnestagent2acr`, so it
couldn't simply be reused. New token's password written into the
gitignored build-output copies of `updater.settings.json` (both
`bin/Debug/net10.0/` and `bin/Release/net10.0/win-x64/`), same as
ADR-039's original placement - never into the source-tree template.

**Verified for real**: `dotnet build` on `Vivnest.slnx` clean after the
`AgentDeployer.cs` change; `build-and-push-agent.ps1` run for real -
built the Agent image (`FirmwareVersion=cd6daca`, the current git SHA,
no `-Version` passed), `docker login vivnestagent2acr.azurecr.io`
succeeded, and `vivnestagent2acr.azurecr.io/vivnest-agent:latest` is
now a real, pushed image - confirmed present via `az acr
repository show-tags --name vivnestagent2acr --repository vivnest-agent`.

**Not yet done**: no Agent process has actually been redeployed against
this new image/registry yet (the standing Capture/AI agents still run
locally via `dotnet run`, not containers) - this ADR covers the build/
push/registry-pointer side only. The `vivnestagentacr` registry itself
was left in place, untouched, not deleted.

**Files**: `scripts/build-and-push-agent.ps1`,
`scripts/update-agent.ps1`, `Vivnest.Agent.Updater/AgentDeployer.cs`.

## ADR-091 — `--credentialencryptionkey` flag on the Updater

**Why:** discovered via a real crash - the first live install-token
self-registration run against a real Agent (RuntimeAgentId
`91923eba-...`, the Capture agent) produced a container stuck in a
crash-restart loop:
`System.FormatException: No valid combination of account information
found` out of `QueueServiceClient`'s constructor. Root cause traced to
`CredentialCipher.IsCredentialField` (`Vivnest.Core/Security/CredentialCipher.cs:27`)
- any field whose name contains "connectionstring" is treated as
credential-shaped and published **encrypted** to the shared-config blob
(ADR-085). `Messaging:ConnectionString` matches that pattern, so it
arrives at the Agent as `enc:v1:...` ciphertext, decryptable only with
`CredentialEncryption:Key` - a key
`WriteAgentAppSettingsFromRegistration` never wrote into the
self-registered `appsettings.json`, because the key deliberately never
travels through `RegisterInstallationResponse` or the blob it protects
(same reasoning as ADR-039's ACR credentials - it has to land on a new
host through a manual, secure channel, not an automated one). Confirmed
by the container's own startup log: `"CredentialEncryption:Key is not
set; encrypted config fields will not be decrypted."` Fixed
immediately by hand-patching the mounted `appsettings.json` with the
tenant's existing key and restarting the container - real heartbeats,
device heartbeats, and a real camera capture all confirmed working
afterward.

**This ADR is the follow-up fix**, so the next self-registered agent
doesn't need the same by-hand patch. New `--credentialencryptionkey`
flag, two call sites:
- `WriteAgentAppSettingsFromRegistration` (`Vivnest.Agent.Updater/Program.cs`)
  now takes an extra `credentialEncryptionKey` parameter, threaded from
  `GetArgValue(args, "--credentialencryptionkey")` at its one call site
  in `TryRegisterFromInstallTokenAsync` - so `--installtoken ...
  --credentialencryptionkey ...` provisions the key in the same single
  command as registration, no separate step.
- New `ApplyAgentAppSettingsOverridesFromArgs` - the
  `appsettings.json`-side counterpart to the existing
  `ApplySettingsOverridesFromArgs` (which only ever touches
  `updater.settings.json`, a different file the real `Vivnest.Agent`
  process never reads). Covers fixing an *already-registered* agent
  (exactly the situation just hit) without burning a new install token
  just to add one field - `--credentialencryptionkey` alone, no
  `--installtoken`, patches the existing file in place.

Both no-op if the flag's absent, same convention as every other
override flag in this file (`--agent`/`--container`/
`--connectionstring`/`--acrusername`/`--acrpassword`).

**Verified for real**: `dotnet build` on `Vivnest.slnx` clean; ran the
new flag against a scratch `appsettings.json` copy and confirmed the
`CredentialEncryption.Key` section was added correctly with existing
keys (`LoadLocalSettings`, `Agent`) left untouched.

**Files**: `Vivnest.Agent.Updater/Program.cs`.

---

## ADR-091 - Configuration blobs are named by tenant and site

**Context.** `device-config` and `agent-config` named every blob by runtime
id alone: `{runtimeDeviceId}.json`, `{runtimeDeviceId}/current.json`,
`{runtimeDeviceId}/versions/{n}.json`. Every tenant's configuration sat in
one flat container with nothing in the name to separate them.

That is not just untidy. `Vivnest.Agent`'s startup path listed the WHOLE
container and downloaded every blob in it, checking `OwningAgentId` on each
document only after the download, discarding the ones it did not own. So
every agent routinely pulled every other tenant's device names, locations,
brands, models and RTSP URLs across the wire. Credential-shaped fields are
ciphertext (ADR-085); none of the rest ever was.
`DeviceCapabilitiesQueryService.BuildTriggeredByAsync` did the same thing
Cloud-side.

**Decision.** Blob names carry the tenant and site as a path prefix:
`{tenantId}/{siteId}/{runtimeId}.json` and friends. One new type,
`ConfigBlobKey(TenantId, SiteId, RuntimeId)`, builds every name; a key
with either field missing yields exactly the old unscoped name, so both
layouts are one code path rather than a branch at every call site.

Readers try the scoped name first and the unscoped one second. Writers
publish to BOTH layouts on every publish.

**What this does and does not fix.** It fixes the normal path: an Agent now
enumerates and downloads only its own prefix, and the startup scan is
O(this site's devices) rather than O(every device in the storage account).
It does NOT establish a tenant boundary on its own, because Agents still
hold an account-level `Storage:ConnectionString` and can therefore read any
prefix they like. That remains open, and closing it means issuing scoped,
short-lived SAS instead of the account key - which this ADR is the
prerequisite for, since a SAS can only be scoped to a prefix that exists.

**Why dual-write rather than a migration script.** Every Agent currently
deployed reads the unscoped layout and nothing else. A one-shot move would
strand all of them on their last-known-good config until each was rebuilt
and restarted - including any that happen to be offline during the
migration, which is exactly the population least able to recover on its
own. Dual-write costs three extra blob uploads per publish and lets the
estate migrate agent by agent, in any order, with no coordination.

It is deliberately NOT behind a feature flag. A half-migrated estate is the
normal state during a rollout, not an exceptional one, and a flag would
only add a way to get it wrong.

Only the scoped version blob is written with `failIfExists` as the
concurrency check; the legacy copy mirrors an already-won version, so a 409
there means the mirror already exists and is ignored.

**An unchanged entity still has to migrate.** Found by running the
republish pass against real dev storage, not by reasoning: of three
entities, the one device whose content had changed migrated correctly and
both agents got *nothing*. They were content-hash no-ops, and the ADR-069
guard returns before any blob is written.

That guard exists to stop a republish burning a version number. It should
never have been the thing deciding blob *placement* - and in a settled
system most entities are unchanged, so the migration would have covered
only whatever happened to change, and removing the dual-write later would
have stranded the rest. Silently: the entity looks fine until the legacy
blobs go away.

`RuntimeConfigurationWriter.BackfillScopedLayoutAsync` closes it. On the
no-op path, if the scoped manifest is absent, the already-published version
is copied from the legacy layout into the scoped one - same version number,
no new version, no restart dispatched. It is skipped entirely once the
scoped manifest exists, so re-running a republish over a migrated estate
costs one extra read per entity and writes nothing.

The manifest is rebuilt rather than copied, because `ConfigurationUri` is a
full container-relative name: a verbatim copy would leave the scoped
manifest pointing back into the legacy layout, so deleting the legacy blobs
later would break exactly the entities the backfill exists to rescue.

**The legacy layout is gone — DONE 2026-08-21, code and blobs.**

This happened in two steps on the same day, and the second one overtook the
first. Recorded that way rather than tidied into one, because the reasoning
for stopping halfway was sound and the reason it was overtaken is a fact
about the product, not a change of mind.

**Step 1, the dual-write.** The stated exit condition was met: the only live
deployed Agent reported `FirmwareVersion 1.1.1` with 23 hours of uptime, and
`1.1.1` reads the scoped layout. (The other heartbeat row is a dev-machine
`local-dev` instance, last seen 51 hours earlier, out of scope.) The
`if (!key.IsUnscoped)` block in `RuntimeConfigurationWriter.WriteVersionAsync`
went. The read fallbacks and `BackfillScopedLayoutAsync` were deliberately
kept, because the pre-scoping blobs still existed and were still the rollback
path for an older image.

**Step 2, everything else.** The user's call, and it dissolves step 1's
caution rather than overriding it: *"in production, pre-scoping blobs will
not happen, we should not have any code for that."* Vivnest2 has no
production estate that predates scoping — there is nothing for the fallback
to rescue, in this environment or any future one. Code kept for a case that
cannot arise is not a safety net, it is a second code path that every future
reader has to understand and every future change has to preserve.

**What went.** All six read fallbacks (`Vivnest.Agent/Program.cs`,
`ConfigVersionCommandHandlerBase`, both runtime configuration publishers,
`ConfigurationSyncStatusService` ×2, `DeviceCapabilitiesQueryService` ×2),
`BackfillScopedLayoutAsync` with its two now-unused helpers
(`TryDownloadAsync`, `MirrorVersionBlobAsync`), the unscoped `BlobName`/
`VersionBlobName`/`ManifestBlobName` string overloads on both
`AgentConfigBlob` and `DeviceConfigBlob`, and `ConfigBlobKey.Unscoped()`/
`IsUnscoped`.

**An empty tenant or site is now an error, not a layout.** `ConfigBlobKey`'s
constructor throws. This is the one behavioural change worth arguing about:
previously an Agent with no `Agent:TenantId` silently addressed the flat
names, which existed. Now those names address nothing, so the same
misconfiguration would surface as "no configuration found" — the wrong
diagnosis, pointing at storage instead of at the Agent's own settings. It
fails loudly at the point the information is missing instead.

**The blobs went too, but not before their history was preserved.** Deleting
them naively would have destroyed something: the device had legacy
`versions/1`–`7` with **no** scoped counterpart (its scoped history started
at 8), and one agent had a legacy `versions/1`. Those are exactly what
`ConfigRollback` reads. Eight orphaned version blobs were server-side copied
into the scoped layout first, every legacy blob was then confirmed to have a
scoped counterpart, and only then were all 19 deleted. Rollback range is
unchanged. The account has soft delete at 7 days, so the delete was
recoverable in any case — a second net, not the plan.

`DeviceConfigRuntimeAdapter`'s legacy branch is **not** part of this and
stays. It detects document *shape* (pre-ADR-064 flat `DeviceOptions` vs the
`capabilities[]` document), which has nothing to do with where a blob is
named. The two were listed together in the retired dead-code report; they
are separate items.

---

## ADR-092 - Device runtime state is not a camera concept

**Context.** `CaptureStatusStore` / `ICaptureStatusStore` /
`DeviceRuntimeState` lived in `Vivnest.Core/Camera/Stores`, under a
`Vivnest.Core.Camera.Stores` namespace. That was accurate when the platform
only did cameras. It stopped being accurate several capabilities ago: a
smart plug reading, a motion sensor event, a TapoHub poll and a Home
Assistant state change all write to it. Twelve call sites across Camera,
SmartPlug, MotionSensor, Bridges/HomeAssistant, Bridges/TapoHub and
DeviceHealth.

Recorded as L4 in the 2026-08 dead-code audit (file since retired; see
git history).

**Decision.** Moved to `Vivnest.Core/Devices/Stores`
(`Vivnest.Core.Devices.Stores`), and renamed:

| Was | Is |
|---|---|
| `ICaptureStatusStore` | `IDeviceRuntimeStateStore` |
| `CaptureStatusStore` | `DeviceRuntimeStateStore` |
| `DeviceRuntimeState` | unchanged - it was already named correctly |

`ICamera`, `ICameraFactory` and `Camera/Models` stay exactly where they
are. Those genuinely are camera-specific; this was never a blanket
de-camera-ing of `Vivnest.Core`.

**Why bother with a pure rename.** CLAUDE.md and ADR-007 both say "camera"
must not read as an architectural boundary, and this was the clearest place
the code said otherwise. The failure mode is not confusion, it is
duplication: someone adding a water-meter capability sees
`Vivnest.Core.Camera.Stores`, reasonably concludes it is not for them, and
writes a second store. That is precisely how the two near-identical
configuration publishers came about (U-D2), which cost far more to unpick
than this rename cost to do.

**Older ADRs are left alone.** Entries before this one still say
`ICaptureStatusStore` - they are point-in-time records of decisions taken
then, and rewriting them would falsify the log. This table is the mapping.

**Verified**: all 7 projects compile clean; no reference to the old names
survives anywhere in source, including string literals (nothing resolved
these by name). No behavioural change - no logic, storage, configuration or
wire format touched.

**Files**: `Vivnest.Core/Devices/Stores/*` (moved via `git mv`, so history
follows), plus 13 referencing files across `Vivnest.Agent` and
`Vivnest.Core`.

---

## ADR-093 - Operational alerting: throttle first, no LLM in v1

**Context.** The Agent has shipped its own Warning/Error log lines to
`agent-logs/{agentId}.txt` since ADR-027, but nothing reacts to them. You
find out an Agent is failing when you think to go and look, or when a device
stays quiet long enough for the offline alert to fire. Roadmap Sprint 8
designed the fix and then deliberately blocked it on one question -
rate limiting - rather than building it and discovering the answer in
production.

**Decision.** Built the pipeline, with two changes to the design as written.

```text
Agent: Error-level log call
  -> AgentLogBufferLoggerProvider also fills IAgentErrorSignalBuffer
  -> PlatformErrorEventWorker drains it: AgentEvent row + agent-events queue
  -> Cloud: AgentEventQueueFunction refetches the event
  -> AgentAlertThrottle decides whether anyone should be told
  -> existing NotificationDispatcher (no new channel)
```

**Change 1: no LLM in v1.** Sprint 8 put an `ILlmService` between the event
and the notification, to turn a raw stack trace into a triage summary. That
is the expensive, non-deterministic part of the idea, and it is the part
hardest to judge without real traffic. Forwarding the error as-is closes
most of the gap - you learn the Agent is failing, which today nothing tells
you - and leaves triage to be added once there is something to evaluate it
against. `NotificationTypes.AgentErrorLogged` and the payload shape are
unchanged by adding it later.

**Change 2: throttle on (agent, signature), with a per-agent ceiling.** Two
gates, because they fail differently:

- **Per-signature cooldown** (default 10 min) suppresses the *same* fault
  repeating - the crash-loop case, which this codebase has hit twice for
  real (ADR-023's RTSP timeout, ADR-024's restart-policy incident). Keyed on
  the signature rather than on the agent, because a per-agent cooldown would
  let one noisy subsystem silence a different and possibly worse fault on
  the same agent.
- **Per-agent hourly ceiling** (default 12) is the backstop for what the
  cooldown structurally cannot catch: many *distinct* errors at once, where
  every one is a new signature and so every one passes gate 1.

The ceiling is only consumed when a notification actually goes out, so
suppressed duplicates never eat the budget - otherwise a crash loop would
exhaust the hour's allowance without telling anyone anything.

**The signature.** SHA-256 over `category|message` with GUIDs, timestamps
and bare numbers normalised to `#`. Without that normalisation
`"capture 41 failed"` and `"capture 42 failed"` are different signatures,
the cooldown never engages, and the throttle is decorative. Not a plain
string hash: this is persisted as a RowKey, and .NET randomises
`string.GetHashCode` per process, so dedup would silently reset on every
restart.

**A fixed window, not sliding.** Sliding would need every notification
timestamp in the last hour; fixed needs two fields. The cost is that a burst
straddling a boundary can send up to 2x the limit across two adjacent hours.
Acceptable for something whose job is "stop hundreds", not "meter precisely".

**Throttling is Cloud-side, not Agent-side.** The Agent reports what it
sees; Cloud decides what is worth telling a person. Agent-side throttling
would mean every agent independently guessing at a fleet policy, and would
lose the events from the table entirely rather than merely suppressing a
notification.

**Re-entrancy.** The error path must never log an Error itself. The logger
provider fills a buffer; a BackgroundService drains it. `PlatformErrorEventWorker`
logs failures at **Warning**, deliberately below its own trigger level - a
persistent storage failure would otherwise feed itself forever, and storage
being unhappy is exactly when you most want this to work. The buffer is
bounded and drops the NEWEST signal when full, the opposite of
`AgentLogBuffer`'s ring: under a crash loop the first errors are the
informative ones.

**Off by default.** `OperationalAlert:Enabled` is false. Turning alerting on
should be a deliberate act, not something a deploy does.
`AzureTableAgentAlertStateStore` builds its table client lazily for the same
reason - `AzureTableStore`'s constructor calls `CreateIfNotExists()`, and an
un-opted-in deployment has no reason to have set `Tables:AgentAlertState`,
yet the Agent may still be publishing to `agent-events` based on its own
config. Disabled-and-unconfigured is silent; enabled-and-unconfigured throws
with a message naming the setting.

**To turn it on**: `OperationalAlert__Enabled=true`,
`Tables__AgentAlertState=tblAgentAlertState`, and
`Messaging__AgentEventQueue=agent-events` on the Agent side.
`OperationalAlert__CooldownPerSignature` and
`__MaxNotificationsPerAgentPerHour` override the defaults.

**Tests**: `Vivnest.Tests/AgentAlertThrottleTests.cs` - 12, covering
signature normalisation and its limits (different faults must stay
distinguishable), both gates independently, cooldown expiry, window
rollover, that suppressed duplicates do not consume the ceiling, and that
the ceiling is per agent rather than global.

**Verified end to end against real Azure**, 2026-08-20, by pointing the
Kitchen Camera at `192.0.2.1` (RFC 5737 TEST-NET-1, guaranteed unroutable)
and republishing. FFmpeg timed out, `CameraCaptureService` logged an Error,
and the whole chain ran:

| Time (UTC) | What happened |
|---|---|
| 09:19:25 | `ErrorLogged` row written to `tblAgentEvents` |
| — | `agent-events` queue auto-created on first publish |
| 09:19:28 | deployed `AgentEventQueueFunction` processed it; throttle wrote signature `f41ed715f7dcb044` and ceiling `count=1` |
| 09:24:55 | 4th failure, 5.5 min later - **suppressed**, event row written, `lastNotified` and `count` unchanged |

Four faults, one alert. That is the behaviour Sprint 8 blocked the feature
on, on live data rather than a fake.

**The first real message exposed a bug every test had missed.** Every event
was being retried to death into `agent-events-poison`:

```
System.NotSupportedException: DateTime 1/01/0001 12:00:00 am has a Kind of
Unspecified. Azure SDK requires it to be UTC.
   at AgentAlertThrottle.ShouldNotifyAsync
```

`AgentAlertStateEntity` carries two `DateTime` fields and each row type sets
only one - a signature row leaves `WindowStartedUtc` alone, a ceiling row
leaves `LastNotifiedUtc` alone. The unset one defaulted to
`default(DateTime)`, which is `0001-01-01` with `Kind.Unspecified`, and
Azure Tables refuses to serialise that at all. The write failed, the handler
threw, the throttle recorded nothing: the feature was dead on its first real
message while reporting twelve green tests.

Both fields now default to `DateTime.UnixEpoch` (`Kind.Utc`). The real rows
show `1970-01-01` in whichever field they do not use, which is the fix
visible in the data.

**The lesson is about the fake, not the field.** All 12 throttle tests
passed throughout, because the hand-written `FakeStore` accepted any
`DateTime` while Azure accepts only UTC. **A fake more permissive than the
thing it stands in for hides exactly the bugs it was written to catch.**
`FakeStore` now asserts `DateTimeKind.Utc`, with a message naming the SDK
error it stands in for; reverting the entity fix now fails 8 of the 12.

This is the second time in two days that running something against real
storage found what the suite could not - see ADR-091's backfill gap, found
the same way. Both were in the space the tests did not think to assert, not
in logic the tests got wrong. Worth weighing when judging what green means
in this codebase.

**Still unproven: the final delivery hop.** `Telegram__Enabled` is `false`
on `vivnestcloud2`, so no message has actually been sent. Everything up to
and including the dispatch decision is verified; whether an alert reaches a
human is not. Enabling Telegram switches on every other notification type at
the same time, which is why it was left alone.

**Cost of the test, recorded because it was not free**: two config versions
burned on a live device (bad host, then restored to `192.168.50.166`), and
the owning agent restarted twice.

---

## ADR-094 - The whole environment moved to `rg-vivnest-2`, not just the registry

**Why this exists.** ADR-090 recorded the container registry moving from
`vivnestagentacr` (`rg-vivnest-dev`) to `vivnestagent2acr` (`rg-vivnest-2`)
and called it a permanent switch. That was accurate and incomplete: the
Function App, the storage account and the dashboard moved too, and nothing
recorded it. The result was a half-record - one component documented as
having moved, three silently relocated - which is worse than no record,
because it reads as "the registry moved and the rest did not."

That cost real time on 2026-08-20: working out which Function App was live
required listing resources in Azure and asking, because two apps existed
(`vivnestcloudprod` and `vivnestcloud2`) and the docs named the wrong one.

**The mapping, verified against Azure on 2026-08-21:**

| Component | Was (`rg-vivnest-dev`) | Is (`rg-vivnest-2`) |
|---|---|---|
| Function App | `vivnestcloudprod` | `vivnestcloud2` |
| Storage account | `stvivnestagentdev` | `stvivnestagent2` |
| Container registry | `vivnestagentacr` | `vivnestagent2acr` (ADR-090) |
| Dashboard | `vivnest-dashboard` (Static Web App) | `vivnest-dashboard-2` |
| App Service plan | - | `ASP-rgvivnest2-aa75` |

**`rg-vivnest-dev` is the V1 generation and is out of scope.** Confirmed with
the user: the resources still sitting there belong to the first-generation
Vivnest codebase, which is a different repository. Do not diff Vivnest2's
expectations against them, do not deploy Vivnest2 code to them, and do not
read `vivnestcloudprod` missing a setting as a gap - it runs different
software.

**Older references are left alone on purpose.** The ADR recording the first
deployment still names `vivnestcloudprod`; that was true when written, and
rewriting a point-in-time record to match today would falsify the log - the
same reasoning ADR-092 applies to the renamed runtime-state store. This
entry is the mapping to read them through.

**One thing did not move with the rest.** The blob lifecycle policy in
`devops/blob-lifecycle/` was applied to `stvivnestagentdev` and was never
applied to `stvivnestagent2` - verified: `az storage account
management-policy show` returns nothing for the new account. Capture images
are therefore not being aged out in this environment. The user has accepted
this as a known infrastructure difference rather than a defect; recorded here
so it is a decision rather than an oversight.

---

## ADR-095 - The Agent composition root split, and a capability host with one capability

**What changed (2026-08-22).** `Program.cs` went from ~1300 lines to a thin
entry point; configuration loading and service registration moved to seven
files under `Vivnest.Agent/Bootstrap/`. Three projects were added:
`Vivnest.Abstraction` (contracts, no references), `Vivnest.Runtime`
(`EventDispatcher`, `CapabilityHost`, `CapabilityRegistry`,
`CapabilityContext`, `CapabilityHostedService`) and `Vivnest.Domain`
(currently empty). Camera became the first `ICapability`.

**This is early against CLAUDE.md's own rule, deliberately.** That file says
not to extract a capability host or separate `Vivnest.Runtime`/
`Vivnest.Abstraction` projects speculatively - extract them when a second
real consumer needs them. There is one capability. Recorded here as a
decision rather than an oversight: the target architecture in
`vivnest-runtime-overview.md` does head here, and the user chose to start
the move now. What it costs in the meantime is stated plainly below rather
than discovered later.

**The half-migration is the real cost.** Camera runs through the capability
host; smart plug, motion sensor, Home Assistant and sink cleanliness are
still plain hosted services. Two mechanisms now do the same job with no
stated rule for choosing between them. Every worker added before the
migration finishes is a coin flip that a later reader has to justify.

**The trap that was avoided, and is worth naming so it stays avoided.**
`CameraCaptureWorker` is registered `AddSingleton`, not `AddHostedService`,
and started by `CameraCapability`. Registering it both ways would start the
capture loop twice - two captures, two uploads, two `DeviceEvent` rows per
tick, all of which would look like a camera misconfiguration rather than a
DI mistake. Verified live on 1.1.3: exactly one capture per cycle.

**Startup order reversed as a side effect.** `AddAgentInfrastructure()`
registers `CapabilityHostedService` before `AddAgentPlatform()` registers
the seven `Platform*` workers, and hosted services start in registration
order. Capture now runs before heartbeat, command polling and log shipping.
Nothing is lost - the log and error buffers are singletons that retain
whatever is raised before their workers start - but the change was
incidental, not intended. Moving the capability-host registration after
`AddAgentPlatform()` restores the previous order.

**Two things this broke that no local build could catch:**

1. **The Agent image would not build at all.** `Vivnest.Agent/Dockerfile`
   copies an explicit list of project directories, not the solution, so the
   two new projects did not exist inside the build context: `dotnet restore`
   skipped them and the publish failed with ~80 `CS0234` errors. `dotnet
   build` passes locally because every project is on disk. **Every new
   project now needs a `Dockerfile` line** - the standing tax of a
   copy-list Dockerfile, worth paying knowingly.

2. **`ArchitectureDocCoverageTests` silently lost its subject.** It scanned
   `Program.cs` alone for `AddHostedService<>`; every call moved to
   `Bootstrap/*`, so it matched zero workers. Only the `Assert.NotEmpty`
   guard turned that into a failure instead of a permanently vacuous pass -
   which is the entire argument for writing that guard. The scan now covers
   the whole Agent project.

**Known loose ends, recorded rather than fixed:** `ICapabilityWorker` has
no implementations; `Vivnest.Domain` has no source files;
`ICapabilityContext` exposes `IServiceProvider`, which is a service-locator
escape hatch that will eventually be used for something awkward.

### Update - `9012603`, same day, several hours later

The refactor moved again before this entry was a day old. Recorded as an
update rather than a rewrite, because the direction of travel is the useful
part.

**Two more capabilities.** Smart plug and motion sensor joined camera, so
three of six workers now run through the host and three (Home Assistant,
Tapo hub liveness, sink cleanliness) remain plain hosted services. The
`AddSingleton`-vs-`AddHostedService` distinction held across all six - no
worker is registered both ways, which is the failure this arrangement is
most exposed to.

**The manifest grew teeth it does not yet use.** `CapabilityManifest` now
declares `Commands`, `ProducedEvents`, `ConsumedEvents` and
`Dependencies`; `ICapability` exposes a `CapabilityStatus` that
`CapabilityHost` logs on every transition; `ICapabilityContext` carries
tenant and site. Nothing dispatches on any of it yet - it is declaration
without consumption, which is fine as a step and misleading if left to look
finished.

**Cloud is in scope now, which it was not at `7d6e4c9`.**
`AgentRuntimeConfigurationProjector` reads `tblAgentCapabilities`, resolves
each `Active` assignment against the `Capability` catalogue, and projects
`AgentCapabilityRuntimeDto` into the agent config document. **The Agent
does not read it** - no `Capabilities` binding exists in
`AgentConfigurationLoader` or `AgentOptions` - and
`RuntimeCapabilityAssignmentStore`, the obvious consumer, is not registered
in DI or referenced anywhere. The join is half-alive: the data arrives and
is discarded.

**The id-space problem to solve before that wiring lands.** Agent manifest
ids are `camera.capture` / `motion.sensor` / `smartplug.monitor`. Cloud
capability ids are catalogue rows plus the built-in `"ImageCapture"`
constant that `ExecuteCapability` authorizes against.
`ICapabilityRegistry.Get(id)` is never called, so nothing joins them today
and Flow 7 still resolves to `DeviceTriggeredEvent` unchanged. Whoever
connects assignments to capabilities has to pick one id shape first;
discovering the mismatch at that point would look like a bug in whichever
half was written second.

**Deployment note:** because Cloud changed at `9012603`, a Functions
deploy is now required to keep `vivnestcloud2` in step - it was not at
`7d6e4c9`, which was Agent-only.

---

## ADR-096 - Capability assignment: the registry says *can*, Cloud says *may*

**The rule this establishes.** The Agent's capability registry describes what
the Agent *can* do. Cloud's `AgentCapability` configuration describes what
this Agent is *allowed and configured* to do. A capability starts only at the
intersection: registered **and** enabled. Neither side alone starts anything,
and that is the whole point of the separation.

**The path.** `AgentRuntimeConfigurationProjector` (Active assignments,
resolved against the `Capability` catalogue) → `AgentRuntimeConfigurationPublisher`
(root-level `Capabilities` array, **included in the content hash** so an
assignment change cannot be swallowed by the ADR-069 no-op guard) →
`AgentCapabilityAssignmentFactory` → `RuntimeCapabilityAssignmentStore` →
`CapabilityHost.StartAsync`.

**Verified behaviour**, driven through the real `CapabilityHost`,
`CapabilityRegistry` and `RuntimeCapabilityAssignmentStore`:

| Scenario | Result |
|---|---|
| Assigned + enabled | starts |
| Assigned + disabled | does not start |
| Not assigned | does not start |
| Assigned, no implementation registered | warning, nothing starts, no crash |
| Three registered, two assigned + enabled | exactly those two start |
| Registered but unassigned | does not start |

**An enabled assignment with no implementation warns rather than fails.**
A fleet on mixed builds will routinely have Cloud assigning a capability an
older image does not carry; crashing there would turn a rollout into an
outage. A capability that *is* selected and throws during `StartAsync` still
brings the host down - left as-is deliberately, because whether one failed
capability should kill an Agent is a fault-isolation decision, not a
side effect to settle here.

**The bug this shipped with, and why it deserves an ADR paragraph.** The
factory originally called `GetSection("Capabilities").Bind(options)` where
`options` was an object whose own list property was also named
`Capabilities`. The publisher writes `root["Capabilities"] = [ ... ]`, so
that section's children are the array indices `0`, `1`, ... and the binder
went looking for `Capabilities:Capabilities`, found nothing, and returned an
empty list. No exception, no warning, no log line.

Downstream, an empty list is indistinguishable from "Cloud assigned
nothing", so `Enabled assignments: 0` and `selected for startup: 0` - which
is a *correct-looking* log for an incorrect reason. The effect is that no
capability ever starts, and on the camera agent that means capture silently
stops. Fixed to `GetSection("Capabilities").Get<List<T>>()`, confirmed
against the exact JSON the publisher emits: the old call binds 0, the fixed
call binds 1.

The general lesson is the one this codebase keeps relearning: a silent empty
collection is the most expensive failure shape available, because every
layer downstream reports success.

**BLOCKED: the two id spaces do not meet, proven against live data
(2026-08-22).** A publish of the Capture Agent produced config version 3
whose blob carries exactly the expected shape:

```json
"Capabilities": [
  { "CapabilityId": "5217f0ef-f7c6-4d9f-9723-7bf2afad5572",
    "Name": "Image Capture", "Enabled": true }
]
```

`CapabilityId` is the catalogue **RowKey**, a GUID, because `tblCapabilities`
is a GUID-keyed admin registry carrying `CapabilityName`, `CapabilityType`,
`ConfigurationSchema` and defaults. The Agent's registered manifest ids are
`camera.capture`, `motion.sensor`, `smartplug.monitor`. `CapabilityHost`
matches assignment id against manifest id, so the intersection is empty and
always will be.

The live consequence is not a failed test, it is an outage: deploying an
Agent build with capability gating against today's catalogue yields
`Registered 3, Enabled assignments 1, Selected 0`, a warning about
`5217f0ef-...`, and **camera capture never starts**. The 5F-C.6 Test 1
scenario produces Test 4's behaviour. The Agent was deliberately not
upgraded for this reason.

Resolving it is a product decision, not a mechanical fix, and it is the last
thing standing between here and a proven end-to-end path:

- **Give `Capability` a stable string key** (`camera.capture`) alongside its
  GUID RowKey, and project that. The manifest id stays an implementation
  fact; the catalogue gains the vocabulary that joins them. Costs a column,
  a projector change and a backfill of two rows.
- **Key the catalogue by the string itself** - simpler, but changes the
  identity of existing rows and every assignment pointing at them.

Making the Agent's manifests carry catalogue GUIDs is the third option and
is rejected on sight: an implementation should not know a registry's primary
keys.

### 5F-C.6 - proven live, 2026-08-22

`CapabilityKey` is now settable (create and update), both catalogue rows are
backfilled (`Image Capture` -> `camera.capture`, `Sink Cleanliness` ->
`sink.cleanliness`), and the projector **refuses to publish a capability
with no key**, with a warning naming it - publishing one produced an
assignment the Agent could only warn about under a meaningless id.
`AgentCapabilityAssignmentFactory` now filters on `CapabilityKey`, the field
it actually assigns; it filtered on `CapabilityId` while assigning
`CapabilityKey`, so a keyless row survived the filter and became an
assignment with an empty id.

Run against `vivnestcloud2` and the live Capture Agent on `1.1.4`:

| Scenario | Log | Result |
|---|---|---|
| Assigned + enabled | `Registered 3. Enabled 1. Selected 1.` | `camera.capture` Running, capture resumed |
| Not assigned | `Registered 3. Enabled 0. Selected 0.` | nothing started |
| Assigned, no implementation (`sink.cleanliness`) | `Registered 3. Enabled 1. Selected 0.` + warning | warned, container stayed up |

`Registered` is 3 rather than the spec's 1 because three capability
implementations exist (camera, motion sensor, smart plug); `Enabled` and
`Selected` are as specified.

**Test 2 - "assigned + disabled" - cannot currently be produced by Cloud,
and that is a real gap, not a test artefact.** The projector hardcodes
`Enabled: true` for every published assignment, and `AgentCapabilityStatus`
has exactly two values, `Active` and `Removed`. `Removed` is not published
at all, so it produces Test 3's shape. There is no state that publishes an
assignment with `Enabled: false`.

The Agent honours the flag correctly - verified against the real
`CapabilityHost` with a disabled assignment, which selects nothing - so the
consumer is right and the producer cannot express it. `Enabled` is presently
a wire field with one reachable value. Either give `AgentCapability` an
enabled/disabled state distinct from assigned/removed, or drop the flag; the
middle state is what invites someone to trust a switch that is welded on.

**Left undone on purpose:** `AgentCapabilityConfigurationLoader` is a dead
byte-for-byte duplicate of the factory; `ICapabilityRegistry.Get(id)` is
still never called; the manifest's `Commands`/`ProducedEvents`/
`ConsumedEvents`/`Dependencies` are declared but nothing dispatches on them.
No further architecture until the path above is proven live.

---

## ADR-097 - Capability settings: a generic map Cloud never reads

**What this adds.** An `AgentCapability` assignment now carries per-Agent
configuration, so the wire entry goes from `{ CapabilityKey, Enabled }` to
`{ CapabilityKey, Enabled, Settings }`. The same capability can run with
different configuration on different Agents without a new field anywhere.

**This reverses half of ADR-059, which said so explicitly.**
`AgentCapability`'s own comment read: *"No Settings/Enabled the way
DeviceCapability has - nothing about 'can this Agent run X' needs
per-assignment configuration or a separate on/off switch; Status alone
(Active/Removed) covers it."* That was right while an assignment only
answered *may this Agent run X*. It stops being right the moment the
assignment also answers *how*. The Settings half is reversed here; the
**Enabled half still stands** - the only on/off remains `Status`, which is
why 5F-C.6's "assigned + disabled" case is still not expressible.

**Settings are opaque to Cloud, deliberately.** They travel as
`string -> string` from `AgentCapabilityEntity.Settings` (JSON column,
defaulting to `"{}"`, exactly `DeviceCapabilityEntity.Settings`'
convention) all the way to `RuntimeCapabilityAssignment.Settings`. Cloud
never parses a value and never branches on a capability id. The capability
that owns the schema is the only thing that understands
`CaptureIntervalMinutes`, so adding a capability never means touching the
projector, the wire contract, or the Agent's configuration loader.

**Malformed settings fail the publish, not the Agent.** Invalid JSON on an
assignment produces a projection warning, and the publisher refuses to
publish while any warning stands. Verified live by writing
`{"CaptureIntervalMinutes":` into the assignment row: the publish returned
`published: false`, `capabilities: []`, and *"AgentCapability ... has
invalid Settings JSON and won't be published"*. The projector also
`continue`s past the bad assignment, which is belt-and-braces - the
publisher's warning gate is what actually stops it. The alternative,
shipping a broken settings blob, moves the failure to the Agent where the
reason is no longer visible.

**Settings are inside the content hash.** They are part of the wire entry
that already participates, so retuning a capability publishes a new
version. Without that, editing `CaptureIntervalMinutes` would report
"unchanged" and the Agent would keep running the old value with nothing
indicating anything had happened - the ADR-069 no-op guard swallowing a
real change. Covered by `ChangingCapabilitySettingsDefeatsTheNoOpGuard`,
verified to fail when settings are dropped from the wire.

**Proven end to end, 2026-08-22**, config version 8 on the live Capture
Agent:

```json
"Capabilities": [
  { "CapabilityId": "5217f0ef-...", "CapabilityKey": "camera.capture",
    "Name": "Image Capture", "Enabled": true,
    "Settings": { "CaptureIntervalMinutes": "30" } }
]
```

Binding that blob through the real `RuntimeCapabilityAssignmentStore`
yields `camera.capture`, enabled, `CaptureIntervalMinutes = 30`.

**Deliberately not done yet.** `CapabilityHost` is untouched; nothing
consumes the settings. `CameraCapability` and `CameraCaptureWorker` still
ignore `CaptureIntervalMinutes` - transport is proven before behaviour
changes, so the two are never debugged at once. Default merging
(`Capability.DefaultConfiguration` under an assignment override) and any
schema validation engine are also still ahead; `ConfigurationSchema` and
`DefaultConfiguration` exist on `CapabilityEntity` and remain unused by

### 5G.11 - capability consumption: REJECTED for the camera, on purpose

`AgentCapability.Settings` is proven as a generic mechanism. `camera.capture`
consumes nothing from it, because **capture cadence does not belong to the
Agent assignment**.

**What proved it.** A brief implementation resolved
`CaptureIntervalMinutes` into typed options and logged it live on `1.1.6`:

```
Camera capability camera.capture configured with CaptureIntervalMinutes=30.
Device 55cc8aa6-... sleeping for 00:05:00.
```

Two lines, two numbers, two sources. The capability resolved 30 minutes;
the worker slept 5, from `DeviceOptions.Schedule.Interval`. The agent-level
setting was not extending an unconfigured value - it was duplicating a
working per-device one at a coarser grain.

**Where capture cadence actually lives**, and it is the better model:

```
DeviceCapability.Settings.ScheduleIntervalSeconds
        -> ImageCaptureRuntimeProjector
        -> ImageCaptureRuntimeAdapter
        -> DeviceOptions.Schedule.Interval  ->  CameraCaptureWorker
```

Three cameras on one Agent keep three different schedules - 5, 30 and 60
minutes - which an agent-wide interval would have flattened to one. The
`Image Capture` catalogue row already carries the schema and a 900-second
default for exactly this.

**The distinction now locked in:**

| | Answers |
|---|---|
| `AgentCapability` | *What may this Agent execute, and what Agent-level policy applies?* |
| `DeviceCapability` | *How is this capability configured for this device?* |

An upload policy or a storage class is Agent-level. A capture schedule is
per-device. `CaptureIntervalMinutes` was the second wearing the first's
clothes.

**What was removed, and why nothing was kept "just in case":**
`CameraCapabilityOptions` and `CameraCapabilitySettings` are deleted, and
`CameraCapability` no longer takes `IRuntimeCapabilityAssignmentStore`. A
resolver with nothing to resolve is an invitation to find it something to
do. The capability still participates fully in the capability
architecture - it simply consumes no Agent-level setting, which is the
honest state.

**5G outcome:**

| Step | |
|---|---|
| 5G.1-5G.10 generic settings, Cloud to runtime | DONE, proven live |
| 5G.11 camera consumes `CaptureIntervalMinutes` | **REJECTED - wrong configuration owner** |

**Agent-side code has no unit coverage, structurally.** `Vivnest.Tests`
targets `net8.0`, `Vivnest.Agent` targets `net10.0`, so the test project
cannot reference it - no resolver, capability or worker is testable from
the existing suite, which is why all 115 tests are Cloud/Core-side. Closing
this needs a second test project on `net10.0`; multi-targeting was tried
and reverted, so it is not the route.

---

## ADR-098 - Every configuration property has exactly one authoritative owner

**Why now.** 5G.11 caught `CaptureIntervalMinutes` duplicating a working
per-device setting at Agent level before it shipped. The question that
catches the *next* one is not "is this setting reasonable" but "who already
owns this value". This ADR answers it once, for every surface, and states
the rule that keeps the answer true. The register itself lives in
[current-architecture.md](current-architecture.md), "Configuration
ownership register", so it sits beside the rest of the as-built description
rather than in a decision entry nobody re-reads.

**The rule.**

> A capability's configuration belongs to **DeviceCapability** when it can
> differ per device, and to **AgentCapability** when it is one value for
> the whole Agent. The capability catalogue owns schema and defaults, never
> instance values. Host `appsettings.json` owns identity and credentials
> and is never Cloud-writable.

The test to apply before adding any setting: *could two devices on one
Agent legitimately want different values?* If yes, it is DeviceCapability,
and putting it on the Agent flattens a distinction the product needs.

**Findings from the pass.**

1. **`LivenessInterval` and `WarningMultiplier` have two owners.** Both
   `ImageCaptureRuntimeAdapter` and `MotionDetectionRuntimeAdapter` write
   them directly onto the device's runtime root.
   `DeviceConfigRuntimeAdapter` applies adapters in `capabilities[]` array
   order, so the later capability silently wins. The two also disagree on
   units and key name - `LivenessIntervalSeconds` vs
   `LivenessIntervalMinutes` - so the same field means different things
   depending on which capability set it.

   **Latent, not live**: verified against live data, no device currently
   carries both. The only multi-capability device has *Image Capture* and
   *Sink Cleanliness*, which write to their own sub-objects. It becomes
   real the first time one device is both captured from and
   motion-monitored.

   Resolution is a modelling decision, not a patch, and is deliberately
   left open: either liveness moves to the device row (it is a property of
   the device, not of a capability), or one capability is declared its
   owner and the other stops writing it.

2. **The safe pattern already exists and should be the rule.**
   `ObjectDetectionRuntimeAdapter` and `SinkCleanlinessRuntimeAdapter`
   write only into their own named sub-objects, so two capabilities on one
   device cannot collide. New capability adapters follow that pattern; an
   adapter writing a shared root field needs an explicit owner recorded
   here first.

3. **Two `Enabled` flags, both legitimate.** `Device.Enabled` (in service)
   and `DeviceCapability.Enabled` (this capability runs here) are different
   switches, both consumed. `AgentCapability` has neither - only `Status` -
   which is the ADR-096 gap, restated here because the register makes the
   asymmetry obvious.

**Not done in this pass, on purpose.** Schema validation and default
merging (`Capability.DefaultConfiguration` under an assignment override)
come next. Ownership had to be settled first: merging defaults into a value
with two owners would have produced a result that depended on merge order
as well as array order.
