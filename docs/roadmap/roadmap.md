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

Architecture:

```text
Heartbeat
    ↓
tblDeviceHeartbeat
    ↓
Health Monitor
    ↓
Offline Detection
    ↓
Telegram
```

Implementation order:

1. `HealthMonitorTimerFunction` (Timer Trigger)
2. `IHealthMonitorService`
3. `OfflineDetectionRule`
4. `RecoveryDetectionRule`
5. `TelegramNotificationService`
6. Notification state persistence
7. Integration tests (blocked on a test project existing — see [EVOLUTION-PLAN.md](EVOLUTION-PLAN.md))

Outcome: automatic offline alerts, automatic recovery alerts, no duplicate
notifications.

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
vs. cloud-triggered-capture fork this implies — default to agent-local
until there's a concrete reason for a Cloud → Agent command channel.

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
channel. Build one `NotificationWorker` that constructs a channel-agnostic
`Notification` (type, title, message, images, priority, occurred-at), then
fans it out to every configured `INotificationChannel` (`SendAsync`). Adding
Discord, Slack, Signal, Teams later means implementing one more channel —
nothing else changes. Notification types worth building first: Daily
Summary, Instant Alert (heartbeat/capture lost), Scheduled Snapshot, Daily
Album, Motion Alert (future), Camera Offline.

#### Sprint 4 — REST API

- `GET /devices`
- `GET /devices/{id}`
- `GET /devices/{id}/events`
- `GET /devices/{id}/captures`

Read-only for the MVP, reading directly from Azure Table Storage — no
separate database or cache layer. `Vivnest.Cloud.Functions` currently has
zero HTTP triggers, so this is net-new infrastructure, not an addition to
something already there.

#### Sprint 5 — Dashboard

- Device health
- Last heartbeat
- Last capture
- Latest image
- Recent events

The dashboard consumes the REST API only — it does not read Table Storage
directly. Depends on Sprint 4.

## Phase 4 — Integrations

**Objective:** Expand the platform through external integrations without changing the runtime.

Capabilities: MQTT, Home Assistant, ONVIF, Zigbee, future IoT integrations.

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

Long-term vision: multiple Vivnest agents cooperating within a site, with
distinct roles rather than every agent doing everything — e.g. a **Camera
Agent**, **Storage Agent**, **AI Agent**, **Gateway Agent**. Points toward
agent discovery, workload distribution, and site-level resilience as
concrete next steps once this phase is actually reached.

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
- **MediatR** — considered and passed on; `CapabilityDispatcher` already
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
