# Current Architecture (As-Built)

**Status:** Describes the system as it exists today, verified against the
actual code. Contrast with
[vivnest-runtime-overview.md](vivnest-runtime-overview.md), which describes
where it's headed. See [decision-log.md](decision-log.md) for the binding
rules behind these choices, and
[../roadmap/EVOLUTION-PLAN.md](../roadmap/EVOLUTION-PLAN.md) for how one
becomes the other.

## Vision

Vivnest is an edge-first monitoring platform built around an autonomous
agent that captures device data, with the cloud side consuming that data to
drive notifications, an API, and a dashboard.

## Core Principles (current system)

- Edge-first processing — the agent captures and persists independently of the cloud being reachable.
- Event-driven: workers publish events, capability handlers react to them.
- Capability-based extensibility, informally — `ICapabilityHandler<T>` per event type, not yet a formal capability host.
- Runtime state is separated from persistence (see below).
- Cloud services consume *persisted* business events, not runtime state directly.
- Azure Table Storage is the source of truth.

## High-Level Flow

```text
Workers
    ↓
Runtime Events
    ↓
Capability Dispatcher
    ↓
Capability Handlers
    ↓
Azure Table Storage
    ↓
Azure Queue
    ↓
Cloud Functions
    ↓
Notification / API / Dashboard
```

Concretely, in code:

- **Workers** (`BackgroundService`s in `Vivnest.Agent/Runtime/Workers`):
  `CameraCaptureWorker`, `AgentHeartbeatWorker`, `DeviceHeartbeatWorker`.
- **Runtime Events** (`Vivnest.Agent/Runtime/Events`):
  `CameraCaptureCompletedEvent`, `CameraCaptureFailedEvent`,
  `AgentHeartbeatGeneratedEvent`, `DeviceHeartbeatGeneratedEvent`.
- **Capability Dispatcher**: `CapabilityDispatcher` in
  `Vivnest.Agent/Runtime/Dispatching`, multicasting to every registered
  `ICapabilityHandler<TEvent>`.
- **Capability Handlers** (`Vivnest.Agent/Runtime/EventHandlers`):
  `CameraCaptureHandler`, `CameraCaptureFailedHandler`,
  `AgentHeartbeatHandler`, `DeviceHeartbeatHandler` — these own persistence
  and queue publishing.
- **Azure Table Storage**: `AzureTableDeviceEventStore`,
  `AgentHeartbeatStore`, `DeviceHeartbeatStore` in `Vivnest.Infrastructure`.
- **Azure Queue**: `AzureQueuePublisher`, carrying
  `{PartitionKey, RowKey}`-only messages.
- **Cloud Functions**: `CameraCapturedFunction` in
  `Vivnest.Cloud.Functions`, queue-triggered, delegating to
  `CameraCapturedHandler` in `Vivnest.Cloud`.
- **Notification**: `TelegramService` (the only channel implemented today).
  **API / Dashboard**: not yet built — see
  [../roadmap/roadmap.md](../roadmap/roadmap.md) (Phase 3).

## Responsibilities

### Workers

- Perform work (capture an image, build a heartbeat).
- Update runtime state.
- Publish runtime events. Workers never persist directly — persistence is
  the capability handler's job (see [decision-log.md](decision-log.md),
  ADR-001/002).

### Capability Handlers

- Persist entities (via the relevant store/repository).
- Publish queue messages so the cloud side can pick up the resulting work.

### Runtime State

In-memory only (`DeviceRuntimeState` /
`Vivnest.Core.Camera.Stores.CaptureStatusStore`), storing transient
information only:

- `LastCaptureUtc`
- `LastFailureUtc`
- `LastActivityUtc`
- `LastHeartbeatUtc`
- `LastBlobName`
- `LastError`

No cloud or business state is stored in runtime state — it exists purely so
a worker can answer "what happened last?" without a round-trip to storage.
