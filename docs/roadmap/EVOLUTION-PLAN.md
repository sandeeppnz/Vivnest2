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

- **Event dispatch already exists, and now speaks the target vocabulary.**
  `IEventHandler<T>` + `EventDispatcher` (`Vivnest.Agent/Runtime/Dispatching`)
  is a working event dispatcher — multicast, per-handler error isolation.
  Renamed from `ICapabilityHandler<T>` / `CapabilityDispatcher` (step 2,
  done this session) specifically to stop squatting on the word
  "Capability" for what's really just the low-level per-event reaction
  mechanism — see [decision-log.md](../architecture/decision-log.md)
  ADR-002 and [vivnest-runtime-overview.md](../architecture/vivnest-runtime-overview.md)
  for the Capability-vs-EventHandler distinction this protects.
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
- **Three capability stubs are empty but intentional — not dead code.**
  `Vivnest.Agent/Capabilities/HomeAssistant.cs`, `OfflineDetection.cs`, and
  `SnapshotScheduler.cs` are empty classes, unwired to DI, but each is a
  real, correctly-placed design (confirmed directly, not assumed):
  `HomeAssistant.cs` is agent-side because Home Assistant is meant to run
  as its own container on the same Raspberry Pi as the agent, connected
  locally — Phase 4 timing, no change needed now. `OfflineDetection.cs` is
  agent-side because it evaluates a *device's* local status and emits a
  change-triggered `DeviceHeartbeat` instead of a periodic one — see
  [decision-log.md](../architecture/decision-log.md) ADR-005 (revised) —
  and it's directly part of Sprint 1, not later. `SnapshotScheduler.cs`'s
  purpose isn't fully defined yet; leave it as a placeholder, don't delete
  it and don't guess at its design prematurely.
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

## Corrections and open forks in the plan as written

**Correction — `Vivnest.Agent/Capabilities/OfflineDetection.cs` is *not* in
the wrong place.** An earlier version of this section argued the agent
can't detect its own outage, so offline detection must be entirely
Cloud-side, and recommended deleting the agent-side stub. That conflated
"the agent's own liveness" (genuinely can't self-detect) with "a specific
device's status" (the agent *can* observe this firsthand — see
[decision-log.md](../architecture/decision-log.md) ADR-005, revised).
`OfflineDetection.cs` stays agent-side and is real Sprint 1 work: it
evaluates per-device status changes locally and emits a change-triggered
`DeviceHeartbeat`. The Cloud-side `HealthMonitorTimerFunction` /
`OfflineDetectionRule` / `RecoveryDetectionRule` still exist and still make
the final online/offline call — they're a different component with a
similar name, not a replacement for the agent-side one. Don't conflate the
two when implementing Sprint 1.

**Open fork — roadmap.md Phase 3 Sprint 2 ("Scheduled Snapshot") implies a
Cloud → Agent command channel that doesn't exist.** Its diagram (`Timer →
Capture Request → CameraCaptureWorker`) reads as cloud-triggered, but
building that channel is a real infrastructure project, not a two-hour
feature. Two honest options, pick based on actual product need:

- **(a) Agent-local scheduling** — add a second, independently configurable
  interval to `DeviceOptions` (e.g. a "snapshot" cadence alongside the
  existing monitoring `LivenessInterval`), no new channel needed. Cheapest,
  ships this sprint. `SnapshotScheduler.cs` may end up being the
  implementation vehicle for this — but its purpose isn't fully decided
  yet, so don't assume this is exactly what it becomes.
- **(b) True on-demand capture from the cloud** (e.g. a dashboard "take a
  photo now" button) — this is the first feature that actually needs a
  command channel, and would be the right, need-driven moment to introduce
  a minimal `ICommand` + Cloud→Agent queue, rather than building one
  speculatively.

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

2. ~~Rename `ICapabilityHandler<T>` → `IEventHandler<T>` and
   `ICapabilityDispatcher`/`CapabilityDispatcher` → `IEventDispatcher`/
   `EventDispatcher`~~ — **done this session**: purely mechanical, no
   behavior change, verified with a clean full-solution build (0 warnings,
   0 errors). The current code now speaks the Vivnest Runtime's vocabulary,
   so when `Vivnest.Abstractions` eventually gets extracted for real, this
   becomes a namespace move instead of a redesign. The three capability
   stubs (`HomeAssistant.cs`, `OfflineDetection.cs`, `SnapshotScheduler.cs`)
   were deliberately left untouched here — see the corrections above for
   why.

3. ~~**Build Phase 3 / Sprint 1 — Device Health Monitoring.**~~ — **done
   this session, both halves**, per
   [ADR-005](../architecture/decision-log.md#adr-005--cloud-determines-final-device-health-the-agent-reports-device-level-changes-it-can-see-firsthand)
   (revised):
   - **Agent-side**: `IOfflineDetection`/`OfflineDetection`
     (`Vivnest.Agent/Capabilities/OfflineDetection.cs`) evaluates each
     device's status from `LastError`/`LastCaptureUtc`;
     `DeviceRuntimeState.LastReportedStatus` tracks the last value sent;
     `DeviceHeartbeatWorker` now only publishes a `DeviceHeartbeat` (with
     `Status` set) when that status actually changes.
   - **Cloud-side**: `HealthMonitorTimerFunction` (new Timer-triggered
     function in `Vivnest.Cloud.Functions`) sweeps all device/agent
     heartbeats on a cron schedule; `IHealthMonitorService` combines
     `AgentHeartbeat` recency with the last reported device status;
     `OfflineDetectionRule`/`RecoveryDetectionRule` decide when to notify,
     gated by `NotificationState` to avoid duplicates; `ITelegramService`
     gained `SendMessageAsync` for text alerts. Required building two
     read types that didn't exist Cloud-side before
     (`IDeviceHeartbeatReader`/`IAgentHeartbeatReader`, since
     `Vivnest.Cloud` doesn't reference `Vivnest.Infrastructure`) — named
     `Reader` rather than `Repository` (their original name) once that
     turned out to collide with the Agent-side `...Store` types being
     renamed in parallel; see [decision-log.md](../architecture/decision-log.md).
     Also fixed a real bug found along the way:
     `AgentHeartbeatMapping.ToModel()` was setting `AgentId` from
     `entity.PartitionKey` (`"{TenantId}|{SiteId}"`) instead of
     `entity.AgentId`.
   - At the time, deliberately **not** built: the generic
     `Notification`/`INotificationChannel` model. `HealthMonitorService`
     called `ITelegramService` directly instead. **Superseded by step 4**,
     below, once that abstraction had a real reason to exist.

   First real capability delivered, end to end. Verified with a clean
   full-solution build (0 warnings, 0 errors) — not yet verified against a
   live Table Storage/Telegram account.

4. ~~**Build the notification model**~~ — **done this session**:
   `Notification`/`NotificationPriority`/`NotificationTypes`,
   `INotificationChannel` + `TelegramNotificationChannel` (first channel),
   `INotificationDispatcher` + `NotificationDispatcher` (all in
   `Vivnest.Cloud`/`Vivnest.Cloud.Notifications`) — with Telegram as the
   first channel, sitting behind the new interface. Named "Dispatcher"
   rather than "Worker": "Worker" already means `BackgroundService` on the
   Agent side, and this lives Cloud-side where nothing runs a long-lived
   loop. `NotificationDispatcher` deliberately mirrors `EventDispatcher`'s
   existing multicast / per-channel error isolation shape — the first
   genuinely reusable pattern in the codebase, an event/notification fanned
   out to N independent handlers, precisely the target `Dispatcher → 0..N
   Handlers` shape. `HealthMonitorService` and `CameraCapturedHandler`
   (retrofitted immediately after, same session) both now call
   `INotificationDispatcher` — `ITelegramService` is purely the low-level
   Telegram API client now, used only by `TelegramNotificationChannel`.
   Email becomes a pure addition later, not a rewrite.

5. ~~**Decide Scheduled Snapshot**~~ — **done this session**: went with (a),
   agent-local scheduling — but landed somewhere more precise than "add a
   scheduler." Tracing the existing capture pipeline found
   `CameraCaptureWorker` already ran a "scheduled snapshot" loop on
   `LivenessInterval`, but that one interval was silently overloading three
   unrelated concerns onto one unconditional pipeline: liveness, snapshot
   capture, and Telegram notification. See
   [decision-log.md](../architecture/decision-log.md) ADR-010 for the full
   reasoning; summary of the split:
   - **Liveness** (Agent, `LivenessInterval`) — new `ICamera.IsReachableAsync()`
     (raw TCP connect, no ffmpeg) updates `DeviceRuntimeState.LastActivityUtc`;
     `OfflineDetection.Evaluate` reads that instead of `LastCaptureUtc`, so
     liveness accuracy no longer depends on how often a full snapshot happens.
   - **Snapshot capture** (Agent, `DeviceOptions.SnapshotInterval`) —
     `CameraCaptureWorker` only does the real ffmpeg capture + blob upload +
     `DeviceEvent` persist when `SnapshotInterval` has elapsed; otherwise it
     just probes. Zero/unset preserves prior behavior (capture every
     `LivenessInterval` tick). `CameraCaptureHandler` forwards every capture
     unconditionally — no agent-side notification gating.
   - **Telegram notification** (Cloud, `SnapshotNotificationOptions.MinInterval`) —
     `CameraCapturedHandler` gained a `NotificationState`-style dedup gate
     (new `IDeviceSnapshotStateReader` / `tblDeviceSnapshotState`), so
     notification cadence is a Cloud config change, not an agent redeploy.
     Stays purely event-driven (gates the newly-arrived capture, never
     re-sends an old one), so there's no duplicate-photo risk regardless of
     how the two intervals relate.

6. ~~**REST API + Dashboard**~~ (roadmap.md Phase 3 Sprints 4–5) —
   **done this session, both sprints**: Sprint 4 (REST API) —
   `GET /devices`, `GET /devices/{id}`, `GET /devices/{id}/events`,
   `GET /devices/{id}/captures`, plus `POST /apikeys` for tenant-scoped
   auth (see roadmap.md Sprint 4 for the two gaps found while building it,
   not scoped upfront). Sprint 5 (Dashboard) — `Vivnest.Dashboard`
   (React/Vite), consuming the REST API only; required adding SAS-URL
   image serving to the API along the way (see roadmap.md Sprint 5).
   Neither sprint has been run end-to-end against live Azure resources yet
   — verified with clean builds and a local dev-server smoke test only.

7. **Ongoing, opportunistic:** each time a new capability is added, ask
   "does this want to be pulled out as a formal `ICapability`/`ICommand`
   yet?" Pull the trigger on extracting `Vivnest.Abstractions` /
   `Vivnest.Runtime` as real class libraries only once there are two or more
   concrete consumers that need it — e.g. a second agent type, or dynamic
   capability loading becomes an actual request — not before.

## What stays deferred, and why

Mesh networking, plugin marketplace / dynamic loading, OTA fleet
management, distributed scheduling, Kubernetes/K3s, MQTT, Home Assistant,
ONVIF — all Phase 4+ in [roadmap.md](roadmap.md). These only pay for
themselves once there's more than one agent in production. Building them
now would be infrastructure for a fleet that doesn't exist yet. Revisit
this list when a second physical deployment is real, not hypothetical.

This now spans three distinct "distributed" targets, worth not conflating
(see [decision-log.md](../architecture/decision-log.md) ADR-007/008 and
roadmap.md's Phase 6 split):

1. **Multiple device types** (camera, sensors, meters) — a capability
   abstraction question. Trigger: building the second device type.
2. **Multiple independent customer sites** (commercial/multi-tenant) — a
   cloud-side data-isolation question. Already partly addressed today
   (`TenantId`/`SiteId` exist); the remaining work is enforcing it in the
   REST API once that's built.
3. **Multiple cooperating agent processes within one site** (containerized
   Camera/Storage/AI/Heartbeat agents on separate Raspberry Pis, meshed,
   with failover and load sharing) — the biggest lift of the three. Needs
   network-transparent command/event dispatch, which today's in-process
   `EventDispatcher` doesn't provide. Deferred furthest out, but real —
   don't design near-term capabilities in a way that quietly assumes
   same-process dispatch is permanent.

## Working agreement

- This file is the plan of record for "what's next" — point future
  sessions at it instead of re-explaining context.
- Update it as steps complete or priorities change; it's meant to stay
  current, unlike the architecture docs (which describe the stable target
  and should change rarely).
- [../../CLAUDE.md](../../CLAUDE.md) at the repo root gives any session a
  starting orientation and links here.
