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

Build Vivnest as a distributed edge platform rather than a single camera
application. **The Vivnest Runtime is the reusable foundation; Vivnest is
the product built on top of it through capabilities.** The runtime stays
stable while capabilities evolve independently — new functionality is added
by writing a capability, not by modifying the runtime.

## Core Principles

1. The runtime is stable and rarely changes.
2. Commands express intent ("do something").
3. Events express facts ("something happened").
4. Workers schedule work; they do not contain business logic.
5. Command handlers execute business workflows.
6. Event handlers (capabilities) react to completed work.
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

```text
                    Vivnest.Agent
                          │
                          ▼
                  Vivnest.Hosting
                          │
                          ▼
                  Vivnest.Runtime
 ┌──────────────────────────────────────────────┐
 │ Kernel                                        │
 │ Runtime Context                               │
 │ Runtime State                                 │
 │ Lifecycle Manager                             │
 │ Capability Host                               │
 │ Command Dispatcher                            │
 │ Event Dispatcher                               │
 │ Internal Work Queue                           │
 │ Scheduler                                     │
 │ Diagnostics                                   │
 │ Telemetry                                     │
 └──────────────────────────────────────────────┘
                          │
          ┌───────────────┼────────────────┐
          ▼               ▼                ▼
   Camera Capability  Telegram Capability  Cloud Sync Capability
                          │
                  Vivnest.Abstractions
```

**Kernel** — Hosting, Dependency Injection, Scheduler, Command Dispatcher,
Event Dispatcher, Work Queue (`Channel<T>`), Runtime Context, Lifecycle.

**Platform Services** — Capability Host, Service Registry, Configuration,
Diagnostics, Telemetry, Mesh (optional).

**Capabilities** (current and future) — Camera, Agent Heartbeat, Device
Heartbeat, Cloud Sync, Telegram Notifications, Email, Offline Detection,
Snapshot Scheduler, Dashboard API, AI Image Analysis, Statistics, Rules
Engine, MQTT, Home Assistant, ONVIF, Zigbee, BLE, Modbus, BACnet, OTA
Updates, Licensing.

## Message Model

**Commands** represent intent. They may execute immediately or be queued.
Examples: `GenerateAgentHeartbeatCommand`, `CaptureImageCommand`,
`RestartDeviceCommand`, `DiscoverPeersCommand`, `ExecuteInferenceCommand`.

```text
Command → Dispatcher → Single Handler → Result
```

**Events** represent completed facts, published after successful execution.
Examples: `AgentHeartbeatGeneratedEvent`, `DeviceStateChangedEvent`,
`CaptureCompletedEvent`, `CaptureFailedEvent`, `PersonDetectedEvent`,
`PeerDiscoveredEvent`.

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

An in-memory `Channel<T>` handles long-running or asynchronous work.
Immediate commands execute synchronously; queued work is for things like:

- AI inference
- Video processing / timelapse generation
- Compression
- Cloud synchronization retries
- Uploads

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
