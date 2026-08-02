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

Known remaining gap, not yet built: this closes the "stale status frozen
forever" bug, but there's still no signal for "the agent's WebSocket
connection to HA itself is down but HA is otherwise fine" — `plug-001`'s
apparent status would stay whatever it last was during an extended
reconnect loop, the same class of problem `AgentHeartbeat` staleness
solves for native devices, just not yet built for the HA connection
itself. Deferred as a distinct, smaller gap from the one just fixed.

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
