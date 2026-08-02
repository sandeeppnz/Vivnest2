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
  `CameraCaptureWorker`, `SmartPlugMonitorWorker`, `AgentHeartbeatWorker`,
  `DeviceHeartbeatWorker`, `HomeAssistantWorker`.
  `DeviceHeartbeatWorker` is event-driven, not periodic-unconditional: each
  tick it asks `IOfflineDetection`
  (`Vivnest.Agent/Capabilities/OfflineDetection.cs`) to evaluate the
  device's current status from `DeviceRuntimeState`, and only publishes a
  `DeviceHeartbeatGeneratedEvent` when that status differs from
  `DeviceRuntimeState.LastReportedStatus` — see
  [decision-log.md](decision-log.md) ADR-005. `HomeAssistantWorker` is
  different in kind from the others — a persistent WebSocket subscriber
  reacting to Home Assistant's own `state_changed` push events, not a
  polling loop (see "Home Assistant Integration" below).
- **Runtime Events** (`Vivnest.Agent/Runtime/Events`):
  `CameraCaptureCompletedEvent`, `CameraCaptureFailedEvent`,
  `SmartPlugReadingCompletedEvent`, `SmartPlugReadingFailedEvent`,
  `SmartPlugPowerStateChangedEvent`, `AgentHeartbeatGeneratedEvent`,
  `DeviceHeartbeatGeneratedEvent`, `HomeAssistantStateChangedEvent`.
- **Event Dispatcher**: `EventDispatcher` in
  `Vivnest.Agent/Runtime/Dispatching`, multicasting to every registered
  `IEventHandler<TEvent>`.
- **Event Handlers** (`Vivnest.Agent/Runtime/EventHandlers`):
  `CameraCaptureHandler`, `CameraCaptureFailedHandler`,
  `SmartPlugReadingHandler`, `SmartPlugReadingFailedHandler`,
  `SmartPlugPowerStateChangedHandler`, `AgentHeartbeatHandler`,
  `DeviceHeartbeatHandler`, `HomeAssistantStateChangedHandler` — these own
  persistence and queue publishing. Each is, informally, the reactive half
  of a future capability — but none of them are wrapped in a formal
  `ICapability` yet.
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

## Device Types: two implemented, the rest still modeled-not-implemented

[`DeviceType`](../../Vivnest.Core/Enums/DeviceType.cs) lists eight values:
`Camera`, `HumiditySensor`, `SmokeAlarm`, `WaterLeak`, `HeatPump`,
`MotionSensor`, `DoorSensor`, `SmartPlug` — the domain model was written
with a multi-device-type future in mind. Two now have real capture paths:

- **Camera** — [`ICamera`](../../Vivnest.Core/Camera/ICamera.cs),
  `Task<Stream> CaptureAsync()`, shaped entirely around image capture.
  `CameraCaptureService` / `CameraCaptureResult` / `ICameraFactory` /
  `CameraCaptureWorker`.
- **SmartPlug** — [`ISmartPlug`](../../Vivnest.Core/SmartPlug/ISmartPlug.cs),
  `Task<SmartPlugState> GetStateAsync()`, shaped around a polled state
  reading (on/off, power/voltage/current, brand/model/firmware), not image
  capture. `SmartPlugMonitorService` / `SmartPlugReadingResult` /
  `ISmartPlugFactory` / `SmartPlugMonitorWorker`. Talks to the device over
  the legacy Kasa protocol (`KasaSmartPlug`, `Vivnest.Infrastructure/SmartPlug`) —
  plain TCP on port 9999, XOR-obfuscated JSON, no TLS/auth — a native C#
  implementation, no external process or library needed, since that
  protocol is simple and stable (unlike the Tapo camera's HTTPS/cloud-token
  auth, which is currently broken by a TP-Link firmware bug — see ADR
  entries on the Tapo motion-detection investigation).

**This is Stage 2 (JOURNEY.md) actually landing, not just being planned.**
It answered the open question ADR-007 posed: does a second device type
reuse `ICamera`, or does it need its own shape? It needed its own shape —
`ISmartPlug` shares no code with `ICamera`, deliberately (a plug doesn't
capture images; forcing one interface over both would have been the wrong
generalization). What *did* carry over for free, unchanged: `DeviceHeartbeatWorker`,
`OfflineDetection`, and the whole persistence/eventing pipeline — all
already operated on generic `DeviceRuntimeState`/`DeviceEvent` fields, so
a second device type just started flowing through them without any
changes there. That's the split current-architecture predicted: the
*capture* layer is device-specific, everything downstream of it isn't.

Six device types remain modeled-not-implemented: `HumiditySensor`,
`SmokeAlarm`, `WaterLeak`, `HeatPump`, `MotionSensor`, `DoorSensor` — no
reader, no worker, no capability behind any of them yet.

The persistence and eventing layers were already device-agnostic before
SmartPlug proved it: `DeviceEvent.Data` is `object?` serialized to a
generic JSON `Payload` string, and `DeviceEventTypes` is just a set of
string constants — `MotionDetected`, `SmokeDetected`, `HumidityChanged`,
`TemperatureChanged`, `WaterLeakDetected`, and now `PowerReading`
alongside `CameraCaptured`. Adding a new event type doesn't require schema
changes. See [decision-log.md](decision-log.md) ADR-007 for the original
reasoning and the newer ADR entry for how the SmartPlug build confirmed it.

## Home Assistant Integration

A real, working bridge to a self-hosted Home Assistant instance — not the
motion-detection consumer Sprint 6 (roadmap.md Phase 4) originally set out
to build, but the generic inbound/outbound plumbing that goal depends on,
verified against a real HA instance and a real HS110 smart plug. See
[decision-log.md](decision-log.md) ADR-016 for the full build and
verification writeup.

- **Inbound** (`Vivnest.Agent/Runtime/Workers/HomeAssistantWorker.cs`): a
  `BackgroundService` holding a persistent `ClientWebSocket` to HA's
  `/api/websocket` — connects, authenticates with a long-lived access
  token, subscribes to `state_changed`, and reconnects on any failure.
  Unlike the other workers, it's push-driven, not a polling loop. A
  periodic `{"type":"ping"}` (every 20s) plus a 45s receive timeout guard
  against the connection going silently stale — a real failure mode found
  during verification, not a defensive guess (ADR-016).
  `HomeAssistantOptions`/`HomeAssistantEntityOptions`
  (`Vivnest.Core/Options`) bind an explicit `HomeAssistant:Entities`
  allowlist (`EntityId` → `DeviceId`/`DeviceType`/`EventType`), the same
  explicit-config style as `Devices[]` — no automatic discovery of
  everything HA knows about. A matched entity's state change dispatches
  `HomeAssistantStateChangedEvent` through the existing `IEventDispatcher`;
  `HomeAssistantStateChangedHandler` (`Vivnest.Agent/Runtime/EventHandlers`)
  persists a `DeviceEvent` and publishes to the existing `DeviceEventQueue`
  — no Cloud-side code needed, same pipeline every other device event uses.
- **Outbound**: `IHomeAssistantCommandSender`/`HomeAssistantCommandSender`
  (`Vivnest.Agent/Services`), a typed `HttpClient` calling HA's REST
  `/api/services/<domain>/<service>` to control a device through HA (e.g.
  `switch.turn_off`). Built and manually verified; no automatic trigger
  wired to it yet (there's no motion-triggered-capture or AI-detection
  consumer built yet either — see roadmap.md Phase 5).
- **A device reachable multiple ways keeps one `DeviceId`.** The HS110 is
  monitored both directly (`SmartPlugMonitorWorker` over the Kasa
  protocol) and via HA — both write into the same `DeviceId`'s
  `DeviceEvent` timeline. `DeviceEvent` has no single-writer assumption, so
  this isn't a special case; see ADR-016 for why this doesn't collide with
  `ICaptureStatusStore`/`DeviceRuntimeState`, and for why `Devices[]` and
  `HomeAssistant:Entities` stay two separate, unmerged config sections.
- **Not built yet:** the original Sprint 6 motion-detection goal itself —
  no `MotionDetectedEvent`/`MotionCaptureHandler` exists, and none of this
  has been exercised against a motion sensor (none is on hand). HA-sourced
  devices also don't get a `DeviceHeartbeatEntity`/heartbeat presence yet —
  only `DeviceEvent` history, driven purely by whatever HA pushes.
