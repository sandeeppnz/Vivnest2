# Vivnest Roadmap

**The Vivnest Runtime** is the reusable edge runtime. **Vivnest** is the
product built on top of it through capabilities. See
[../architecture/vivnest-runtime-overview.md](../architecture/vivnest-runtime-overview.md)
for the target architecture, and [EVOLUTION-PLAN.md](EVOLUTION-PLAN.md) for
the concrete near-term sequencing against the current codebase.

> Merged from two originally separate documents (`product-roadmap.md`, the
> high-level phase structure, and `mvp-next-steps.md`, the sprint-level
> detail for what's next) so there's one roadmap instead of two that could
> drift apart. No content was dropped — the sprint detail now lives nested
> under the phase it implements.

## Current Status

Completed (current ad-hoc architecture, not yet re-platformed onto the
Vivnest Runtime kernel — see [Phase 1](#phase-1--runtime-foundation)):

- Runtime architecture (workers, capability dispatcher, capability handlers)
- Camera capture pipeline
- Heartbeat pipeline
- Azure Table Storage persistence
- Azure Queue publishing
- Azure Functions integration

The focus now shifts from infrastructure to product features — Phase 3 below.

## Phase 1 — Runtime Foundation

**Status:** informally complete for the current architecture; the *formal*
Vivnest Runtime kernel described here has not been built — see the note
under [Overall Roadmap](#overall-roadmap).

**Objective:** Build and freeze a stable runtime that all future
capabilities can plug into.

Components:

- Kernel — Hosting, Dependency Injection, Scheduler, Runtime Context, Lifecycle
- Message Bus — Command Dispatcher, Event Dispatcher, Internal Work Queue (`Channel<T>`)
- Capability Host
- Runtime State
- Configuration
- Diagnostics
- Telemetry

**Deliverable:** A stable runtime with no business logic.

## Phase 2 — Core Capabilities

**Status:** Complete.

**Objective:** Provide the essential capabilities required for a functioning edge agent.

Capabilities: Camera, Agent Heartbeat, Device Heartbeat, Cloud Sync.

**Deliverable:** Reliable device monitoring, capture, heartbeats, and cloud synchronization.

## Phase 3 — User Capabilities

**Status:** Next.

**Objective:** Deliver the first production-ready Vivnest experience.

Capabilities: Telegram Notifications, Email Notifications, Offline
Detection, Snapshot Scheduler, Dashboard API.

**Deliverable:** A usable edge monitoring platform with notifications and scheduling.

### Sprint Plan

#### Sprint 1 — Device Health Monitoring

Two halves, agent-side and cloud-side — don't conflate them despite the
similar naming (see
[decision-log.md](../architecture/decision-log.md) ADR-005):

**Agent-side — event-driven `DeviceHeartbeat`.** Today `DeviceHeartbeatWorker`
sends an unconditional heartbeat every tick. Instead, it should evaluate
each device's status locally (it already tracks `LastError` /
`LastCaptureUtc` per device) and only publish a `DeviceHeartbeat` when that
device's status actually changes — reviving and reshaping the
currently-commented-out `DetermineStatus` method as the change evaluator.
This is the `Vivnest.Agent/Capabilities/OfflineDetection.cs` capability.
`AgentHeartbeat` stays periodic and unconditional — it's the one signal
that proves the agent process itself is alive.

**Cloud-side — final determination, via two triggers, not one.** The cloud
makes the authoritative online/offline call by combining `AgentHeartbeat`
recency (is the agent alive at all?) with the last reported device status.
If `AgentHeartbeat` goes stale, every device on that agent must be treated
as unknown/possibly-offline regardless of its last reported status, since
silence could mean "nothing changed" or "the agent died" — only the
`AgentHeartbeat` check tells those apart.

That "detecting an absence" requirement is *why there are two triggers*,
not one — a mistake corrected mid-session (the first version only had the
Timer, quietly ignoring the queue message the agent was already
publishing on every status change):

- **Timer (`HealthMonitorTimerFunction`)** — the only way to catch an
  agent that's gone completely silent; nothing arrives to trigger on when
  the agent itself is dead, so this has to be a periodic sweep of every
  device/agent. Doubles as a reconciliation safety net for anything the
  queue path below might have missed (dropped message, function downtime).
- **Queue (`DeviceHeartbeatChangedFunction`)** — reacts within seconds to
  the `DeviceHeartbeatQueueMessage` the agent already publishes whenever a
  device's status changes (per ADR-004, `{PartitionKey, RowKey}` only —
  refetches the one entity). This queue existed and was being published to
  since before Sprint 1 started; nothing cloud-side consumed it until now.

Both triggers call the same `IHealthMonitorService` logic
(`EvaluateAndNotifyAsync`) so the determination and notification rules
exist in exactly one place, not two.

```text
Agent: device status change detected
    ↓
DeviceHeartbeat (event-driven)
    ↓
tblDeviceHeartbeat + DeviceHeartbeatQueue
    ↓                              ↓
Cloud: Timer sweep          Cloud: Queue trigger
(all devices, catches            (this device,
 agent silence)                   seconds not minutes)
    ↓                              ↓
    └────────── same logic ───────┘
                  ↓
    OfflineDetectionRule / RecoveryDetectionRule
                  ↓
              Telegram
```

Implementation order:

1. ~~Agent-side: revive `DetermineStatus` as a change evaluator; wire it
   into `OfflineDetection.cs`; make `DeviceHeartbeatWorker`
   event-driven~~ — **done**.
2. ~~`HealthMonitorTimerFunction` (Timer Trigger, Cloud-side)~~ — **done**:
   runs on a configurable cron schedule (`HealthMonitorCronSchedule`,
   deliberately **not** double-underscored — see gotcha below), sweeping
   all `DeviceHeartbeat` and `AgentHeartbeat` rows each tick. Paired with
   `DeviceHeartbeatChangedFunction` (queue-triggered on `device-heartbeats`,
   added after the review below) for near-instant reaction to a single
   device's status change — see the two-trigger note above.

   **Gotcha, took real debugging to isolate:** the trigger attribute
   originally read `[TimerTrigger("%HealthMonitor__CronSchedule%")]`, which
   failed indexing with "`%HealthMonitor__CronSchedule%` does not resolve to
   a value" — despite the setting being present, correctly typed, and (once
   confirmed by forcing it in as a real OS environment variable) genuinely
   visible to Core Tools. Root cause: this is the *only* `%...%` app-setting
   placeholder anywhere in the codebase — every other trigger uses a
   hardcoded literal (`"camera-captured"`, etc.) — so nothing surfaced the
   real problem until now. .NET's environment-variable configuration
   provider auto-converts `__` into a `:` hierarchy separator when it loads
   env vars into `IConfiguration`. The WebJobs host's `%...%` resolver looks
   up the *literal* string between the percent signs against that same
   `IConfiguration` — so `%HealthMonitor__CronSchedule%` was searching for a
   key that, by the time it reached `IConfiguration`, no longer existed
   under that literal name; only `HealthMonitor:CronSchedule` did. Every
   *other* `HealthMonitor__*` setting is fine because they're only ever
   read via `IOptions<HealthMonitorOptions>` binding
   (`GetSection("HealthMonitor")`), which expects and handles that same
   `__`→`:` conversion — the placeholder-resolution path is the one place
   in the app that doesn't. Fixed by giving this one setting a flat name
   (`HealthMonitorCronSchedule`, no separator) since it's never bound via
   `IOptions` anyway — nothing else reads it.
3. ~~`IHealthMonitorService`~~ — **done**: combines `AgentHeartbeat`
   recency (`HeartbeatInterval * AgentStaleMultiplier`, default 3x) with
   each device's last reported status to determine the final status.
4. ~~`OfflineDetectionRule` (Cloud-side)~~ — **done**: notifies on
   Offline/Error status, gated by `NotificationState != OfflineNotified`.
5. ~~`RecoveryDetectionRule`~~ — **done**: notifies on Online status, gated
   by `NotificationState == OfflineNotified`.
6. ~~`TelegramNotificationService`~~ — **not built as a separate class**:
   `HealthMonitorService` originally called `ITelegramService`'s
   `SendMessageAsync` directly. **Since superseded** — see Sprint 3 below,
   which pulled this forward and retrofitted `HealthMonitorService` onto
   the real notification model once it existed.
7. ~~Notification state persistence~~ — **done**:
   `IDeviceHeartbeatReader.UpdateNotificationStateAsync` writes
   `NotificationState`/`LastOfflineNotificationUtc`/`LastRecoveredUtc` back
   after a successful send (not before — a failed Telegram call retries
   next tick instead of silently marking itself "notified").
8. Integration tests (blocked on a test project existing — see [EVOLUTION-PLAN.md](EVOLUTION-PLAN.md))

Outcome: automatic offline alerts, automatic recovery alerts, no duplicate
notifications, and materially less heartbeat traffic than today's
unconditional periodic `DeviceHeartbeat`.

New Cloud-side types, for reference: `IDeviceHeartbeatReader` /
`IAgentHeartbeatReader` (+ Azure Table implementations) read the two
heartbeat tables — neither existed cloud-side before this, since
`Vivnest.Cloud` doesn't reference `Vivnest.Infrastructure`. (Originally
named `...Repository`, renamed to `...Reader` — see
[decision-log.md](../architecture/decision-log.md) for why: Agent-side
already had `...Store` types with the *same* simple names as intended,
which would have collided with identically-named-but-differently-behaved
types across the Agent/Cloud boundary. `Writer`/`Reader` names the actual
behavioral split instead of hiding it behind a shared, ambiguous word.)
`ITelegramService` gained `SendMessageAsync` for text-only alerts (it
previously only supported photo messages).

#### Sprint 2 — Scheduled Snapshot

```text
Timer
    ↓
Capture Request
    ↓
CameraCaptureWorker
    ↓
CameraCapturedEvent
    ↓
Telegram
```

See [EVOLUTION-PLAN.md](EVOLUTION-PLAN.md) for the agent-local-scheduling
vs. cloud-triggered-capture fork this implied — resolved as agent-local.

**Status: done.** Turned out to need three independent cadences, not one —
see [decision-log.md](../architecture/decision-log.md) ADR-010:
`LivenessInterval` (Agent) now drives a lightweight liveness probe
(`ICamera.IsReachableAsync()`, no ffmpeg), `DeviceOptions.SnapshotInterval`
(Agent) drives how often a real frame gets captured and stored, and
`SnapshotNotificationOptions.MinInterval` (Cloud) drives how often a
captured snapshot actually reaches Telegram — changeable without an agent
redeploy. Zero/unset on either interval preserves the pipeline's prior
unconditional behavior.

#### Sprint 3 — Notification Pipeline

```text
Notification Processor
        ↓
Notification Channel
        ↓
Telegram
```

Future channels: Email, WhatsApp, Push Notifications, Signal.

**Design decision:** don't build a `DailyEmailWorker`-shaped feature per
channel. Build one dispatcher that constructs a channel-agnostic
`Notification` (type, title, message, images, priority, occurred-at), then
fans it out to every configured `INotificationChannel` (`SendAsync`). Adding
Discord, Slack, Signal, Teams later means implementing one more channel —
nothing else changes.

**Status: pulled forward and done**, ahead of Sprint 2, once
`HealthMonitorService` (Sprint 1) needed somewhere to send alerts:
`Notification`/`NotificationPriority`/`NotificationTypes`
(`Vivnest.Cloud/Notifications`), `INotificationChannel` +
`TelegramNotificationChannel` (first and only channel so far),
`INotificationDispatcher` + `NotificationDispatcher` — the last one named
"Dispatcher" rather than "Worker" deliberately, since "Worker" already
means `BackgroundService` on the Agent side and Cloud.Functions has no
long-running loops; `NotificationDispatcher` mirrors `EventDispatcher`'s
existing multicast / per-channel error isolation pattern instead.
`HealthMonitorService` and `CameraCapturedHandler` were both retrofitted
to call `INotificationDispatcher` instead of `ITelegramService` directly
— `ITelegramService` is now purely the low-level Telegram API client,
used only by `TelegramNotificationChannel`. There is currently no
producer left that talks to Telegram directly.

Notification types worth building next: Daily Summary, Scheduled
Snapshot, Daily Album, Motion Alert (future) — `DeviceOffline`,
`DeviceRecovered`, and `CameraCaptured` already exist (`NotificationTypes`).

#### Sprint 4 — REST API

**Status: done.**

- `GET /devices`
- `GET /devices/{id}`
- `GET /devices/{id}/events`
- `GET /devices/{id}/captures`

Read-only, reading directly from Azure Table Storage — no separate database
or cache layer. Required two additions not in the original list, discovered
while implementing rather than guessed upfront:

- `IDeviceHeartbeatReader`/`IDeviceEventReader` gained `GetByTenantAsync`/
  `GetByDeviceAsync` — the existing Readers only supported single-row
  lookups and one unscoped `GetAllAsync`, built for their original internal
  callers (health monitoring's full sweep, single-event handlers), not a
  tenant-scoped listing API.
- `POST /apikeys` — per ADR-008, every endpoint had to be tenant-scoped
  from day one, which meant deciding how a request identifies its tenant
  before any of the four read endpoints could be built. Went with API-key
  → tenant lookup (`tblApiKeys`, `IApiKeyStore`, `IApiKeyAuthenticator`),
  read endpoints gated by that tenant key, key creation itself gated by a
  *different*, higher-privilege credential (Azure Functions'
  `AuthorizationLevel.Function` host key) so a caller holding one tenant's
  read key can't mint keys for other tenants.

`Vivnest.Cloud.Functions` previously had zero HTTP triggers — this was
genuinely new infrastructure, not an addition to something already there.

#### Sprint 5 — Dashboard

**Status: MVP done.** `Vivnest.Dashboard` (React + Vite + TypeScript, no
UI framework dependency yet) — API-key entry gate (stored in
`localStorage`, re-prompts on 401), device list (status/type/last
heartbeat), device detail (health, last activity, latest image, recent
events). Consumes the REST API only, never Table Storage directly, per the
original plan.

- Device health — done (status badge, error message if present)
- Last heartbeat — done
- Last capture / Latest image — done, via SAS URL (see below)
- Recent events — done (`GET /devices/{id}/events`)

**New backend capability this required:** the REST API only ever returned
event *metadata* — `BlobName`/`BlobContainer` inside the JSON payload, not
image bytes (Table Storage never held binary data; images live in Blob
Storage). Resolved with SAS URLs: `AzureBlobStorageClient.GenerateReadSasUri`
(`Vivnest.Core.Storage`) issues a 15-minute read-only SAS for a given blob;
`IBlobStorageService` exposes it Cloud-side; `DeviceQueryService` populates
`DeviceEventDto.ImageUrl` only for `/captures` responses (not general
`/events`, which don't need it) by parsing the capture payload's
`BlobContainer`/`BlobName` and generating the URL inline — no extra
network round-trip, SAS generation is a local signing operation.

Not yet done: no routing library (device list ↔ detail is local component
state, fine for two views); no polling/auto-refresh (manual reload only);
no Static Web Apps deployment config — the dashboard runs via `npm run dev`
locally against `VITE_API_BASE_URL` (see `.env.example`), pointing at
`func start`'s local Functions host.

**Added after the initial MVP, not in the original Sprint 5 list:** an
Agents tab, showing agent-level liveness (host, status, started/last
heartbeat) separately from device-level health — the same
`AgentHeartbeat`-vs-`DeviceHeartbeat` distinction ADR-005 draws backend-side,
now visible in the dashboard too. Required a new `GET /agents` /
`GET /agents/{id}` pair and `IAgentHeartbeatReader.GetByTenantAsync` (same
gap `IDeviceHeartbeatReader` had before Sprint 4 — `GetAllAsync` was
unscoped, built only for `HealthMonitorService`'s internal full sweep).
`AgentQueryService` derives an Online/Offline status by mirroring
`HealthMonitorService.DetermineFinalStatus`'s own staleness formula
(`HeartbeatInterval * AgentStaleMultiplier`, 5-minute fallback), so the
dashboard agrees with whatever actually drives notifications instead of
inventing a second, possibly-divergent threshold.

## Phase 4 — Integrations

**Objective:** Expand the platform through external integrations without changing the runtime.

Capabilities: MQTT, Home Assistant, ONVIF, Zigbee, future IoT integrations.

**Home Assistant is bidirectional, not just another data source.** Two
distinct flows:

- **Inbound** — consume HA device/automation state as Vivnest events. This
  is a "buy vs. build" shortcut: HA already has thousands of existing
  device integrations, so tapping into it can reach far more devices, far
  faster, than natively implementing every protocol (Zigbee, Z-Wave, etc.)
  inside Vivnest.
- **Outbound** — Vivnest events trigger HA services/automations (e.g. an AI
  motion detection firing a scene). This fits the existing Command/Event
  model directly: an HA state change arrives as a Vivnest event; a Vivnest
  capability wanting to act calls HA's service API as a command.

Both directions read on the *current* event/command vocabulary — no new
concept needed, just a new capability that talks HA's REST/WebSocket API in
both directions.

**Deliverable:** An extensible integration ecosystem.

## Phase 5 — Intelligence

**Objective:** Introduce AI and intelligent automation.

Capabilities: AI Image Analysis, Statistics, Rules Engine, Natural Language Automation.

Sub-phases:

- **AI Phase 1** — person detection, vehicle detection, pet detection.
- **AI Phase 2** — package detection, face recognition (optional), custom models.

**Integration decision:** AI does not get its own pipeline. It generates
`DeviceEvent`s (the same domain type `CameraCaptureHandler` already
produces) that flow through the existing notification pipeline — a new
detection is just a new `EventType` under
[`DeviceEventTypes`](../../Vivnest.Core/Constants/DeviceEventTypes.cs), not
new plumbing.

**Deliverable:** A smart edge platform capable of intelligent decision making.

## Phase 6 — Distributed Runtime

**Objective:** Scale from a single edge agent to a distributed edge platform.

Capabilities: Mesh Networking, Peer Discovery, Capability Advertisement,
Command Routing, Load Balancing, Failover.

This phase covers two genuinely different distribution problems — worth
keeping distinct rather than treating as one thing:

- **Phase 6A, cross-site fleet management**: many independent customer
  sites (commercial/multi-tenant target — see ADR-008), each typically
  running its own agent(s), managed centrally from the cloud.
- **Phase 6B, intra-site agent mesh**: *within one site*, multiple
  specialized, containerized agents cooperating — e.g. a **Camera Agent**,
  **Storage Agent**, **AI Agent**, **Heartbeat Agent** — potentially each on
  its own physical Raspberry Pi, discovering each other, sharing load, and
  failing over to one another if a device goes down. This is a real target,
  not a hypothetical: the goal is that if the Pi running the Camera Agent
  dies, another Pi on-site picks up that role, and if one Pi is
  CPU-saturated, work shifts to an idle one.

**Architectural implication, worth noting now even though this is a later
phase:** today there is exactly one `Vivnest.Agent` process, and
`IEventHandler<T>` / `EventDispatcher` is an **in-memory**
dispatcher within that single process. Phase 6B requires dispatch to become
network-transparent between separate agent processes — service discovery,
serialized commands/events over the network, and a failover mechanism
(e.g. leader election or a health-check quorum) to decide when another
agent takes over a dead one's role. That's a materially bigger step than
the `ICapabilityHandler<T>` → `IEventHandler<T>` rename already done in
[EVOLUTION-PLAN.md](EVOLUTION-PLAN.md)'s near-term sequence — don't conflate
the two. The rename didn't get us closer to network-transparent dispatch;
it just keeps vocabulary consistent for whenever this phase is actually
tackled.

### Phase 6A — Fleet Management

- Agent discovery
- Capability advertisement
- Remote commands
- OTA updates
- Fleet configuration
- Health monitoring

### Phase 6B — Distributed Execution

- Command routing
- Load balancing
- Work migration
- Leader election
- Distributed scheduling
- Automatic failover
- High availability

### How each distributed feature would work

These are illustrative designs, not commitments — captured here so the
reasoning behind Phase 6 isn't lost, not because any of it is scheduled.

1. **Remote Updates (OTA).** The runtime itself doesn't know about updates —
   an Update capability handles the flow: Cloud sends
   `UpdateAvailableCommand` → Agent downloads the package → verifies its
   signature → installs it → restarts the runtime → publishes
   `AgentUpdatedEvent`. The kernel only needs to expose
   `IRuntimeController.RestartAsync()` / `StopAsync()`; everything else
   belongs to the Update capability. This supports updating one agent, a
   group, or the whole fleet, rolling back failed updates, and staged
   rollouts (5% → 20% → 100%) — the same model as Azure IoT Edge or
   Kubernetes rolling updates.

2. **Load balancing between agents.** Possible because every agent runs the
   same runtime. If one agent (e.g. Living Room) is CPU-saturated while
   others (Garage, Office) are idle, a future Coordinator capability could
   redistribute work: `Capture Request → Coordinator → Choose Best Agent →
   Execute Capture`. The runtime itself doesn't know anything about
   balancing — it only executes commands.

3. **Capability distribution.** If only one agent has an AI accelerator
   (GPU/NPU), a camera agent can publish `ImageCapturedEvent`, have it
   routed to the AI-capable agent for detection, and receive the result
   back. The camera doesn't care where the AI runs — this is exactly why
   commands and events are separated from execution.

4. **Peer discovery.** A future Discovery capability periodically
   broadcasts agent name, capabilities, version, CPU, memory, storage,
   health, and IP address. Every runtime builds a peer table, so distributed
   features (routing, failover, load balancing) have something to route
   against.

5. **Command routing.** Today, `CaptureImageCommand` goes straight to the
   Camera capability. Later, it could go through a Router that picks the
   best agent first — nothing inside the Camera capability changes.

6. **Failover.** If the Garage agent goes offline, another agent detects the
   missing heartbeat, takes ownership, and starts monitoring in its place.
   The runtime already supports lifecycle management, so failover is just
   another capability on top of it.

7. **Work migration.** Long-running jobs (e.g. AI analysis) could move from
   an overloaded agent to another and continue there. Harder than the rest
   of this list, but nothing in the architecture prevents it.

8. **Fleet management.** One command could target every agent: Cloud sends
   `RestartCommand` → fans out to N agents → each restarts → each publishes
   `RestartCompletedEvent`. The runtime already has command handling; fleet
   fan-out is a coordination layer on top.

9. **Distributed scheduling.** Today, "capture every 5 minutes" runs on a
   fixed agent. Later, a Scheduler could choose which agent runs a given
   job — the scheduler doesn't care which machine executes it.

10. **Rolling updates.** Update rolls through Agent 1 (healthy) → Agent 2
    (healthy) → Agent 3 (failed) → rollback. The same pattern enterprise
    deployments use.

**Deliverable:** A resilient distributed runtime supporting multiple edge agents.

## Deferred / Not Scheduled

Most "not now" work is already captured by Phase 4 onward above — treat
those phase numbers as the deferral list, not a separate one. A few
additional things don't map to a phase and are worth naming explicitly:

- **Plugin architecture / dynamic capability loading** — part of the Vivnest
  Runtime's target Extension Model
  ([vivnest-runtime-overview.md](../architecture/vivnest-runtime-overview.md)),
  not scheduled until a second real consumer needs it (see
  [EVOLUTION-PLAN.md](EVOLUTION-PLAN.md)'s rule of thumb).
- **MediatR** — considered and passed on; `EventDispatcher` already
  fills this role without the extra dependency.
- **Event sourcing** — no current requirement for it.
- **Kubernetes / K3s** — deployment orchestration; would only matter once
  Phase 6 (Distributed Runtime) is real.
- **WebSockets** — no current feature needs push updates; revisit only if
  the Phase 3 dashboard needs live updates instead of polling.

## Overall Roadmap

```text
Runtime Foundation    Complete   (superseded — see note below)
Capture Pipeline      Complete
Heartbeat Pipeline    Complete
Notification Engine   Next
REST API               Later
Dashboard              Later
AI Detection           Future
Distributed Agents     Future
Commercial Platform    Future
```

> **Note:** "Runtime Foundation — Complete" here refers to the *current*
> ad-hoc runtime (Agent + Cloud + Core + Infrastructure), not the Vivnest
> Runtime's Phase 1 kernel, which has not been built. See
> [EVOLUTION-PLAN.md](EVOLUTION-PLAN.md) for how the two relate.

## Guiding Principles

- The Vivnest Runtime is the runtime; Vivnest is the product.
- The runtime never references capabilities.
- Capabilities never reference each other directly.
- Capabilities communicate through commands and events.
- Business logic belongs inside capabilities.
- Workers orchestrate execution only.
- Cloud functionality is implemented as a capability.
- New features are added by creating capabilities rather than modifying the runtime.
- The architecture remains distributed-ready from day one, without building distribution before it's needed.
