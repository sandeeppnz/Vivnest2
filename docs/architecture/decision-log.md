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
`IDeviceEventStore`, `IAgentHeartbeatStore`, or `IDeviceHeartbeatStore`.

*Verified:* confirmed — e.g. `CameraCaptureHandler.HandleAsync` is the only
caller of `IDeviceEventStore.SaveAsync` in the capture path.

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
(from `LastError` / `LastCaptureUtc`); it needs to be **revived and
reshaped** to detect a *change* from the previously reported status (not
just recompute current status every tick) and to drive conditional
sending, not deleted.

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
