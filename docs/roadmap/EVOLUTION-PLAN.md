# Evolution Plan — MVP → Vivnest Runtime

This is the living plan for how Vivnest gets from where it is today to the
[Vivnest Runtime](../architecture/vivnest-runtime-overview.md), without
stopping feature work to do it. It reconciles the two halves of
[roadmap.md](roadmap.md) — the feature-first sprint plan (Phase 3's Sprint
Plan) against the current architecture, and the runtime-first phase
structure (Phase 1, 4-6) against the target architecture: **both are "the
plan"** — every feature we build should ship real value on the current
codebase *and* be shaped, at low cost, toward the Vivnest Runtime's target
vocabulary.

For what's actually built today, see
[current-architecture.md](../architecture/current-architecture.md). For the
binding rules behind it, see
[decision-log.md](../architecture/decision-log.md) — treat those as
constraints on everything below, not suggestions.

The rule of thumb: don't build generalized infrastructure speculatively.
Extract an abstraction when a second real consumer needs it, not before.
Everything below is sequenced with that in mind.

## Where the current codebase already stands, relative to the Vivnest Runtime

Grounded in the actual code, not the aspiration:

- **Event dispatch already exists.** `ICapabilityHandler<T>` +
  `CapabilityDispatcher` (`Vivnest.Agent/Runtime/Dispatching`) is a working
  event dispatcher — multicast, per-handler error isolation. It's
  functionally close to the target `IEventHandler<T>` / `IEventDispatcher`,
  just named differently.
- **Domain events already exist**: `CameraCaptureCompletedEvent`,
  `CameraCaptureFailedEvent`, `AgentHeartbeatGeneratedEvent`,
  `DeviceHeartbeatGeneratedEvent` are facts published after work completes —
  exactly the "events represent facts" principle.
- **No command concept exists yet.** Workers call services directly
  (`CameraCaptureService.CaptureAsync(...)`) instead of dispatching a
  `CaptureImageCommand`. There's no `ICommandDispatcher` equivalent, and
  nothing needs one yet — nothing currently requires commands to be queued,
  routed, or retried independently of their caller.
- **Workers are already schedulers**, just ad hoc: `CameraCaptureWorker` and
  `DeviceHeartbeatWorker` loop on `Task.Delay`, `AgentHeartbeatWorker` uses
  `PeriodicTimer`. A future `IScheduler` would generalize this, but three
  workers doing their own thing isn't yet a real pain point.
- **The offline-detection data model is already half-built.**
  `DeviceHeartbeat` / `DeviceHeartbeatEntity` already has `NotificationState`,
  `LastOfflineNotificationUtc`, and `LastRecoveredUtc` — fields designed for
  exactly the dedup logic Phase 3's "Offline Detection" capability needs,
  currently unused by anything.
- **Three capability stubs are dead scaffolding, not a head start.**
  `Vivnest.Agent/Capabilities/HomeAssistant.cs`, `OfflineDetection.cs`, and
  `SnapshotScheduler.cs` are empty classes, unwired to DI. They look like
  early attempts at exactly this roadmap, but they're on the wrong side of
  the architecture (see below) — treat them as things to delete, not finish.
- **Queues are one-directional.** Every queue today flows Agent → Cloud.
  There is no Cloud → Agent channel. Any feature implying the cloud tells an
  agent to do something on demand (scheduled snapshot on request, remote
  restart, OTA) needs new infrastructure that doesn't exist yet.
- **Cloud.Functions has exactly one function, queue-triggered.** No HTTP
  surface exists. The REST API (roadmap.md Phase 3 Sprint 4) is net-new
  infrastructure, not an addition to something already there.
- **No test project exists.** Explicitly out of scope for now per prior
  discussion, but worth remembering it'll eventually gate confidently
  refactoring toward the runtime's abstractions.

## Two corrections to the plans as written

1. **`Vivnest.Agent/Capabilities/OfflineDetection.cs` is in the wrong
   place.** An agent can't reliably detect its own outage — if it's down, it
   can't run detection code. Offline/recovery detection has to live
   Cloud-side, reading heartbeat timestamps centrally
   (`HealthMonitorTimerFunction` in Cloud.Functions, per roadmap.md Phase 3
   Sprint 1). Delete the Agent-side stub rather than implement it there.

2. **roadmap.md Phase 3 Sprint 2 ("Scheduled Snapshot") implies a Cloud → Agent
   command channel that doesn't exist.** Its diagram (`Timer → Capture
   Request → CameraCaptureWorker`) reads as cloud-triggered, but building
   that channel is a real infrastructure project, not a two-hour feature.
   Two honest options, pick based on actual product need:
   - **(a) Agent-local scheduling** — add a second, independently
     configurable interval to `DeviceOptions` (e.g. a "snapshot" cadence
     alongside the existing monitoring `ActivityInterval`), no new channel
     needed. Cheapest, ships this sprint.
   - **(b) True on-demand capture from the cloud** (e.g. a dashboard "take a
     photo now" button) — this is the first feature that actually needs a
     command channel, and would be the right, need-driven moment to
     introduce a minimal `ICommand` + Cloud→Agent queue, rather than
     building one speculatively.

   Default to (a) until something concrete demands (b).

## Recommended sequence

1. ~~Stabilize the existing codebase~~ — **done this session**: fixed the
   `CaptureStatusStore` DI duplication, null-deref in
   `CameraCaptureFailedHandler`, swallowed dispatcher exceptions, unguarded
   `DeviceHeartbeatWorker` loop, RTSP credential/argument-injection risk,
   unvalidated blob payload deserialization, `RtspCamera` process handling,
   `DeviceRegistry.GetDevice` error handling, duplicated blob storage code,
   the `Vivnest.Cloud.Functions` missing `TablesOptions`/`DeviceEventOptions`
   binding (the crash you hit), magic strings/numbers, and unused package
   references. This groundwork is what makes it safe to build new features
   on top with confidence.

2. **Delete the dead capability stubs** — `HomeAssistant.cs`,
   `OfflineDetection.cs`, `SnapshotScheduler.cs`. They're empty, unwired,
   and (per above) partly on the wrong side of the architecture. Removing
   them stops them being mistaken for a head start.

3. **Rename `ICapabilityHandler<T>` → `IEventHandler<T>` and
   `ICapabilityDispatcher`/`CapabilityDispatcher` → `IEventDispatcher`/
   `EventDispatcher`.** Purely mechanical, no behavior change — but it means
   the current code already speaks the Vivnest Runtime's vocabulary. When
   `Vivnest.Abstractions` eventually gets extracted for real, this becomes a
   namespace move instead of a redesign. This is the single cheapest "shape
   toward the runtime" step available right now.

4. **Build Phase 3 / Sprint 1 — Device Health Monitoring**, Cloud-side:
   `HealthMonitorTimerFunction`, `IHealthMonitorService`,
   `OfflineDetectionRule`, `RecoveryDetectionRule`, driven off the
   already-existing `NotificationState` / `LastOfflineNotificationUtc` /
   `LastRecoveredUtc` fields. First real capability delivered. Per
   [ADR-005](../architecture/decision-log.md#adr-005--cloud-determines-device-health),
   this is also the point to remove `DeviceHeartbeatWorker`'s unused
   `Status`/`DetermineStatus` plumbing on the agent side rather than filling
   it in — health determination belongs here, not there.

5. **Build the notification model** — `Notification`,
   `INotificationChannel`, `NotificationWorker`, with Telegram as the first
   channel (already exists, just needs to sit behind the new interface).
   This is the first genuinely reusable pattern in the codebase: an
   event fanned out to N independent handlers is precisely the target
   `Event → Dispatcher → 0..N Handlers` shape. Email becomes a pure
   addition later, not a rewrite.

6. **Decide Scheduled Snapshot** per the (a)/(b) fork above — default to (a)
   unless there's a concrete reason for (b).

7. **REST API + Dashboard** (roadmap.md Phase 3 Sprints 4–5) once
   notifications are live and there's real usage to inform what the
   dashboard actually needs to show.

8. **Ongoing, opportunistic:** each time a new capability is added, ask
   "does this want to be pulled out as a formal `ICapability`/`ICommand`
   yet?" Pull the trigger on extracting `Vivnest.Abstractions` /
   `Vivnest.Runtime` as real class libraries only once there are two or more
   concrete consumers that need it — e.g. a second agent type, or dynamic
   capability loading becomes an actual request — not before.

## What stays deferred, and why

Mesh networking, plugin marketplace / dynamic loading, OTA fleet
management, distributed scheduling, Kubernetes/K3s, MQTT, Home Assistant,
ONVIF — all Phase 4+ in [roadmap.md](roadmap.md). These
only pay for themselves once there's more than one agent in production.
Building them now would be infrastructure for a fleet that doesn't exist
yet. Revisit this list when a second physical deployment is real, not
hypothetical.

## Working agreement

- This file is the plan of record for "what's next" — point future
  sessions at it instead of re-explaining context.
- Update it as steps complete or priorities change; it's meant to stay
  current, unlike the architecture docs (which describe the stable target
  and should change rarely).
- [../../CLAUDE.md](../../CLAUDE.md) at the repo root gives any session a
  starting orientation and links here.
