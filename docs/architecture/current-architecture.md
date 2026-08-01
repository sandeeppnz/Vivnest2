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
- Event-driven: workers publish events, event handlers react to them.
- Capability-based extensibility, informally — `IEventHandler<T>` per event
  type. This is the low-level mechanism a capability would use internally,
  not a capability itself; there's no formal `ICapability`/Capability Host
  yet (see [decision-log.md](decision-log.md) and
  [vivnest-runtime-overview.md](vivnest-runtime-overview.md) for the
  distinction).
- Runtime state is separated from persistence (see below).
- Cloud services consume *persisted* business events, not runtime state directly.
- Azure Table Storage is the source of truth.

## High-Level Flow

```text
Workers
    ↓
Runtime Events
    ↓
Event Dispatcher
    ↓
Event Handlers
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
  `DeviceHeartbeatWorker` is event-driven, not periodic-unconditional: each
  tick it asks `IOfflineDetection`
  (`Vivnest.Agent/Capabilities/OfflineDetection.cs`) to evaluate the
  device's current status from `DeviceRuntimeState`, and only publishes a
  `DeviceHeartbeatGeneratedEvent` when that status differs from
  `DeviceRuntimeState.LastReportedStatus` — see
  [decision-log.md](decision-log.md) ADR-005.
- **Runtime Events** (`Vivnest.Agent/Runtime/Events`):
  `CameraCaptureCompletedEvent`, `CameraCaptureFailedEvent`,
  `AgentHeartbeatGeneratedEvent`, `DeviceHeartbeatGeneratedEvent`.
- **Event Dispatcher**: `EventDispatcher` in
  `Vivnest.Agent/Runtime/Dispatching`, multicasting to every registered
  `IEventHandler<TEvent>`.
- **Event Handlers** (`Vivnest.Agent/Runtime/EventHandlers`):
  `CameraCaptureHandler`, `CameraCaptureFailedHandler`,
  `AgentHeartbeatHandler`, `DeviceHeartbeatHandler` — these own persistence
  and queue publishing. Each is, informally, the reactive half of a future
  capability — but none of them are wrapped in a formal `ICapability` yet.
- **Azure Table Storage (Agent-side, write path)**:
  `AzureTableDeviceEventWriter`, `AgentHeartbeatWriter`,
  `DeviceHeartbeatWriter` in `Vivnest.Infrastructure` — named `Writer`
  because that's their defining role (the agent creates this data).
- **Azure Table Storage (Cloud-side, read path)**: `AzureTableDeviceEventReader`,
  `AzureTableDeviceHeartbeatReader`, `AzureTableAgentHeartbeatReader` in
  `Vivnest.Cloud` — never creates rows, only reads and makes narrow,
  targeted updates (notification state, processing status) to rows the
  agent already wrote. Deliberately not shared with the Agent-side
  `Writer` types above, even though both read the same tables — see
  [decision-log.md](decision-log.md) for why the names had to differ
  rather than both being called `...Repository`.
- **Azure Queue**: `AzureQueuePublisher`, carrying
  `{PartitionKey, RowKey}`-only messages.
- **Cloud Functions** (`Vivnest.Cloud.Functions`): `CameraCapturedFunction`
  (queue-triggered, delegates to `CameraCapturedHandler`);
  `HealthMonitorTimerFunction` (cron-triggered full sweep of every
  device/agent heartbeat — the only way to detect an agent gone silent);
  `DeviceHeartbeatChangedFunction` (queue-triggered on `device-heartbeats`,
  near-instant reaction to one device's status change). The Timer and
  Queue functions both delegate to the same `IHealthMonitorService`
  method (`EvaluateAndNotifyAsync`) so the determination/notification
  logic exists once, not twice — see
  [decision-log.md](decision-log.md) ADR-005.
- **Notification**: `Vivnest.Cloud.Notifications` —
  `INotificationDispatcher`/`NotificationDispatcher` fan a generic
  `Notification` out to every registered `INotificationChannel`.
  `TelegramNotificationChannel` is the only channel implemented today;
  `ITelegramService` is now purely the low-level Telegram API client
  behind it — nothing else calls it directly.
- **REST API** (`Vivnest.Cloud.Functions/Http`): read-only, tenant-scoped
  via `x-api-key` (see "REST API & Auth" below).
- **Dashboard** (`Vivnest.Dashboard`, React + Vite + TypeScript): consumes
  the REST API only, never Table Storage directly (see "Dashboard" below).

## Responsibilities

### Workers

- Perform work (capture an image, build a heartbeat).
- Update runtime state.
- Publish runtime events. Workers never persist directly — persistence is
  the event handler's job (see [decision-log.md](decision-log.md),
  ADR-001/002).

### Event Handlers

- Persist entities (via the relevant store/repository).
- Publish queue messages so the cloud side can pick up the resulting work.

### Runtime State

In-memory only (`DeviceRuntimeState` /
`Vivnest.Core.Camera.Stores.CaptureStatusStore`), storing transient
information only:

- `LastCaptureUtc`
- `LastFailureUtc`
- `LastActivityUtc` — updated by *either* a full capture *or* the
  lightweight `ICamera.IsReachableAsync()` liveness probe, whichever ran
  most recently; see ADR-010.
- `LastHeartbeatUtc`
- `LastBlobName`
- `LastError`
- `LastReportedStatus` — last status actually sent via `DeviceHeartbeat`,
  used for change detection so the heartbeat stays event-driven (ADR-005).

No cloud or business state is stored in runtime state — it exists purely so
a worker can answer "what happened last?" without a round-trip to storage.

## REST API & Auth

`Vivnest.Cloud.Functions/Http` — read-only, all routes under `/api`:

- `GET /devices`, `GET /devices/{deviceId}`
- `GET /devices/{deviceId}/events?take=N`
- `GET /devices/{deviceId}/captures?take=N` (flat cap) or
  `?days=N` (date-range — used by the dashboard's capture timeline;
  `IDeviceEventReader.GetByDeviceAndDateRangeAsync` uses a `RowKey` range
  filter rather than loading the whole partition, since `RowKey` is
  already timestamp-prefixed)
- `GET /agents`, `GET /agents/{agentId}`
- `GET /whoami` — lets the dashboard discover its own key's permissions
  after login
- `POST /apikeys`, `GET /apikeys?tenantId=X&siteId=Y`,
  `POST /apikeys/{keyId}/revoke` — key management

Two-tier auth, not one — see ADR-012 for the full reasoning:

- **Tenant tier** (`x-api-key` header): every read endpoint plus
  `/whoami`. Resolved by `IApiKeyAuthenticator` → `TenantContext
  {TenantId, SiteId, DevicesOnly}`. `DevicesOnly` keys get 403 from
  `/agents`/`/agents/{id}` — enforced server-side on the endpoint itself,
  not just hidden in the dashboard UI.
- **Operator tier** (`AuthorizationLevel.Function`, an Azure Functions host
  key): the three `/apikeys` endpoints. A tenant key can never see or
  revoke other keys.

Capture image URLs are short-lived SAS URIs
(`AzureBlobStorageClient.GenerateReadSasUri`, 15 minutes), generated
inline by `DeviceQueryService` when building a capture's response — not a
proxy download through the Function, and not a separately-stored
thumbnail (the dashboard displays the same full-resolution image scaled
down via CSS; see roadmap.md Sprint 5 for why a real thumbnail pipeline
isn't built yet).

## Dashboard

`Vivnest.Dashboard` — React + Vite + TypeScript, no UI framework
dependency. Two tabs, **Devices** and **Agents**, the latter hidden
entirely (not just disabled) for a `DevicesOnly` key, decided from
`GET /whoami` right after login. Device detail shows device health,
recent events, and — gated behind `device.deviceType === "Camera"`, see
ADR-007's frontend addendum — a `CaptureGallery` component: a 30-day,
day-grouped capture timeline (`Today`, `Yesterday`, then full dates), each
date section showing its complete set of captures, not a truncated
sample.

## Device Types: modeled broadly, implemented narrowly

[`DeviceType`](../../Vivnest.Core/Enums/DeviceType.cs) already lists seven
values: `Camera`, `HumiditySensor`, `SmokeAlarm`, `WaterLeak`, `HeatPump`,
`MotionSensor`, `DoorSensor` — the domain model was written with a
multi-device-type future in mind. But only `Camera` has an implemented
capture path. [`ICamera`](../../Vivnest.Core/Camera/ICamera.cs) is
`Task<Stream> CaptureAsync()` — shaped entirely around image capture — and
`CameraCaptureService` / `CameraCaptureResult` / `ICameraFactory` are the
only capture pipeline that exists. The other six device types have no
reader, no worker, and no capability behind them today.

The persistence and eventing layers, by contrast, are already
device-agnostic: `DeviceEvent.Data` is `object?` serialized to a generic
JSON `Payload` string, and `DeviceEventTypes` is just a set of string
constants — already including `MotionDetected`, `SmokeDetected`,
`HumidityChanged`, `TemperatureChanged`, `WaterLeakDetected` alongside
`CameraCaptured`, mirroring `DeviceType`'s reach beyond cameras. Adding a
new event type doesn't require schema changes; those five non-camera
constants exist with nothing that raises them today — the same
"modeled, not implemented" pattern as `DeviceType`. The gap
is specifically in the *capture* layer, not persistence. See
[decision-log.md](decision-log.md) ADR-007 for what this means for adding
the second device type.
