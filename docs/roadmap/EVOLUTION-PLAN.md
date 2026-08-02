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
  locally — Phase 4 timing, no change needed now. (Update: Phase 4 Sprint 6
  has since been built for real — see item 10 below — but as
  `HomeAssistantWorker`/`HomeAssistantStateChangedHandler`/
  `HomeAssistantCommandSender` directly, not by filling in this stub; no
  other capability uses `ICapability` yet either, so there was nothing to
  conform to. `HomeAssistant.cs` itself remains an empty, unused
  placeholder.) `OfflineDetection.cs` is
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
   **done this session, both sprints, and grew well past their original
   scope once real usage revealed real gaps** (see roadmap.md Sprint 4/5
   and [decision-log.md](../architecture/decision-log.md) ADR-012 for the
   full list): the REST API grew from 4 endpoints to 11 (agents, capture
   date-range timeline, `/whoami`, full API-key create/list/revoke
   lifecycle with a `DevicesOnly` permission tier); the dashboard grew an
   Agents tab (hidden entirely for `DevicesOnly` keys), a day-grouped
   capture timeline extracted into its own `CaptureGallery` component
   gated by device type, and permission-aware UI driven by `/whoami`.
   Verified against live Azure resources throughout (real Table
   Storage/Blob Storage, real captures, real heartbeats), not just clean
   builds — including a real TimeSpan-serialization bug found and fixed
   this way (ADR-011) that a build alone would never have caught.

7. ~~**Deploy.**~~ **Done.** `Vivnest.Cloud.Functions` deployed to a real
   Azure Function App (`vivnestcloudprod`, resource group `rg-vivnest-dev`,
   New Zealand North) — `HealthMonitorTimerFunction` and the queue
   triggers now run continuously regardless of whether this dev machine is
   on. `Vivnest.Dashboard` deployed to Azure Static Web Apps
   (`vivnest-dashboard`, East Asia — the closest supported Static Web Apps
   region to the Function App's; Static Web Apps isn't offered in New
   Zealand North), built with `VITE_API_BASE_URL` pointed at the deployed
   Function App and pushed via the SWA CLI's token-based `swa deploy`
   (no GitHub Actions wired up yet — deliberately deferred; every future
   dashboard change needs a manual rebuild + `swa deploy` until/unless
   that's worth automating). Production CORS added on the Function App
   resource for the Static Web App's origin (`local.settings.json`'s
   `Host.CORS` only ever covered local dev). Verified end-to-end in a
   real browser against the live deployment, not just a clean build:
   logged in with a freshly minted prod API key
   (`POST /apikeys` against the deployed Function App's host key) and
   confirmed real device data renders.
   Not containerized — considered and declined, see ADR-014: Azure
   Functions Consumption plan (the right fit for this traffic level)
   doesn't support custom containers at all, and containerizing would have
   forced a paid Premium/Container Apps plan plus new image/registry
   tooling for no benefit at this scale.

8. **Motion-triggered capture — tried, blocked on an upstream bug, code
   reverted; currently parked.** (roadmap.md Phase 4, Sprints 6–7, and its
   "What actually happened" / "What was built" sections — read those for
   the full detail, this is the summary.) The gap: nothing in this
   codebase can detect motion at all — `ICamera` is only
   `CaptureAsync`/`IsReachableAsync`. Sequencing went through three
   revisions: planned real Home Assistant first (broadest long-term
   reach); flipped to native ONVIF once real hardware (a Tapo C120)
   entered the picture, since it only needs Agent code talking to
   hardware already in hand; **then a third, simpler option got tried
   instead of either** — a direct Agent → camera integration via the
   `pytapo` Python library (what HA's own Tapo integration is built on),
   run as a subprocess the Agent supervises, deliberately chosen over
   installing full HA for this narrow a need. Neither Sprint 6 (real HA)
   nor Sprint 7 (ONVIF) as originally specified were built as Agent
   capabilities — ONVIF was spiked (not implemented) and found non-viable.
   Real HA *was* later actually stood up (a throwaway
   `ghcr.io/home-assistant/home-assistant:stable` container, once Docker
   was set up for the Agent containerization work below) and tested
   directly against the camera via its own `tplink` integration — same
   `SSLV3_ALERT_HANDSHAKE_FAILURE` the bare `python-kasa` CLI test hit.
   So this is now a fully closed question, not an inference: real HA is
   confirmed not viable for this camera on this firmware, same as ONVIF
   and the pytapo-direct sidecar.

   The pytapo-direct pipeline **was fully built and verified to compile**
   — `CameraCaptureExecutor`, `MotionDetectedEvent` +
   `MotionCaptureHandler`, `TapoMotionWorker`, and a Python sidecar script
   — then tested against the real camera and blocked by the identical
   `NotAuthorized`/`Invalid authentication data` failure ONVIF hit: a
   known, dated, currently-unresolved TP-Link firmware bug (confirmed via
   multiple community reports on the same firmware line) that breaks local
   API auth for ONVIF and pytapo alike. Not fixable from this codebase.
   **Checked, not assumed, whether real HA (Sprint 6) would do any
   better** — it has two realistic paths to a Tapo camera: the community
   `HomeAssistant-Tapo-Control` integration (pytapo-based, same bug), and
   HA's official core `TP-Link Smart Home` integration, which uses a
   different, separately-maintained library, `python-kasa`. Tested
   `python-kasa` directly against the camera rather than assume — it fails
   too, but at a lower layer than pytapo (a raw TLS handshake failure,
   `SSLV3_ALERT_HANDSHAKE_FAILURE`, before any credential is even sent),
   tried with explicit camera type, KLAP encryption, and login-version
   flags. Both of HA's real paths are independently confirmed blocked, not
   inferred. The code was reverted afterward rather than left in the tree
   unusable — `git status` shows none of it today. Two ways forward,
   neither started: wait for TP-Link/pytapo/python-kasa to fix the
   handshake, or get a dedicated non-Tapo motion sensor (e.g. a Zigbee
   PIR) and actually build Sprint 6 (real HA) against that instead.

9. **Ongoing, opportunistic:** each time a new capability is added, ask
   "does this want to be pulled out as a formal `ICapability`/`ICommand`
   yet?" Pull the trigger on extracting `Vivnest.Abstractions` /
   `Vivnest.Runtime` as real class libraries only once there are two or more
   concrete consumers that need it — e.g. a second agent type, or dynamic
   capability loading becomes an actual request — not before.

10. **Home Assistant integration — built for real this time, not reverted.**
    (`roadmap.md` Phase 4 Sprint 6,
    [decision-log.md](../architecture/decision-log.md) ADR-016 — read those
    for the full build/verification writeup, this is the summary.)
    Different device, different outcome from item 8 above: that attempt
    targeted the Tapo C120 and hit a firmware-level auth bug that blocked
    every path (ONVIF, pytapo, and real HA's own `tplink` integration
    alike). This build targets the HS110 smart plug instead, via HA's
    `python-kasa`-based `tplink` integration — unaffected by the Tapo bug —
    and stays in the tree. Built and verified against a real HA instance
    (Docker) and the real device: `HomeAssistantWorker` (inbound WebSocket
    subscription to `state_changed`), `HomeAssistantStateChangedHandler`
    (persists + queues through the existing, unchanged Cloud pipeline — and
    gets a free Telegram notification via the `PowerStateChanged` consumer
    item 8's SmartPlug work already wired up), and
    `IHomeAssistantCommandSender` (outbound REST control, manually verified
    flipping the real relay). A WebSocket-staleness bug surfaced during
    verification (idle connections silently stopped receiving server
    pushes — a Docker Desktop/WSL2 port-forwarding quirk, isolated with a
    raw Python probe to confirm it was client-side, not HA-side) and was
    fixed with HA's own ping/pong keepalive plus a receive timeout — see
    ADR-016 for the full diagnosis.

    **Sprint 6's original motion-detection goal is still not done** — no
    `MotionDetectedEvent`/`MotionCaptureHandler` exists, and none of this
    was exercised against a motion sensor, since no Zigbee/PIR sensor is on
    hand. What's now true, though: the generic HA bridge Sprint 6 needed as
    its foundation is built and proven, so wiring up a motion sensor once
    one is acquired is entity-config plus a small handler, not a WebSocket
    client from scratch.

    **Also settled: a device reachable more than one way (the HS110, both
    directly via Kasa and via HA) keeps a single `DeviceId`** — connection
    method is a data source feeding the same device's event timeline, not a
    separate device. Checked, not assumed, that this doesn't collide
    (`DeviceEvent` has no single-writer assumption; the one store that
    could collide, `ICaptureStatusStore`, is never touched by the HA path)
    before unifying two initially-separate `DeviceId`s back into one. See
    ADR-016 for the full reasoning, including why `Devices[]` and
    `HomeAssistant:Entities` stay two separate config arrays rather than
    one merged schema — one device needing dual-path today doesn't meet the
    "second real consumer" bar for that generalization.

## What stays deferred, and why

Mesh networking, plugin marketplace / dynamic loading, OTA fleet
management, distributed scheduling, Kubernetes/K3s, MQTT — all Phase 4+ in
[roadmap.md](roadmap.md). These only pay for themselves once there's more
than one agent in production. Building them now would be infrastructure
for a fleet that doesn't exist yet. Revisit this list when a second
physical deployment is real, not hypothetical.

Home Assistant and ONVIF are the exception to that reasoning — see steps 8
and 10 above: they're not fleet infrastructure, they're the
motion-detection path for the one agent that already exists, so they don't
wait on a second deployment the way the rest of this list does. Home
Assistant specifically has since grown beyond just that one purpose — it's
now also a general device-integration bridge already delivering real value
(smart plug monitoring and control) independent of whether motion detection
ever gets built on top of it.

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
