# Vivnest Runtime — Architecture Overview

**Status:** Target architecture (north star), not yet implemented.
**Scope:** This document describes where the runtime is heading. For how and
when we actually get there from the current codebase, see
[../roadmap/EVOLUTION-PLAN.md](../roadmap/EVOLUTION-PLAN.md).

> This file consolidates two earlier drafts (originally named
> `VERA_v1_Architecture.md` and `Vivnest_Edge_Runtime_Architecture_VERA.md`)
> into a single canonical reference, so the architecture has one source of
> truth instead of two overlapping ones. The earlier drafts used the name
> "VERA" for the runtime layer; we're not using that name going forward —
> the runtime layer is just the **Vivnest Runtime**, and the product built
> on top of it is **Vivnest**.

## Vision

Build Vivnest as a distributed edge platform for **any IoT device or
sensor**, not a single camera application. The target market spans multiple
verticals: home monitoring, commercial CCTV management, agriculture (soil
and water sensors), and industrial IoT (heat pumps, remote equipment
sensors) — camera monitoring is the first capability shipped, not the
boundary of the product. **The Vivnest Runtime is the reusable foundation;
Vivnest is the product built on top of it through capabilities.** The
runtime stays stable while capabilities evolve independently — new device
types and new verticals are added by writing a capability, not by modifying
the runtime.

## Core Principles

1. The runtime is stable and rarely changes.
2. Commands express intent ("do something").
3. Events express facts ("something happened").
4. Workers schedule work; they do not contain business logic.
5. Command handlers execute business workflows.
6. Capabilities react to events via event handlers — a capability is the
   pluggable module; an event handler is the low-level mechanism inside it
   that reacts to one event type. The two are not the same thing.
7. Runtime state is in-memory and separate from persistence.
8. Cloud synchronization is implemented as a capability, not baked into the runtime.
9. Long-running work executes through an internal work queue, not inline.
10. Every agent runs the same runtime and can, optionally, participate in a distributed mesh.
11. The runtime never references capabilities.
12. Capabilities never reference each other directly — only through commands and events.
13. Infrastructure projects contain adapters only, no business logic.

## High-Level Flow

```text
Worker
  → Command Dispatcher
  → Command Handler
  → Runtime State / Infrastructure / Repositories
  → Event Dispatcher
  → Capabilities
```

## Architecture Layers

**The runtime becomes the platform. Cameras are just one capability.** The
runtime itself should never mention cameras, heartbeats, or any other
domain concept by name — if it did, it wouldn't be reusable. Once stable,
it should barely need to change for years; everything domain-specific
plugs in from outside it.

```text
                    Vivnest Edge Runtime
 ┌──────────────────────────────────────────────────────────┐
 │                                                            │
 │  Scheduler          Dispatcher          Runtime State      │
 │                                                            │
 │  Command Bus        Event Bus           Work Queue         │
 │                                                            │
 │  Capability Host    Service Registry    Health             │
 │                                                            │
 └──────────────────────────────────────────────────────────┘
              │
              ├──────────────────────────────┐
              ▼                              ▼
      Local Capabilities                Remote Agents
```

Notice there is no mention of cameras anywhere in the runtime box — that's
intentional, not an oversight.

**Kernel** — Hosting, Scheduling, Dispatching, Runtime State, Work Queue,
Agent Discovery, Capability Loader, Service Registry.

**Platform Services** — Capability Host, Service Registry, Configuration,
Diagnostics, Telemetry, Mesh (optional).

**Capabilities** (current and future) — Camera, Heartbeat, Cloud Sync,
MQTT, AI, Home Assistant, BLE, Zigbee, Modbus, BACnet, Licensing,
Statistics, OTA, Mesh Networking, Telegram Notifications, Email, Offline
Detection, Snapshot Scheduler, Dashboard API, Rules Engine, ONVIF.

**Heartbeat isn't special anymore.** In the target architecture, "Agent
Heartbeat" and "Device Heartbeat" are just two more capabilities plugged
into the same Capability Host as Camera or MQTT — not runtime-level
concepts. This is a real gap against today's code, worth naming plainly:
`AgentHeartbeatWorker` and `DeviceHeartbeatWorker` are currently hardcoded
`BackgroundService`s wired directly in `Program.cs`, not pluggable
capabilities. Closing that gap is Sprint 5 territory
([phase-1-runtime-foundation.md](phase-1-runtime-foundation.md)) — a real
Capability Host to plug into — not something to chase before then.

## Message Model

**Commands** represent intent. They may execute immediately or be queued.
Examples: `CaptureImageCommand`, `GenerateHeartbeatCommand`,
`RestartDeviceCommand`, `ExecuteInferenceCommand`,
`PublishCloudMessageCommand`, `SynchronizeStateCommand`,
`DiscoverPeersCommand`, `TransferWorkCommand`. Issued by three kinds of
sources: workers issue commands, capabilities issue commands, and (once
Phase 6B exists) remote agents issue commands — the dispatcher doesn't care
which.

```text
Command → Dispatcher → Single Handler → Result
```

**Events** represent completed facts, published after successful execution.
Examples: `CaptureCompleted`, `HeartbeatGenerated`, `BlobUploaded`,
`AgentJoined`, `AgentLeft`, `DeviceOffline`, `InferenceCompleted`,
`CloudConnected`, `PeerDiscovered`, `CapabilityLoaded`. Everything reacts
to events — a capability doesn't need to know who else is listening.

```text
Event → Dispatcher → 0..N Handlers
```

## Runtime State

In-memory only, never persisted directly:

- `AgentRuntimeState`
- `DeviceRuntimeState`
- `CaptureRuntimeState`
- Queue depth, health, peer status

## Persistence

Repositories provide durable storage, separate from runtime state:

- `AgentHeartbeatRepository`
- `DeviceHeartbeatRepository`
- `DeviceEventRepository`

## Internal Work Queue

**Include the work queue from day one (Sprint 7, not deferred to later),
but use it only where appropriate — not every command needs to be
queued.** There are two execution paths, and both implement the *same*
handler interface — the caller dispatching a command doesn't know or care
which path it takes:

```text
Immediate                       Queued
    │                               │
    ▼                               ▼
Command Dispatcher              Work Queue
    │                               │
    ▼                               ▼
Handler                         Worker
                                     │
                                     ▼
                                 Handler
```

An in-memory `Channel<T>` backs the queued path, for long-running or
asynchronous work such as:

- AI inference
- Video processing / timelapse generation
- Compression
- Cloud synchronization retries
- Uploads

**Priority matters here, not just FIFO ordering.** On constrained edge
hardware (a Raspberry Pi doing double duty as an agent), an urgent item —
an instant alert from motion/intrusion detection — shouldn't sit behind a
routine background job like timelapse compression or a queued retry. The
work queue needs at least two priority tiers (e.g. `Urgent` / `Normal`),
with urgent work always dequeued ahead of normal work regardless of arrival
order. This is a property of the queue itself, not something each
capability should have to implement separately.

## Mesh (optional, deferred)

Every node runs the same runtime. Nodes may provide different capabilities
while collaborating through commands and events — no dedicated master node
is required. Provides:

- Peer discovery
- Capability advertisement
- Command routing
- Event propagation
- Load balancing
- Failover

This is explicitly **not** part of the near-term plan — see the roadmap for
why it's deferred until there's a real multi-agent deployment to justify it.

## Capability Manifest

Each capability declares:

- Name
- Version
- Dependencies
- Consumed commands
- Consumed events
- Produced events
- Provided services
- Execution mode (immediate or queued)

## Architectural Rules

- The runtime never references capabilities.
- Capabilities never reference each other directly.
- Communication happens only through commands or events.
- Infrastructure projects contain adapters only.
- Workers orchestrate execution; they do not contain business logic.
- Cloud functionality is implemented as a capability.
- New features are added by creating capabilities, not by modifying the runtime.

## Milestones

A canonical ordering, reconciling the two source drafts:

1. Kernel (hosting, dispatchers, runtime state, scheduling) — see
   [phase-1-runtime-foundation.md](phase-1-runtime-foundation.md) for the
   detailed sprint breakdown.
2. Agent Heartbeat
3. Device Runtime / Device Heartbeat
4. Camera Capture
5. Cloud Sync
6. AI
7. Mesh (deferred — see roadmap)

## Mission

"A stable runtime with extensible capabilities for distributed edge
intelligence."
