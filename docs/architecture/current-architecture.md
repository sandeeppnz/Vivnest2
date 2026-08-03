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
  `CameraCaptureWorker`, `SmartPlugMonitorWorker`, `MotionSensorMonitorWorker`,
  `AgentHeartbeatWorker`, `DeviceHeartbeatWorker`, `HomeAssistantWorker`,
  `AgentMetricsWorker`. `AgentMetricsWorker` is deliberately its own
  `BackgroundService`, not folded into `AgentHeartbeatWorker`'s tick — a
  CPU/Memory-sampling failure must never be able to block the liveness
  heartbeat from publishing; see ADR-020.
  `MotionSensorMonitorWorker` has no probe/full-read split the way
  `CameraCaptureWorker`/`SmartPlugMonitorWorker` do — a motion sensor read
  is already as cheap as a liveness probe (one `control_child` round trip),
  so every tick does a full read, publishing
  `MotionSensorStateChangedEvent` only when `Detected` actually flips.
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
  `SmartPlugPowerStateChangedEvent`, `MotionSensorStateChangedEvent`,
  `MotionSensorReadingFailedEvent`, `AgentHeartbeatGeneratedEvent`,
  `DeviceHeartbeatGeneratedEvent`, `HomeAssistantStateChangedEvent`,
  `AgentMetricsSampledEvent`, `DeviceTriggeredEvent`. `DeviceTriggeredEvent`
  is deliberately generic (`DeviceId`/`DeviceType`/`Reason`), not
  capture-specific — any number of action-specific handlers can
  subscribe to it, each deciding independently whether it applies; see
  ADR-021.
- **Event Dispatcher**: `EventDispatcher` in
  `Vivnest.Agent/Runtime/Dispatching`, multicasting to every registered
  `IEventHandler<TEvent>` — this is the mechanism ADR-021's
  device-triggers-device design relies on; nothing new was needed to
  get "devices subscribe to an event."
- **Event Handlers** (`Vivnest.Agent/Runtime/EventHandlers`):
  `CameraCaptureHandler`, `CameraCaptureFailedHandler`,
  `SmartPlugReadingHandler`, `SmartPlugReadingFailedHandler`,
  `SmartPlugPowerStateChangedHandler`, `MotionSensorStateChangedHandler`,
  `MotionSensorReadingFailedHandler`, `AgentHeartbeatHandler`,
  `DeviceHeartbeatHandler`, `HomeAssistantStateChangedHandler`,
  `AgentMetricsHandler`, `MotionTriggerResolverHandler`,
  `CaptureOnTriggerHandler` — these own persistence and queue
  publishing. `MotionTriggerResolverHandler` is a *second* handler on
  `MotionSensorStateChangedEvent` (multicast dispatch already supports
  this); it only resolves `DeviceOptions.TriggersDeviceIds` into
  `DeviceTriggeredEvent`s, it doesn't know what a triggered device does.
  `CaptureOnTriggerHandler` is deliberately narrow — one immediate
  capture plus setting `DeviceRuntimeState.BurstUntilUtc`/`BurstInterval`,
  no persistence of its own (see below). Each is, informally, the
  reactive half of a future capability — but none of them are wrapped
  in a formal `ICapability` yet.
- **Azure Table Storage (Agent-side, write path)**:
  `AzureTableDeviceEventWriter`, `AzureTableAgentEventWriter`,
  `AgentHeartbeatWriter`, `DeviceHeartbeatWriter` in
  `Vivnest.Infrastructure` — named `Writer` because that's their
  defining role (the agent creates this data). `AzureTableAgentEventWriter`
  mirrors `AzureTableDeviceEventWriter` exactly (append-only rows,
  `tblAgentEvents`, generic `EventType`/`Payload` JSON) — see ADR-020.
- **Azure Table Storage (Cloud-side, read path)**: `AzureTableDeviceEventReader`,
  `AzureTableAgentEventReader`, `AzureTableDeviceHeartbeatReader`,
  `AzureTableAgentHeartbeatReader` in
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
  device *and* agent heartbeat — the only way to detect an agent gone
  silent, since an agent can't self-report that); `DeviceHeartbeatChangedFunction`
  (queue-triggered on `device-heartbeats`, near-instant reaction to one
  device's status change); `AgentHeartbeatChangedFunction`
  (queue-triggered on `agent-heartbeats`, near-instant reaction when an
  agent's first heartbeat arrives after being marked offline — recovery
  only, since an agent can't publish its own offline transition). All
  three delegate to `IHealthMonitorService` (`EvaluateAndNotifyAsync` for
  devices, `EvaluateAgentAndNotifyAsync` for agents) so the
  determination/notification logic exists once per level, not once per
  trigger — see [decision-log.md](decision-log.md) ADR-005.
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

- `GET /devices`, `GET /devices/{deviceId}` — `DeviceSummaryDto` now
  includes `AgentId`/`TenantId`/`SiteId` (added for `AgentDetail`'s
  device list and the dashboard's Agent link — previously present on
  `DeviceHeartbeatEntity` but never surfaced through the API)
- `GET /devices/{deviceId}/events?take=N`
- `GET /devices/{deviceId}/captures?take=N` (flat cap) or
  `?date=yyyy-MM-dd&skip=N&take=N` (one day, paginated — the dashboard's
  capture gallery; `IDeviceEventReader.GetByDeviceAndDateRangeAsync` uses
  a `RowKey` range filter rather than loading the whole partition, since
  `RowKey` is already timestamp-prefixed)
- `GET /devices/{deviceId}/captures/summary?days=N` — per-day counts only,
  no SAS URLs generated, so the gallery can render every day's collapsed
  header cheaply before the user expands anything (see ADR-017)
- `GET /agents`, `GET /agents/{agentId}` — `AgentSummaryDto` carries
  `TenantId`/`SiteId` (same reasoning as devices above), though the
  dashboard itself now shows those once in the header rather than
  per-entity — see ADR-018's follow-up
- `GET /agents/{agentId}/metrics?days=N` (default 30) — `AgentMetricSampleDto[]`
  (`OccurredAtUtc`/`CpuUsagePercent`/`MemoryUsedBytes`), parsed
  server-side from `AgentEventTypes.MetricsReported` rows for the
  dashboard's resource-usage chart; 403 for `DevicesOnly` keys, same as
  the other `/agents*` routes (see ADR-020)
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
dependency; a hand-rolled CSS custom-property token system (`App.css`)
instead — see ADR-018. Two tabs, **Devices** and **Agents**, the latter
hidden entirely (not just disabled) for a `DevicesOnly` key, decided from
`GET /whoami` right after login. Both lists render as status-accented row
cards (`.entity-list`/`.entity-row`), not raw tables, so they reflow at
narrow widths instead of horizontally scrolling.

Four views total, all state-driven (no router): `DeviceList` →
`DeviceDetail`, and `AgentList` → `AgentDetail` (new — previously agents
had no drill-down). `DeviceDetail`'s metric grid includes a clickable
link to the device's `AgentDetail` page (hidden for `DevicesOnly` keys,
which get 403 from `/agents*`); `AgentDetail` lists that agent's devices,
filtered client-side from the already-fetched device list rather than a
dedicated endpoint, linking back into `DeviceDetail`.

Device detail shows device health and — gated behind
`device.deviceType === "Camera"`, see ADR-007's frontend addendum — a
`CaptureGallery` component: a 30-day, day-grouped capture timeline
(`Today`, `Yesterday`, then full dates), collapsed by default except the
current day. Expanding a day (or the initial Today auto-expand) fetches
its captures one page at a time (50 at a time, newest first, "Load more"
for the rest) rather than the whole window or the whole day up front —
see ADR-017. Above the gallery sits a `Live Feed` hero panel (currently a
static placeholder — no streaming pipeline exists yet, see ADR-018);
clicking a thumbnail swaps that same panel to show the selected capture
with a "Back to live" control, rather than opening a separate preview or
a lightbox, so there's always exactly one large-image panel on the page.
Non-camera devices show `DeviceEventList` (extracted from what was
originally inline in `DeviceDetail`) instead of the gallery, and — since
every event a camera produces is `CameraCaptured`, already shown richer
in the gallery — cameras never call `GET .../events` at all.

## Device Types: two implemented, the rest still modeled-not-implemented

[`DeviceType`](../../Vivnest.Core/Enums/DeviceType.cs) lists eight values:
`Camera`, `HumiditySensor`, `SmokeAlarm`, `WaterLeak`, `HeatPump`,
`MotionSensor`, `DoorSensor`, `SmartPlug` — the domain model was written
with a multi-device-type future in mind. Three now have real capture paths:

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
- **MotionSensor** — [`IMotionSensor`](../../Vivnest.Core/MotionSensor/IMotionSensor.cs),
  `Task<MotionSensorState> GetStateAsync()`, shaped like `ISmartPlug` (a
  polled state reading — `Detected`, battery/signal/model/firmware — not
  image capture). `MotionSensorMonitorService` / `IMotionSensorFactory` /
  `MotionSensorMonitorWorker`. Backed by a Tapo H100 hub with a T100 child
  sensor, talked to over Tapo's newer **KLAP v2** protocol
  (`TapoKlapClient`, `Vivnest.Infrastructure/Tapo`) — encrypted
  (AES-128-CBC, session key derived from a SHA-256 handshake), unlike the
  plug's unauthenticated Kasa protocol, but a native C# reimplementation
  all the same, no Python sidecar or Home Assistant bridge needed. The
  hub is the only thing with a network presence; a child device (the
  T100) is addressed via the hub's `control_child` wrapper using its
  `ChildDeviceId` (`DeviceSettings.ChildDeviceId`) — see ADR-019. This is
  a separate, direct integration from the Home Assistant bridge below;
  the Tapo camera's own HTTPS/cloud-token protocol remains broken by the
  same TP-Link firmware bug noted above, but the H100/T100 use an
  entirely different protocol and were unaffected.

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

Five device types remain modeled-not-implemented: `HumiditySensor`,
`SmokeAlarm`, `WaterLeak`, `HeatPump`, `DoorSensor` — no reader, no worker,
no capability behind any of them yet.

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
  Every event also goes through `IHomeAssistantLivenessTracker`
  (`Vivnest.Agent/Services`), which is what actually keeps
  `DeviceHeartbeatEntity` current for HA-sourced devices (see below) — HA's
  own `state == "unavailable"` is the offline signal, everything else
  counts as evidence of reachability. `HomeAssistantWorker` also calls it
  directly (bypassing the DeviceEvent/notification pipeline) with a
  one-off REST state read per mapped entity on every successful
  (re)connect, so a status can't stay frozen across an agent restart with
  no subsequent HA event.
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
- **The original Sprint 6 motion-detection goal is now built, but not
  through HA.** `MotionSensorStateChangedEvent`/`MotionSensorStateChangedHandler`
  exist and are exercised against real Tapo H100/T100 hardware — see the
  `MotionSensor` device type entry above and ADR-019. It's a direct
  native integration parallel to this Home Assistant bridge, not routed
  through it; HA-sourced devices get a real `DeviceHeartbeatEntity`/heartbeat presence (via
  `IHomeAssistantLivenessTracker`, above), *and* Cloud now distinguishes
  "HA itself says this entity is unreachable" from "the agent's WebSocket
  connection to HA is down but HA is otherwise fine" —
  `IHomeAssistantConnectionTracker` (`Vivnest.Agent/Services`) tracks the
  latter, surfaced as `AgentHeartbeat.HomeAssistantLastConnectedUtc`, and
  `DeviceStatusResolver` cascades any `DeviceHeartbeatSource.HomeAssistant`
  device to `Unknown` (with notifications suppressed, same as the
  agent-level cascade) once that connection's been stale past
  `HealthMonitor:HomeAssistantConnectionStaleAfter`. See ADR-016's newest
  entry in [decision-log.md](decision-log.md) for the full build, which
  was triggered by a real bug (a `localhost:8123`-inside-Docker
  misconfiguration going undetected because nothing tracked HA connection
  health at all).
