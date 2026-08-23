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
  `IEventHandler<T>` + `EventDispatcher` (`Vivnest.Runtime/Events`)
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
- **A command concept now exists — no longer true as written.** Phase 9
  built it: `ICommandDispatcher`/`CommandDispatcher` (Cloud), persisted
  state in `tblAgentCommands`, the `agent-commands` queue,
  `AgentCommandPollingWorker` (Agent), and `ICommandHandler` with
  `ExecuteCapability`, `RefreshConfiguration` and `ApplyConfiguration`
  implemented. Commands are queued, routed and status-tracked
  independently of their caller.

  What remains true is the *shape* the original note described: workers
  still call services directly for their own scheduled work
  (`CameraCaptureService.CaptureAsync(...)`), and a command does not
  become an in-process `CaptureImageCommand` — `ExecuteCapability`
  resolves a capability and publishes `DeviceTriggeredEvent`, joining the
  existing execution path rather than adding a second one.
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
- **Queues are no longer one-directional — no longer true as written.**
  Most flow Agent → Cloud, but three Cloud → Agent command queues now
  exist: `agent-commands` (ADR-079+), `agent-restart-commands` (ADR-024)
  and `agent-deploy-commands` (ADR-028, consumed by
  `Vivnest.Agent.Updater`, never by the Agent itself). On-demand capture,
  remote restart and deploy are all built and live. Note the known
  multi-agent hazard recorded in current-architecture.md: each worker
  deletes a message before checking whether it was addressed to it, so two
  Agents polling one queue can consume each other's messages.
- **Cloud.Functions started with exactly one function, queue-triggered —
  no longer true.** It now has several queue-triggered functions, two
  Timer-triggered functions (health monitoring sweep, retention), and a
  full tenant-scoped HTTP REST API (`/devices`, `/agents`, `/apikeys`,
  `/whoami` — see roadmap.md Phase 3 Sprint 4, step 6 below). This bullet
  is kept as a reminder that the REST API was net-new infrastructure when
  built, not an addition to something already there — not as a
  description of the current state.
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

**~~Open fork~~ CLOSED — roadmap.md Phase 3 Sprint 2 ("Scheduled
Snapshot") implied a Cloud → Agent command channel that did not exist.**
Both options below were eventually taken: (a) shipped as the device's own
`Schedule.Interval`, and (b) shipped in Phase 9 as the real command
channel. A dashboard "Capture now" button is live and proven end to end —
see current-architecture.md's "Identity spaces: the complete map". The
fork is recorded rather than deleted because the reasoning still applies
to the *next* feature that looks like it needs a channel: build it when
something concrete demands it, not speculatively.

The original text follows.

Building that channel is a real infrastructure project, not a two-hour
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
   `CaptureStatusStore` (renamed `DeviceRuntimeStateStore`, ADR-092) DI duplication, null-deref in
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
   Azure Function App. **The environment has since moved** — this originally
   targeted `vivnestcloudprod` in `rg-vivnest-dev`; today's deployment target
   is `vivnestcloud2` in `rg-vivnest-2` (ADR-094). The names below are left
   as the historical record of the first deploy: `vivnestcloudprod`,
   resource group `rg-vivnest-dev`, New Zealand North — `HealthMonitorTimerFunction` and the queue
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

10. ~~**Home Assistant integration — built for real this time, not reverted.**~~ **Done** — built, verified against real hardware, and still in the tree.
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

    **Sprint 6's original motion-detection goal was not done through this
    HA path — update below, item 13, covers how it actually got built.**
    At the time this item was written, no `MotionDetectedEvent`/
    `MotionCaptureHandler` existed and none of this was exercised against
    a motion sensor, since none was on hand. What was true then still
    stands: the generic HA bridge Sprint 6 needed as its foundation is
    built and proven, so wiring up a motion sensor is entity-config plus a
    small handler, not a WebSocket client from scratch — it just turned
    out the sensor that got acquired went native instead (item 13).

    **Also settled: a device reachable more than one way (the HS110, both
    directly via Kasa and via HA) keeps a single `DeviceId`** — connection
    method is a data source feeding the same device's event timeline, not a
    separate device. Checked, not assumed, that this doesn't collide
    (`DeviceEvent` has no single-writer assumption; the one store that
    could collide, `ICaptureStatusStore` (now `IDeviceRuntimeStateStore`, ADR-092), is never touched by the HA path)
    before unifying two initially-separate `DeviceId`s back into one. See
    ADR-016 for the full reasoning, including why `Devices[]` and
    `HomeAssistant:Entities` stay two separate config arrays rather than
    one merged schema — one device needing dual-path today doesn't meet the
    "second real consumer" bar for that generalization.

    **Update — three real bugs found and fixed after this item was
    originally written, all in the same ADR-016:** (1) both native and
    HA paths ended up actively covering the HS110 at once, never a
    deliberate design — resolved with a symmetric `Enabled` flag on both
    sides so exactly one path is active at a time, config-only, no
    redeploy; (2) HA-sourced devices had no liveness mechanism of their
    own and silently froze at a stale status — fixed with
    `IHomeAssistantLivenessTracker`, treating any mapped-entity
    `state_changed` as reachability evidence and HA's own
    `state == "unavailable"` as offline; (3) the agent's own WebSocket
    connection to HA going down (not the device itself) was invisible to
    Cloud — closed with `IHomeAssistantConnectionTracker` surfacing
    `AgentHeartbeat.HomeAssistantLastConnectedUtc`, and a second
    `DeviceStatusResolver` cascade (`HomeAssistantCascade`) that reports
    `Unknown` with notifications suppressed once that connection goes
    stale, mirroring the existing agent-offline cascade one level down.
    See [decision-log.md](../architecture/decision-log.md) ADR-016 for
    the full root-cause writeups — each was found live, not by inspection.

11. ~~**Capture gallery pagination**~~ — **done**: the dashboard's capture
    timeline now loads day-by-day, one page of captures at a time within
    an expanded day, instead of fetching the whole 30-day window (or a
    whole day) up front — a `?days=N` summary endpoint (per-day counts,
    no SAS URLs) renders every collapsed day header cheaply, and
    expanding a day pages its captures 50 at a time. See
    [decision-log.md](../architecture/decision-log.md) ADR-017.

12. ~~**Dashboard visual redesign**~~ — **done**: hand-rolled CSS
    custom-property design tokens instead of a UI framework dependency,
    status-accented row cards instead of tables (reflow at narrow widths
    rather than scroll), and a new real `AgentDetail` page (previously
    agents had no drill-down). See
    [decision-log.md](../architecture/decision-log.md) ADR-018.

13. ~~**Motion detection, built natively**~~ — **done**: against a Tapo
    H100 hub + T100 sensor, over Tapo's KLAP v2 protocol
    (`TapoKlapClient`), mirroring the `SmartPlug` shape file-for-file
    (`IMotionSensor`/`MotionSensorMonitorService`/`MotionSensorMonitorWorker`)
    rather than bridging through Home Assistant — a third proof point for
    ADR-007/ADR-015's "second device type doesn't reuse `ICamera`, but the
    persistence/eventing layers absorb it unchanged" prediction. This is
    what actually closed Sprint 6's motion-detection goal (item 10 above),
    just not through the HA bridge that item was originally building
    toward. See [decision-log.md](../architecture/decision-log.md)
    ADR-019.

14. ~~**Agent CPU/Memory/Bandwidth metrics, and a real `FirmwareVersion`**~~
    — **done**: `AgentEvent`/`AgentEventEntity` mirrors `DeviceEvent`'s
    shape one level up; `AgentMetricsWorker` is a genuinely separate
    `BackgroundService` (own `PeriodicTimer`, own try/catch) specifically
    so a metrics-sampling failure can never block the liveness heartbeat —
    the first attempt put this on `AgentHeartbeatWorker`'s own tick and
    was correctly rejected for exactly that coupling risk before it
    shipped. Separately, `FirmwareVersion` stopped being a hand-typed,
    untrustworthy config string and now comes from the real git commit
    SHA baked into the Docker image at build time
    (`docker build --build-arg BUILD_VERSION=$(git rev-parse --short HEAD)`),
    needing zero C# changes since the env-var-config pipeline already
    existed. See [decision-log.md](../architecture/decision-log.md)
    ADR-020.

15. ~~**Motion-triggered capture**~~ — **done**: asked "how do other
    vendors solve this" before designing (consumer platforms hardcode a
    one-hop link; HA/Hubitat build a full rules engine) and deliberately
    built the first tier, not the second, for one motion sensor and one
    camera. `DeviceOptions.Trigger.DeviceIds` (plain config) +
    `MotionTriggerResolverHandler` (a *second* handler on the existing
    `MotionSensorStateChangedEvent`, multicast dispatch already supports
    this) + a generic `DeviceTriggeredEvent` (deliberately not
    capture-specific, so a future `TurnOnPlugOnTriggerHandler` is just
    another handler file). `CameraCaptureExecutor` finally got extracted
    from `CameraCaptureWorker` — this created its second real caller,
    exactly the trigger condition roadmap.md's Shared section anticipated.
    Burst cadence (30s capture interval for 10 min, then revert) is a
    self-expiring state machine on `DeviceRuntimeState`
    (`BurstUntilUtc`/`BurstInterval`), woken immediately via a
    `SemaphoreSlim` signal rather than waiting up to a full
    `LivenessInterval` to notice. This also closes roadmap.md Phase 4's
    "Shared — wiring a motion source into an actual capture" section,
    built more generically than that section originally sketched. See
    [decision-log.md](../architecture/decision-log.md) ADR-021.

16. ~~**Operational alerting (LLM log triage) — designed, not started.**~~
    **Built 2026-08-20 (v1, no LLM) and verified end to end against real
    Azure — see [decision-log.md](../architecture/decision-log.md) ADR-093
    and step 19 below.** The rate-limiting question that blocked it is
    resolved as a per-(agent, signature) cooldown plus a per-agent hourly
    ceiling. Still off unless `OperationalAlert__Enabled` is set, and the
    final delivery hop is unproven while `Telegram__Enabled` is false.
    Original design follows.
    (roadmap.md Phase 5, new "Sprint 8 — Operational Alerting" section —
    read that for the full design, this is the pointer.) Builds on top of
    the log-shipping feature
    ([decision-log.md](../architecture/decision-log.md) ADR-027,
    `LogShippingWorker`):
    an Error-level log call becomes a real `AgentEvent`
    (`AgentEventTypes.ErrorLogged`), published to a new `agent-events`
    queue (mirrors `device-events`' `{PartitionKey, RowKey}`-only shape,
    ADR-004), consumed Cloud-side by a new queue-triggered function that
    calls a new `ILlmService` to triage the raw message, then reuses the
    existing `NotificationDispatcher` — no new notification channel.
    Blocked on deciding a rate-limiting/cooldown strategy before it's
    built, not on anything technical — see roadmap.md's Sprint 8 for why.

17. ~~**Deploy — the "Deploy a specific container version" button ADR-024
    named and deliberately deferred**~~ — **done**: a "Deploy latest"
    button on the Agent Detail page, publishing to a new
    `agent-deploy-commands` queue. The real work was the host-side half —
    Deploy needs Docker access the Agent container is deliberately
    refused (ADR-020), so it's consumed by a new, separate standalone
    process, `Vivnest.Agent.Updater` (a sixth project, deployed alongside
    the Agent on the host, never inside its container), not by
    `Vivnest.Agent` itself. Watchtower was considered directly before
    building this and deferred again, for the same "more than one
    agent/host" reason this log already named once (ADR-020's
    `FirmwareVersion` follow-up) — seeing that reasoning confirmed a
    second time rather than just re-asserted. v1 scope deliberately cut:
    always deploys `:latest`, no version picker; the queue message carries
    no deploy-time config yet. See
    [decision-log.md](../architecture/decision-log.md) ADR-028 for the
    full design, including the Watchtower comparison and why Deploy
    reuses Restart's tenant-scoped gating despite ADR-024 flagging that as
    worth revisiting once built.

18. ~~**Command & Control (Phase 9) — Admin can actively affect running
    Agents, not just observe them.**~~ **Done, all five passes.** `Admin →
    Command → Agent → Handler/Capability → Event`, per-command persisted
    state in `tblAgentCommands` (mirrors `AgentInstallationEntity`'s
    persist-then-orchestrate split). `RestartAgent` rewired through the
    new `ICommandDispatcher`; two genuinely new command types built —
    `RefreshConfiguration`/`ApplyConfiguration` (download-then-restart-
    to-adopt, not live hot-reload) and `ExecuteCapability(ImageCapture)`
    (reuses the motion-triggered-capture path verbatim). Completion is
    confirmed via the next heartbeat, never self-reported, since a
    restarting process dies before it could report its own success. A
    Pass 4 reliability sweep closed a real gap (neither Agent-side
    polling worker checked whether Cloud still considered a fetched
    command live before acting on it — found by review, then confirmed
    against real Azure data: offline-dispatch → real 5-minute expiry →
    stale queue message correctly discarded on restart). Pass 5 put
    Refresh/Apply/Capture Now buttons and a Command History panel on
    both `AgentDetail` and `DeviceDetail` — build/lint clean, but not
    browser-verified against live data (no API key was available that
    session). See [decision-log.md](../architecture/decision-log.md)
    ADR-079 through ADR-083.

19. ~~**Hardening pass — dead code, duplication, secrets, and the
    configuration blob layout.**~~ **Done.** Not a feature; this is the
    "stabilize before building on top" step from item 1, repeated once the
    codebase had grown enough to need it again. Driven by a full dead-code
    and duplication audit. That file has since been retired: what it found
    is either done, recorded in the decision log, listed under "Known
    cleanup backlog" below, or - for the things deliberately *kept* and the
    findings that did not survive scrutiny - moved into
    [current-architecture.md](../architecture/current-architecture.md).

    - **`Vivnest.Tests` exists**, seeded from real defects: the shared
      primitives, the configuration publish pipeline, and API auth driven
      through the real Function class. This supersedes the "no automated
      tests" line that stood in README.md and CLAUDE.md until now.
    - **The two runtime-configuration publishers were de-duplicated** into
      `RuntimeConfigurationWriter<TEntity>` — they had been running the
      same ~150-line versioning/retry algorithm twice, which the source
      comments openly admitted ("mirrored here").
    - **A live defect fell out of that**: the content hash was computed
      over *encrypted* settings, and AES-GCM draws a fresh nonce per call,
      so identical admin data hashed differently every time. The ADR-069
      no-op guard therefore never fired for any device with a credential —
      i.e. every real camera. Fixed by hashing plaintext, then encrypting.
    - **Configuration blobs are tenant/site scoped** (ADR-091). The Agent
      used to enumerate and download the *entire* container and discard
      what it did not own, pulling other tenants' device names, locations
      and RTSP URLs across the wire on every startup. Both layouts are
      written and read during the transition; ADR-091 records exactly what
      to delete once every Agent is on a scoped-reading build.
    - **A second defect fell out of running that for real**: the no-op
      guard also blocked the layout *migration*, so any entity whose
      content had not changed never acquired scoped blobs. Only found by
      executing the migration against real storage and looking at the
      result — the test suite could not have found it, because the bug was
      in what the tests did not think to assert.
    - **Agent command callbacks can now authenticate** (agent-scoped API
      keys, grace mode, `AgentAuth:RequireApiKey`). **The flag is off in
      deployed config by choice** — the mechanism is built and tested, so
      the endpoints stay open until it is flipped.
    - **Agent images are semver-tagged** from `1.0.0` (ADR-073), making
      `AgentInstallation.ImageVersion` meaningful instead of permanently
      `NeverDeployed`.
    - **ADR-092**: device runtime state moved out of the `Camera`
      namespace, per ADR-007 and CLAUDE.md's own rule that "camera" must
      not read as an architectural boundary.

    **Still open from this pass, waiting on a decision rather than on
    work:** the decrypt-vs-mask question for credentials at rest (T3 in the
    dead-code report). The other item that sat here - when to give up the
    legacy blob layout - was closed on 2026-08-21: it is gone, code and
    blobs, see ADR-091.

## Known cleanup backlog

Carried over from `VIVNEST-DEAD-LEGACY-CODE.md`, a one-off dead-code and
duplication audit that has now been retired (its findings are either done,
recorded as decisions in the decision log, or listed here). The file is in
git history if the original per-finding detail is ever wanted.

None of these block anything. They are listed so the next person does not
have to rediscover them.

1. **~~Remove the legacy unscoped blob layout~~ DONE 2026-08-21** (was
   L1/L3). The layout is gone entirely - dual-write, all six read
   fallbacks, the backfill, the unscoped name overloads, and the blobs
   themselves. Orphaned version blobs were copied into the scoped layout
   first, so rollback range is unchanged. See ADR-091.

   **The other half of L1/L3 remains**: `DeviceConfigRuntimeAdapter` still
   carries a legacy-shape branch. That is document *shape* (pre-ADR-064
   flat `DeviceOptions` vs the `capabilities[]` document), not blob
   location - a separate item that the dead-code report happened to list
   alongside this one. It stays until every device document in play is
   known to be the new shape.

2. **`AgentsFunction.DeployAgent` bypasses `ICommandDispatcher`** (was L7).
   Blocked rather than pending: routing it through the dispatcher means
   deploy commands become tracked `tblAgentCommands` rows like every other
   command since ADR-079, which needs Updater-side plumbing that does not
   exist. Not a tidy-up; a real piece of work.

3. **Capability names are hard-coded in two assemblies** (was U-D3). The
   fuzzy-match rule itself was unified into `RuntimeNameMatch`, but the
   eight `CapabilityName` literals - four projectors in Cloud, four
   adapters in Core - remain independent. Adding a capability still means
   two matching edits with nothing enforcing agreement. Unifying them needs
   a shared capability registry, which is the same design question as item
   5 below, not a de-duplication.

4. **`BaseIdentity` vs `ISiteScoped`** (was U-D7). Two mechanisms carrying
   the same TenantId/SiteId/AgentId triple: an abstract base with
   `required init` for the four telemetry models, an interface for the 13
   aggregates. Confidence that this is worth merging was only ever MEDIUM.

5. **`AgentCapability` / `tblAgentCapabilities` has no runtime consumer**
   (was T1). Full admin CRUD, three routes, a dashboard screen - and
   nothing reads it. Neither projector; neither `Program.cs` branch.
   Assigning a capability to an Agent changes nothing about what that Agent
   does; the runtime equivalent is still the hard-coded
   `if (agentType == Low/High)` blocks. This arrived *before* its
   replacement rather than after it: removing it would discard the data
   model a Capability Host is meant to consume. Resolve it by building that
   consumer, not by deleting the model.

6. **Two behavioural asymmetries in event writing** (was U-D8). The
   `DeviceEvents:Enabled` / `AgentEvents:Enabled` flags are respected by the
   Agent's writers but not by Cloud's direct write sites, and the two sides
   differ on Add vs Upsert. Deliberately left alone at the time; both are
   small and both are real.

7. **Credentials are plaintext at rest in `tblDeviceRegistry.Settings`**
   (was T3). Deferred pending a product decision, not blocked on anything
   technical - see `current-architecture.md`'s gaps section for the
   decrypt-on-read vs mask-on-read trade-off. `CredentialCipher` already
   does everything the fix needs.

## Command Routing 1.x — frozen (2026-08-23)

**This is frozen architecture, not an active thread.** The identity model
below is proven live end to end; further change to it needs a new
decision, not incremental cleanup. No routing refactoring until the Phase
9 study period has reviewed the resulting system as a whole.

The model, in one place — full detail in
[current-architecture.md](../architecture/current-architecture.md)'s
"Identity spaces: the complete map":

```
Catalogue GUID          persistent identity   tblAgentCommands, tblDeviceCapabilities
CapabilityKey           runtime identity      Agent wire, CapabilityRegistry, Manifest.Id
RuntimeDeviceId         runtime identity  ->  registry DeviceId via GetByRuntimeDeviceIdAsync
RuntimeAgentId          runtime identity  ->  registry AgentId  via GetByRuntimeAgentIdAsync
```

Execution boundary unchanged, and deliberately so: command -> validation ->
`CapabilityRegistry` -> `DeviceTriggeredEvent` -> the existing execution
path. **No command-specific executor was introduced.**

**Why this checkpoint is trustworthy.** It was not only unit-tested: 180
tests pass, the dashboard was deployed and Capture Now verified through
the deployed UI, a catalogue-GUID command was dispatched and completed
live, the runtime->registry device translation was exercised on real data,
the Agent received `camera.capture`, `DeviceTriggeredEvent` remained the
execution boundary, an actual photograph was captured, and both retired
identities (`"ImageCapture"` and `"Image Capture"`) were rejected live
with `CAPABILITY_NOT_FOUND` without ever leaving Cloud. V1 deployment
profiles were removed and the V2 path documented.

### Parked work from this thread

| Priority | Work | Decision |
|---|---|---|
| — | 1.10: rename wire `capabilityId` -> `capabilityKey` | **Won't do** |
| High | `CapabilityType` / `Source` vocabulary unification | Park |
| High | Runtime <-> registry device lifecycle | Park |
| High | Capability fault isolation / recovery | Park |
| Medium | Per-device worker fault isolation | Park |
| Medium | Command-triggered burst semantics | Park |
| Medium | Projection-time validation / default semantics | Park |
| Low | `AgentCapability` enabled/disabled model | Park |

**Added by architecture review, 2026-08-23 — study before implementing:**

| Priority | Work | Decision |
|---|---|---|
| **High** | Projector/adapter binding on mutable `CapabilityName` | Study |
| High | Shared command queues, before multi-agent | Constraint |
| Medium | Cloud integration test coverage | Park |

**Projector binding is now the weakest identity boundary in the system**,
and more compelling than anything 1.10 would have fixed. Command routing
binds on ids; configuration publishing still binds on a display name a
user can edit:

```
CapabilityRuntimeProjectorLookup.Find(projectors, capability.CapabilityName)
```

Rename "Image Capture" to "Camera Capture" in the admin UI and
`CapabilityId`, `CapabilityKey` and every `DeviceCapability` row stay
valid while the projector stops matching. It degrades to a warning plus
exclusion from the published document - the device keeps running its last
config, so the symptom is configuration that quietly stops updating.

The obvious fix, binding on `CapabilityKey`, is **not sufficient alone**:
`Capability.Update` assigns `Key` whenever a non-blank one is passed, so
the key is mutable too. A real fix is both halves - bind on
`CapabilityKey`, and make it immutable after creation. Study first: making
an existing field immutable needs to establish that nothing legitimately
rewrites it today.

**Shared command queues are a constraint, not a bug.** `agent-commands`,
`agent-restart-commands` and `agent-deploy-commands` are each a single
queue every Agent polls, and each worker deletes a message *before*
checking whether `envelope.AgentId` matches its own. With one Agent it
cannot fire. With two, one Agent can consume and discard a command
addressed to the other, which then never arrives. Whatever fixes it -
per-agent queues, peek-then-claim, or a real broker - must land **before**
multi-agent execution, not after it.

**Cloud integration coverage is the next testing gap.** There is one
test project again - `Vivnest.Tests`, 191 tests - since every project
moved to net8.0 on 2026-08-24 and `Vivnest.Agent.Tests` (which existed
only to work around the TFM mismatch) folded back into it.
`CommandDispatcherIdentityTests` drives the real dispatcher through real
validation and identity translation. Most other Cloud services are still
verified by reading code plus a manual live pass - acceptable now, and the
natural next maturity step.

**1.10 is closed, not forgotten.** The Agent property is already
semantically `CapabilityKey`; the existing JSON name `capabilityId` is
retained for wire compatibility. Renaming it provides no functional
benefit and introduces an unnecessary contract migration. The identity
distinction is now documented and enforced through the Cloud-to-Agent
translation boundary. Recorded here so it is not reopened as if it were
unfinished work.

The three "High" items are one cluster in practice: they all become real
with a second camera or a second agent, and all three are decisions about
ownership rather than refactors. The fault-isolation entry covers both
halves left open by ADR-095 (a capability that *throws* from `StartAsync`
still stops the Agent) and ADR-103 (a worker that dies after startup goes
`Failed` and stays there - no retry, no backoff).


## Phase 10 — Distributed / Multi-Agent Execution (parked)

**Defined 2026-08-23. Deliberately not started.** The step where Vivnest
goes from *"an Agent can execute capabilities"* to *"Vivnest coordinates a
fleet of Agents that can execute capabilities"* — the platform decides
which Agent should run a capability, rather than the caller naming one.

```
              Cloud / API
                   |
            capability request
                   |
           Execution / Routing Layer
                   |
      +------------+------------+
      |            |            |
   Agent 1      Agent 2      Agent 3
   Camera        LoRa          AI
```

Scope as defined:

1. **Capability discovery** — which Agents currently provide a capability;
   what the fleet offers in total.
2. **Execution routing** — given `camera.capture`, choose an Agent by
   capability availability rather than a hard-coded target.
3. **Agent availability** — online? capability actually `Running`?
   healthy?
4. **Capability compatibility** — can this Agent run this capability, at
   this version, with the required device/config prerequisites present?
5. **Failover** — Agent A unavailable, Agent B supports it, route to B.
6. **Workload distribution** — when several Agents qualify, choose on
   load, locality, priority.
7. **Distributed command execution** — a command targets a *capability*;
   the platform finds the execution target.

### This overlaps Phase 6 in roadmap.md, and not only by name

`roadmap.md`'s **Phase 6B — Distributed Execution** already lists command
routing, load balancing, failover, work migration, leader election and
distributed scheduling; **Phase 6A** lists agent discovery and capability
advertisement. Between them they cover most of the seven items above.

The overlap is not just duplication — the two describe **different
architectures for the same problem**:

| | Phase 6B (older framing) | Phase 10 (current framing) |
|---|---|---|
| Topology | agents discover each other, peer mesh | Cloud-side routing layer decides |
| Failover | leader election / health quorum among peers | routing layer picks another Agent |
| Dispatch | network-transparent `IEventDispatcher` between processes | existing Cloud → Agent command path, retargeted |

Phase 10 is the cheaper of the two by a wide margin: it reuses the command
channel that already exists and works (ADR-079+, ADR-102/104/105) and adds
a decision step in Cloud, where Phase 6B needs peer discovery and
network-transparent in-process dispatch — which roadmap.md itself calls
"a materially bigger step".

**Unreconciled on purpose.** Whether Phase 10 supersedes 6B, or 6B remains
a later intra-site refinement on top of it, is a real architectural
decision and has not been made. Do not treat either as settled; two parked
plans for one problem is how ADR-091 ended up meaning two things.

### Why it is parked

The single-Agent foundations Phase 10 depends on were only just
established, and finishing Phase 9 surfaced questions that are still open
*within one Agent*: runtime vs registry identity, capability configuration
ownership, device registration lifecycle, worker failure semantics, queue
isolation, capability health, configuration projection.

Routing work across a fleet before those are settled would distribute the
ambiguities rather than resolve them. Two of the parked items above are
outright prerequisites:

- **Shared command queues.** Each command queue is one queue every Agent
  polls, and each worker deletes a message before checking whether it was
  addressed to it. Harmless with one Agent; a correctness bug the moment
  a second one polls the same queue — which Phase 10 guarantees.
- **Capability health granularity.** Routing on "is the capability
  `Running`" needs that answer to be trustworthy, and today a capability
  reports `Failed` as a unit even when some of its per-device loops are
  still working (see per-device fault isolation, above).

Sequence: finish Phase 9 → study and test the resulting system → close the
gaps it exposed → *then* design Phase 10 from that understanding.


## Near-duplicate scan, 2026-08-24

Ran after the architecture migration. Method: strip comments and string
literals, tokenise, compare files by Jaccard similarity of overlapping
5-token windows. 418 files of 40+ tokens, 24 candidate pairs at >= 34%.
Exact-duplicate hashing found nothing; this finds the same logic written
twice in slightly different words, which hashing cannot.

**The strongest signal is not any single pair.** Object Detection and Sink
Cleanliness are duplicated at *four* layers:

| Layer | Pair | Similarity |
|---|---|---|
| Core adapter | `ObjectDetectionRuntimeAdapter` / `SinkCleanlinessRuntimeAdapter` | **89%** |
| Cloud projector | `ObjectDetectionRuntimeProjector` / `SinkCleanlinessRuntimeProjector` | 72% |
| Options | `ObjectDetectionRoiOptions` / `SinkCleanlinessRoiOptions` | 60% |
| Options | `ObjectDetectionOptions` / `SinkCleanlinessOptions` | 52% |

The two adapters are 307 tokens each and differ in **five lines**: the
class name, `CapabilityName`, and three strings inside log/JSON keys. The
ROI parsing, validation and integer-coercion helper are identical.

These are not two capabilities that happen to look alike. They are one
concept - *a capability that classifies a region of interest in a camera
frame* - implemented twice in parallel. A third ROI capability would
currently mean a fourth copy at each of the four layers.

**The capability lifecycle is triplicated, and this codebase did it to
itself.** `CameraCapability`, `MotionSensorCapability` and
`SmartPlugCapability` are 59-68% similar at ~430 tokens each. The
differences are the worker type, the manifest values, the `DeviceType`
filter and one log phrase; the ~78 lines of lifecycle - Starting, the
zero-device precondition, `StartAsync`, `CapabilityWorkerSupervisor.Observe`,
Running, Stopping, Stopped - are identical in all three.

Worth naming how that happened: ADR-103 extracted
`CapabilityWorkerSupervisor` specifically so the three capabilities could
not drift on worker supervision, then copied the surrounding lifecycle
into all three by hand. The extraction was real and the duplication was
created in the same change. Every future capability inherits both.

A base class carrying the lifecycle, with the manifest, worker and device
type as the derived parts, collapses three copies into one - and is worth
more before Phase 10 than after, since capability count is exactly what
Phase 10 increases.

**Parallel by design, not worth touching:** `AgentConfigBlob` /
`DeviceConfigBlob` (80%, 79 tokens of constants), the MotionSensor and
SmartPlug event handlers (35-36% - same handler shape, different
payloads), the Domain entities (`Site` / `Tenant` /
`DeviceTypeDefinition`, ~37% CRUD scaffolding), and the two throwaway
testers under `tools/` (50%).

**Checked and dismissed:** `IBlobStorageService` (Cloud, read-only) vs
`IBlobStorageClient` (Core, full client) at 39% is interface shape, not
shared logic - current-architecture.md already established the surfaces
are disjoint, and the scan agrees. `AzureTableAgentEventReader` vs
`AzureTableDeviceEventReader` at 38% is one 473-token file against a
932-token one; the shared part is query scaffolding.

Nothing here is fixed. The ROI collapse and the capability base class are
both refactors with real behavioural surface - log wording, manifest
values - and both deserve their own change with tests, not a tail-end
commit on a migration.


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

**Per-device fault isolation within a capability** — a capability whose
worker runs one loop per device fails as a unit. `CameraCaptureWorker`
does `Task.WhenAll` over a loop per camera, so if one camera's loop
faults, `WhenAll` faults, ADR-103's observer marks `camera.capture`
`Failed` — and the sibling loops keep running, detached, capturing
normally. The status is then wrong in both directions at once: it claims
total failure while two thirds of the work continues.

Invisible today, because the one live Agent has one camera, so
"the camera died" and "the capability died" are the same event. It
becomes real with the second camera on one Agent.

Deliberately not fixed alongside ADR-103. That fix was about a worker
dying *unobserved*; this is about what the right granularity of
observation is, and answering it properly means deciding whether a
capability can be partially healthy — a new status, or per-device state
the capability reports upward — plus per-device restart, backoff and
cleanup. That is the same recovery design ADR-103 deferred, and it should
be designed once rather than grown accidentally. Trigger: a second device
on a single capability, or the recovery phase, whichever comes first.

**AgentInstallation as the deploy source of truth** — `AgentInstallation`'s
`ContainerId`/`ImageName`/`ImageVersion` fields (ADR-053) are typed-in
descriptive metadata today; `InstallAsync`/`MoveAsync` are "purely
declarative" and never touch the real deploy pipeline. That pipeline
(`AgentsFunction.DeployAgent` → `agent-deploy-commands` queue →
`Vivnest.Agent.Updater`, ADR-028) doesn't know Machine/AgentInstallation
exist at all — it always redeploys whatever image tag the Updater is
configured to pull as "latest" for that agent. The natural next step is
having the deploy trigger read `ImageName`/`ImageVersion` off the agent's
*active* `AgentInstallation` instead, making it a real desired-state
record rather than descriptive-only. Trigger: the first real need to
deploy something other than "latest" per agent — until then this is
speculative plumbing with no behavior difference, so it stays deferred
per this file's own "second real consumer" rule.

## Working agreement

- This file is the plan of record for "what's next" — point future
  sessions at it instead of re-explaining context.
- Update it as steps complete or priorities change; it's meant to stay
  current, unlike the architecture docs (which describe the stable target
  and should change rarely).
- [../../CLAUDE.md](../../CLAUDE.md) at the repo root gives any session a
  starting orientation and links here.
- **Don't infer ownership or identity from a single read model.** Trace
  the value through storage, projection, wire transport, runtime
  resolution and live execution before concluding anything about it.

  This is the most transferable thing the Kitchen Camera investigation
  produced. Four confident readings were overturned in sequence - "the
  blob and the table disagree", "the capability is built-in so the
  validator is wrong", "the two identity spaces have zero overlap", "the
  agent's key is rejected on its own route" - and each one pointed at a
  plausible fix that would have made things worse: write a row that should
  not exist, teach the validator to skip a check, reconcile two stores
  that were never the same source.

  Two specific habits came out of it. **Query tables globally, not for the
  row you are asking about** - the assignment existed all along, under a
  different device id, and a per-device query showed "empty" exactly as a
  missing row would. **An empty result and a lookup asked with the wrong
  key are indistinguishable from the caller**, so treat "found nothing" as
  a question rather than an answer.
