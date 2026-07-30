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

## ADR-002 — Capability handlers own persistence

`ICapabilityHandler<TEvent>` implementations
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

## ADR-005 — Cloud determines device health

Health/online/offline status is computed cloud-side, from heartbeat
timestamps, not reported by the agent.

*Implication for the codebase:* `DeviceHeartbeatWorker` currently has a
commented-out `DetermineStatus` method, meaning `DeviceHeartbeat.Status` is
never set agent-side. Earlier in this project's history that looked like
unfinished dead code to fix. Under this ADR, it's arguably dead code that
**should stay dead** — agent-side status computation isn't just incomplete,
it's the wrong layer for it to live in. The correct place for this logic is
the Cloud-side `HealthMonitorTimerFunction` /
`OfflineDetectionRule` / `RecoveryDetectionRule` described in
[../roadmap/roadmap.md](../roadmap/roadmap.md) (Phase 3, Sprint 1), reading
`LastHeartbeatUtc` centrally. When that lands, consider removing the unused
`Status` field/plumbing from the agent side entirely rather than filling it in.

## ADR-006 — Runtime state is transient and separated from persistence

See [current-architecture.md](current-architecture.md)'s "Runtime State"
section — `DeviceRuntimeState` holds only in-memory, rebuildable fields
(`LastCaptureUtc`, `LastFailureUtc`, `LastActivityUtc`, `LastHeartbeatUtc`,
`LastBlobName`, `LastError`). Nothing durable or business-relevant is
allowed to live only in runtime state.
