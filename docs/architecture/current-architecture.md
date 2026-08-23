# Current Architecture (As-Built)

**Status:** Describes the system as it exists today, verified against the
actual code. Contrast with
[vivnest-runtime-overview.md](vivnest-runtime-overview.md), which describes
where it's headed. See [decision-log.md](decision-log.md) for the binding
rules behind these choices, and
[../roadmap/EVOLUTION-PLAN.md](../roadmap/EVOLUTION-PLAN.md) for how one
becomes the other.

**Read [Known gaps, risks & inconsistencies](#known-gaps-risks--inconsistencies)
at the end before trusting any single section here.** The body of this
document is written by accretion — each ADR appends its own bullet, and
superseded statements have sometimes been left in place rather than
rewritten. That final section is the register of what is unfinished,
duplicated, unused or genuinely risky, which the descriptive body
deliberately doesn't cover.

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

**File organization is by capability, not by architectural layer** —
`Vivnest.Agent/Capabilities/{Camera,SmartPlug,MotionSensor,DeviceHealth,Triggers}/`
each hold that capability's worker, service, event handler(s), and
event(s) together in one folder/namespace, rather than the previous
`Runtime/Workers`/`Runtime/EventHandlers`/`Runtime/Events`/`Services`
split that scattered one capability's files across four unrelated
top-level folders. `Capabilities/Bridges/HomeAssistant/` is one level
deeper than the direct-to-hardware capabilities — HomeAssistant isn't a
device type, it's a bridge that can carry any device type through it
(the smart plug is reachable both natively and via this bridge, ADR-016),
so it sits under a `Bridges/` parent reserved for that kind of
integration rather than as a sibling to Camera/SmartPlug/MotionSensor.
It's the only thing in `Bridges/` today; see decision-log.md for why that
grouping was still judged worth adding with just one member.
`Vivnest.Agent/Runtime/Shell/` holds the pieces that
aren't a device capability - `PlatformAgentHeartbeatWorker`,
`PlatformAgentMetricsWorker`, `PlatformCommandPollingWorker`,
`PlatformAgentCommandPollingWorker`, `PlatformLogShippingWorker`,
`PlatformErrorEventWorker`, `NetworkUsageTracker` (all `Platform`-prefixed
since ADR-089) - matching
the shell/capability split named directly when this was proposed.
`Runtime/Dispatching` (`EventDispatcher`) and the generic `Interfaces/`
types (`IEventHandler<T>`, `IEventDispatcher`) are the only things that
stayed put - genuinely capability-agnostic runtime machinery. A third
file, `Interfaces/ICapability.cs`, used to sit alongside them; it was an
aspirational stub for the Capability Host with zero implementations and
zero references (and, tellingly, no `namespace` statement at all), and
was **removed** in the dead-code pass. When a real Capability Host is
built, its contract should be written against the capabilities that
exist by then rather than resurrected from that stub.
No new assemblies, no plugin loader - same single deployable project,
reorganized for readability. See decision-log.md's
`Vivnest.Agent` reorganization entry for the reasoning (and why a
formal plugin/package system was explicitly declined for now).

- **Startup: shared config fetch, both roles** (`Vivnest.Agent/Bootstrap/AgentConfigurationLoader.cs`,
  `TryLoadRemoteSharedConfigAsync`, ADR-037) — loaded *before* the
  per-agent blob below, from a single well-known
  `shared-config/common-config.json` blob (`SharedConfigBlob.cs`, fixed
  name — no per-entity ID applies). Holds `Storage.BlobContainer`,
  `Tables`, the entire `Messaging` section (every queue name, including
  the role-specific ones like `CameraCapturedQueue`/`ClassifyCommandQueue`
  — harmless on the role that doesn't read them, same reasoning
  `AiClassificationOptions` is already bound unconditionally on both
  roles), and the `AgentHeartbeat`/`DeviceHeartbeat`/`DeviceEvents`/
  `AgentEvents`/`AgentMetrics` toggle sections — previously hand-duplicated
  (or hand-split) across both per-agent blobs, which caused two real
  config-drift bugs before this existed. `Storage.ConnectionString` and
  `Agent:AgentId`/`Agent:Type` stay local-only — the former structurally
  can't live in any remote blob (it's needed just to reach one), the
  latter identify which agent/type is loading in the first place. Loading
  first (lower precedence) means the per-agent blob can still override a
  shared value if ever needed. Local dev gets a parallel
  `TryLoadLocalSharedConfig` reading the same-shaped local
  `common-config.json` file — required, not just for symmetry: trimming
  the local per-agent files without it would silently disable
  heartbeats/metrics locally,
  since their `Options` classes default `Enabled` to `false`.
  `common-config.json` itself is git-tracked — its one sensitive field
  (`Messaging.ConnectionString`) lives instead in a sibling, local-only
  `common-config.secrets.json` (`TryLoadLocalSharedSecrets`, loaded
  unconditionally regardless of `LoadLocalSettings`, right after this
  block — see ADR-038).
- **Startup: remote config fetch** (`Vivnest.Agent/Bootstrap/AgentConfigurationLoader.cs`,
  `TryLoadRemoteConfigAsync`) — before the host builds, the Agent reads
  `Agent:AgentId`/`Storage:ConnectionString` from local
  `appsettings.json`/env vars (the only things that have to stay local —
  they're what's needed to reach anything remote at all), then downloads
  `agent-config/{agentId}.json` from Blob Storage directly (no REST API
  involved) and layers it into `IConfiguration` ahead of the
  environment-variables source (and after the shared-config layer above),
  so env var overrides still win and this agent's own values still win
  over the shared defaults. Additive, not a replacement — a missing or
  unreachable blob just means the agent runs on local config alone,
  exactly as it always has. See ADR-025. This blob is git-tracked too now
  — its sensitive fields (e.g. the Capture agent's `HomeAssistant.Password`/
  `AccessToken`) live in a local-only `{agentId}.secrets.json` sibling
  (`TryLoadLocalAgentSecrets`, same unconditional-regardless-of-
  `LoadLocalSettings` loading as the shared secrets above — ADR-038).
- **Startup: device config fetch, Low-type only** (`Vivnest.Agent/Bootstrap/AgentConfigurationLoader.cs`,
  `TryLoadRemoteDeviceConfigsAsync`) — `Devices[]` no longer lives embedded
  in the agent-config blob above. Instead, right after config layering,
  a Low-type agent lists every blob in a separate `device-config`
  container, downloads and parses each, keeps only the ones whose
  `OwningAgentId` matches its own `AgentId`, and merges the survivors into
  `IConfiguration` under the same root `"Devices"` key — so
  `Configure<DevicesOptions>(builder.Configuration)` and every downstream
  `IDeviceRuntimeStore` consumer are unaffected by where the data actually
  came from. High-type agents skip this entirely (they never consumed
  `Devices`). Same additive convention as the agent-config fetch — a
  missing container means zero devices, one bad blob is skipped, neither
  aborts startup. See ADR-036. Device blobs are git-tracked drafts too;
  each device's `Settings.Password`/etc. live in a local-only
  `device-config/{deviceId}.secrets.json` sibling, merged into that
  device's `JsonObject` in code (`TryMergeLocalDeviceSecrets`/
  `MergeJsonInto`) right after the ownership filter, before it's added to
  the `Devices` array — a separate config source can't target a field
  inside one array element, only the array-in-code assembly this function
  already does (ADR-038). `DeviceOptions` also carries `Sensors` (list of
  `SensorOptions {Name, Accessible, InaccessibleReason?}`) — hand-authored,
  hardware-specific facts (e.g. a camera's PIR being blocked by firmware),
  not derived from `DeviceType`; default empty list, so existing device
  blobs need no edits. Only consumed today by the Cloud-side Capabilities
  API below, not by the Agent itself. See ADR-040.
- **Workers** (`BackgroundService`s, one per capability folder plus
  `Runtime/Shell` for the non-capability ones). Capability-driven:
  `CameraCaptureWorker`, `SmartPlugMonitorWorker`, `MotionSensorMonitorWorker`,
  `HomeAssistantWorker`, `TapoHubLivenessWorker`, `SinkCleanlinessWorker`.
  Platform (ADR-089 prefix): `PlatformAgentHeartbeatWorker`,
  `PlatformDeviceHeartbeatWorker`, `PlatformAgentMetricsWorker`,
  `PlatformCommandPollingWorker`, `PlatformAgentCommandPollingWorker`,
  `PlatformLogShippingWorker`.
  `PlatformCommandPollingWorker` is
  the Agent's first-ever queue *consumer* (every other queue interaction
  from the Agent has been publish-only) — polls `agent-restart-commands`
  every 15s, and on a matching command calls
  `IHostApplicationLifetime.StopApplication()`; the container's own
  `--restart unless-stopped` policy brings it back, not any code in this
  process. See ADR-024. `PlatformAgentMetricsWorker` is deliberately its own
  `BackgroundService`, not folded into `PlatformAgentHeartbeatWorker`'s tick — a
  CPU/Memory-sampling failure must never be able to block the liveness
  heartbeat from publishing; see ADR-020. `PlatformLogShippingWorker` mirrors
  `PlatformAgentMetricsWorker`'s shape (own `PeriodicTimer`, own try/catch,
  default 5-minute interval) — each tick overwrites
  `agent-logs/{agentId}.txt` in Blob Storage with the current contents of
  an in-memory ring buffer (`AgentLogBuffer`, capped at 500 lines) that a
  custom `ILoggerProvider` (`AgentLogBufferLoggerProvider`, registered via
  `builder.Logging.AddProvider` before the host builds) fills from
  Warning+Error log calls across every category. See ADR-027.
  `MotionSensorMonitorWorker` has no probe/full-read split the way
  `CameraCaptureWorker`/`SmartPlugMonitorWorker` do — a motion sensor read
  is already as cheap as a liveness probe (one `control_child` round trip),
  so every tick does a full read, publishing
  `MotionSensorStateChangedEvent` only when `Detected` actually flips, and
  (independently) `MotionSensorBatteryReportedEvent` whenever
  `DeviceOptions.BatteryReportInterval` (default 2h) has elapsed since
  `DeviceRuntimeState.LastBatteryReportUtc` — same throttle shape as
  `SnapshotInterval`, just gating persistence of a field already read on
  every tick rather than an extra device round trip; see ADR-022.
  `PlatformDeviceHeartbeatWorker` is event-driven, not periodic-unconditional: each
  tick it asks `IOfflineDetection`
  (`Vivnest.Agent/Capabilities/DeviceHealth/OfflineDetection.cs`) to evaluate the
  device's current status from `DeviceRuntimeState`, and only publishes a
  `DeviceHeartbeatGeneratedEvent` when that status differs from
  `DeviceRuntimeState.LastReportedStatus` — see
  [decision-log.md](decision-log.md) ADR-005. `HomeAssistantWorker` is
  different in kind from the others — a persistent WebSocket subscriber
  reacting to Home Assistant's own `state_changed` push events, not a
  polling loop (see "Home Assistant Integration" below).
- **Runtime Events** (co-located with their capability, see above):
  `CameraCaptureCompletedEvent`, `CameraCaptureFailedEvent`,
  `SmartPlugReadingCompletedEvent`, `SmartPlugReadingFailedEvent`,
  `SmartPlugPowerStateChangedEvent`, `MotionSensorStateChangedEvent`,
  `MotionSensorReadingFailedEvent`, `MotionSensorBatteryReportedEvent`,
  `AgentHeartbeatGeneratedEvent`,
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
- **Event Handlers** (co-located with their capability, see above;
  `Triggers/` for the cross-capability ones):
  `CameraCaptureHandler`, `CameraCaptureFailedHandler`,
  `SmartPlugReadingHandler`, `SmartPlugReadingFailedHandler`,
  `SmartPlugPowerStateChangedHandler`, `MotionSensorStateChangedHandler`,
  `MotionSensorReadingFailedHandler`, `MotionSensorBatteryHandler`,
  `AgentHeartbeatHandler`,
  `DeviceHeartbeatHandler`, `HomeAssistantStateChangedHandler`,
  `AgentMetricsHandler`, `MotionTriggerResolverHandler`,
  `CaptureOnTriggerHandler`, `SinkCleanlinessHandler` — these own
  persistence and queue publishing. `MotionSensorBatteryHandler` mirrors `SmartPlugReadingHandler`
  exactly — persists a `BatteryStatus` `DeviceEvent` via `IDeviceEventWriter`,
  no queue publish, see ADR-022. `MotionTriggerResolverHandler` is a *second* handler on
  `MotionSensorStateChangedEvent` (multicast dispatch already supports
  this); it only resolves `DeviceOptions.TriggersDeviceIds` into
  `DeviceTriggeredEvent`s, it doesn't know what a triggered device does.
  `CaptureOnTriggerHandler` is deliberately narrow — one immediate
  capture plus setting `DeviceRuntimeState.BurstUntilUtc`/`BurstInterval`,
  no persistence of its own (see below). `SinkCleanlinessHandler` is a
  *second* handler on `CameraCaptureCompletedEvent`, alongside
  `CameraCaptureHandler` — opt-in per camera via
  `DeviceOptions.SinkCleanliness` (null/disabled for every camera except
  the one it's configured for). Runs only on a Low-type agent
  (`AgentOptions.Type`, ADR-035/044); its own job is deciding "does this
  capture need analysis?" (device lookup, `Enabled` check only — no burst
  throttling, removed in ADR-035's follow-up once classification stopped
  competing with this process's own responsiveness for CPU) and, if so,
  publishing to `MessagingOptions.ClassifyRequestQueue` — it does no ONNX
  inference and no persistence itself. Routing is per-capability, not a
  single agent-wide address (ADR-036): SinkCleanliness and ObjectDetection
  each carry their own `ExecutingAgentId` (on `SinkCleanlinessRoiOptions`/
  `ObjectDetectionRoiOptions`), so the handler publishes up to two
  independent `ClassifyCaptureQueueMessage`s per capture, each addressed
  to that capability's own High-type agent — they can be the same agent or
  two different ones. The actual classification (`ISinkCleanlinessClassifier`,
  `IObjectDetector`) and persistence run on whichever High-type agent's
  `SinkCleanlinessWorker` each message is addressed to, reached via Cloud
  (`ClassifyRequestFunction` relays the message to `agent-classify-commands`)
  — see ADR-032/033/034/035/036. Every classification persists a `SinkCleanliness`
  `DeviceEvent`, not just transitions (a `Changed` flag in the payload
  lets Cloud's Telegram alert filter for transitions itself). Each
  handler is, informally, the reactive half of a future capability — but
  none of them are wrapped in a formal `ICapability` yet.
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
- **Azure Queue**: `AzureQueuePublisher` (`Vivnest.Core.Storage`, shared by
  Agent and Cloud), carrying `{PartitionKey, RowKey}`-only messages for
  every Agent-to-Cloud data/event queue. The exceptions carry a direct
  payload instead, since there's no persisted row to reference — see
  ADR-024: the two Cloud-to-Agent command queues, `agent-restart-commands`
  (`RestartCommandQueueMessage`: `AgentId`, `IssuedAtUtc`) and
  `agent-deploy-commands` (`DeployCommandQueueMessage`, identical shape,
  consumed not by `Vivnest.Agent` but by `Vivnest.Agent.Updater`, a
  separate standalone process deployed alongside the Agent on the host,
  never inside its container — see ADR-028 and "Deploy" below); and the
  AI-inference routing pair added by ADR-035 — a Low-type agent's
  `SinkCleanlinessHandler` publishes `ClassifyCaptureQueueMessage` to
  `classify-requests`, `ClassifyRequestFunction` relays it unchanged
  (a pure relay with no storage interaction, unlike every other queue
  function here) onto `agent-classify-commands`, which a High-type agent's
  `SinkCleanlinessWorker` polls directly instead of draining an
  in-process channel. Since ADR-036, the message carries a `Capability`
  discriminator (`SinkCleanliness`/`ObjectDetection`) and only that one
  capability's data — `SinkCleanlinessHandler` publishes up to two
  independent messages per capture, one per enabled capability, each
  addressed to that capability's own `ExecutingAgentId` instead of a
  single agent-wide address.
- **Cloud Functions** (`Vivnest.Cloud.Functions`): `CameraCapturedFunction`
  (queue-triggered, delegates to `CameraCapturedHandler`);
  `ClassifyRequestFunction` (queue-triggered on `classify-requests`,
  delegates to `ClassifyRequestHandler` — a pure relay onto
  `agent-classify-commands`, ADR-035);
  `HealthMonitorTimerFunction` (cron-triggered full sweep of every
  device *and* agent heartbeat — the only way to detect an agent gone
  silent, since an agent can't self-report that); `DeviceHeartbeatChangedFunction`
  (queue-triggered on `device-heartbeats`, near-instant reaction to one
  device's status change); `AgentHeartbeatChangedFunction`
  (queue-triggered on `agent-heartbeats`, near-instant reaction when an
  agent's first heartbeat arrives after being marked offline — recovery
  only, since an agent can't publish its own offline transition);
  `AgentEventQueueFunction` (queue-triggered on `agent-events`, ADR-093 —
  refetches the `AgentEventEntity` and branches on its `EventType`,
  structurally identical to `DeviceEventQueueFunction`). All
  three delegate to `IHealthMonitorService` (`EvaluateAndNotifyAsync` for
  devices, `EvaluateAgentAndNotifyAsync` for agents) so the
  determination/notification logic exists once per level, not once per
  trigger — see [decision-log.md](decision-log.md) ADR-005.
- **Agent health is a real tiered status, not binary** (ADR-074,
  Phase 8 Pass 1; `Healthy`/`Degraded` naming per ADR-078):
  `Vivnest.Cloud/Interfaces/IAgentStatusResolver.cs` +
  `Vivnest.Cloud/Rules/AgentStatusResolver.cs` compute
  `Healthy`/`Degraded`/`Offline`/`Unknown` from `HeartbeatInterval` ×
  `HealthMonitorOptions.AgentDegradedMultiplier`(2)/`AgentOfflineMultiplier`(5)
  — reuses `DeviceHeartbeatStatus`, the same enum `DeviceStatusResolver`
  already returns for devices, rather than a parallel vocabulary. Shared
  by `HealthMonitorService` (drives the `AgentOffline`/`AgentRecovered`
  notification, now firing at 5× instead of the old unmultiplied 1×
  threshold) and `AgentQueryService` (drives `GET /agents`), mirroring
  exactly how `IDeviceStatusResolver` is already shared between
  `HealthMonitorService` and `DeviceQueryService` for the same "can't
  silently drift apart" reason — `AgentQueryService.ToDto` used to carry
  its own hand-mirrored copy of the threshold check before this pass.
- **Config/version status on the main Agent/Device views** (ADR-075,
  Phase 8 Pass 2): `AgentSummaryDto`/`DeviceSummaryDto` (`GET /agents`,
  `GET /devices`, and their single-entity routes) now carry
  `ConfigurationStatus`, and `AgentSummaryDto` additionally carries
  `VersionStatus` — computed per row via two new lightweight entry points
  that skip the full capability-projection pipeline the original ADR-068/
  ADR-073 methods require: `IConfigurationSyncStatusService.
  GetAgentStatusFromHeartbeatAsync`/`GetDeviceStatusFromHeartbeatAsync`
  reuse the class's own existing manifest-read/comparison helpers directly
  against a heartbeat row (its `RowKey` already *is* the runtime id both
  writers stamp, no registry lookup needed); `IAgentVersionStatusService.
  GetStatusForRuntimeAgentAsync` reuses the existing
  `AgentInstallationManagementService.GetActiveImageVersionByRuntimeAgentIdAsync`
  reverse lookup rather than re-fetching a heartbeat the caller (already
  iterating `AgentHeartbeatEntity` rows) already has. Computed live per
  row, no new table — accepted cost at current scale, same reasoning
  `DeviceCapabilitiesQueryService`'s own O(N) scan already uses.
- **Lifecycle vs. operational status, kept separate** (ADR-076,
  Phase 8 Pass 3): `AgentSummaryDto`/`DeviceSummaryDto` gained
  `LifecycleStatus` (the raw Admin `AgentRegistryStatus`/
  `DeviceRegistryStatus`, resolved via one batch `IAgentRegistryStore.
  ListAsync`/`IDeviceRegistryStore.ListAsync` per list call, not a
  per-row lookup). `Status` is forced to a new 6th `DeviceHeartbeatStatus`
  value, `NotApplicable`, when lifecycle is `Inactive`/`Disabled`/
  `Retired` — the two fields are always shown side by side, never
  collapsed into one, so a Disabled device reads `NotApplicable` instead
  of a misleading `Offline` from its last (now-stale) heartbeat row.
- **Machine operational status, derived not stored** (ADR-076):
  `AgentInstallationManagementService.GetMachineOperationalStatusAsync`
  walks a Machine's active installations → each installation's Agent →
  `RuntimeAgentId` → heartbeat → `IAgentStatusResolver` (ADR-074), then
  aggregates: `Unknown` if nothing installed, `Healthy`/`Offline` only if
  every installed Agent agrees, `Degraded` for any real mix — deliberately
  *not* "one offline Agent = Machine offline." Attached to
  `MachineDto.OperationalStatus` at the Function layer
  (`MachinesFunction`), the same `with { ... }` pattern ADR-073 used for
  `AgentInstallationDto.VersionStatus`. `DeviceHeartbeatStatus` gained a
  `[JsonConverter(typeof(JsonStringEnumConverter))]` here too — this DTO
  field is the first place the enum itself (not a pre-stringified
  `entity.Status.ToString()`) is ever serialized, the same gap
  `AgentVersionStatus`/`ConfigurationSyncStatus` already hit once before.
- **Persisted operational events** (ADR-077, Phase 8 Pass 4): the offline/
  recovery transitions `HealthMonitorService` already alerts on via
  Telegram now also persist as real `AgentEvent`/`DeviceEvent` rows —
  `AgentEventTypes.AgentOffline`/`AgentRecovered`/`ConfigurationApplyFailed`,
  `DeviceEventTypes.DeviceOffline`/`DeviceRecovered` — written via a
  `AzureTableStore<DeviceEventEntity>`/`AzureTableStore<AgentEventEntity>`
  constructed directly in `HealthMonitorService`'s own constructor
  (`Vivnest.Cloud` has no `Vivnest.Infrastructure` reference, so the
  Agent-side `IAgentEventWriter`/`IDeviceEventWriter` aren't reachable
  here — mirrors `DeviceRuntimeConfigurationPublisher`'s existing raw-
  `AzureTableStore<T>` precedent instead). Gated by the same
  `NotificationState` transition as the Telegram alert — additive
  persistence, not a parallel pipeline. Deliberately no
  `DeviceEventTypes.ConfigurationApplyFailed`: `ConfigurationLoadError`
  only ever lives on the Agent's own heartbeat, so persisting it per-
  Device would fan one Agent-level failure out across every Device it
  owns. New `AgentHeartbeatEntity.LastNotifiedConfigurationLoadError`
  (`string?`) gates the `ConfigurationApplyFailed` event the same way
  `NotificationState` gates Online/Offline — fire once per distinct
  error, not every health-check tick.
- **`AzureTableStore<T>.UpdateAsync` ETag fix** (ADR-077): previously
  discarded the Azure response's new ETag, leaving the in-memory
  entity's `ETag` stale. Harmless for a single `UpdateAsync` call per
  entity per request, but a real (previously latent) bug for the two-
  updates-in-one-method case `EvaluateAgentAndNotifyAsync` now has — the
  second call's stale ETag was silently rejected (412), swallowed by the
  per-agent `try/catch` in `HealthMonitorService.RunAsync`. Fixed to
  write `response.Headers.ETag` back onto the entity.
- **Capability operational status** (ADR-078): `CapabilityServiceDto`
  (the Capabilities tab's DTO, `Vivnest.Cloud/Api/DeviceCapabilitiesQueryService.cs`)
  gained `OperationalStatus` — `Running`/`NotRunning`/`Unknown`, computed
  from the owning Agent's health (via the same `AgentSummaryDto` lookup
  the tenant-ownership check already does, cached) plus — for
  `SinkCleanliness`/`ObjectDetection` specifically —
  `DeviceHeartbeatEntity.SinkCleanlinessEnabled`/`ObjectDetectionEnabled`
  as the "runtime reports active" signal; every other capability row has
  no distinct runtime flag and falls through to `Running` whenever the
  Agent is `Healthy` and the capability is enabled. Fetched via a
  tenant-wide `GetByTenantAsync` scan filtered by `RowKey`, not a direct
  `GetAsync(partitionKey, rowKey)` point lookup — `DeviceHeartbeatEntity`'s
  PartitionKey is `TenantId|SiteId|AgentId`, and the AgentId segment isn't
  known ahead of a lookup by deviceId alone; a first attempt at the direct
  lookup silently returned null every time, found live, fixed to mirror
  `DeviceQueryService.GetDeviceAsync`'s existing scan-and-filter shape.
- **Vocabulary**: `DeviceHeartbeatStatus.Online`/`Warning` renamed to
  `Healthy`/`Degraded` (ADR-078) to match the Phase 8 spec's own wording;
  `Offline`/`Error`/`Unknown`/`NotApplicable` unchanged. Same enum, same
  meaning, every consumer above updated. Dashboard: `.status-online`/
  `.status-warning` and their `-dot`/`-badge`/`-thumbnail` variants are a
  shared color palette several unrelated features also use (API key
  enabled/disabled, `ConfigurationSyncStatus`, `CapabilityStatus`,
  `AgentInstallationStatus`, `EventSeverity`) — only the
  DeviceHeartbeatStatus-dedicated selectors were renamed outright; where a
  class was still needed under its old name by one of those other
  features, a new `-healthy`/`-degraded` selector was added alongside it
  instead, reusing the same color tokens.
- **Notification**: `Vivnest.Cloud.Notifications` —
  `INotificationDispatcher`/`NotificationDispatcher` fan a generic
  `Notification` out to every registered `INotificationChannel`.
  `TelegramNotificationChannel` is the only channel implemented today;
  `ITelegramService` is now purely the low-level Telegram API client
  behind it — nothing else calls it directly.
- **REST API** (`Vivnest.Cloud.Functions/Http`): 62 endpoints across 20
  Function classes. No longer read-only — roughly half are mutating
  (admin CRUD, config publish/rollback, the Phase 9 command routes).
  Mostly tenant-scoped via `x-api-key`, with an operator tier above it and
  three unauthenticated endpoints below it (see "REST API & Auth" below).
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

### The Agent composition root (2026-08-22)

`Program.cs` used to be ~1300 lines that loaded configuration and
registered every service. It is now a thin entry point; composition lives
in `Vivnest.Agent/Bootstrap/`:

| File | Role |
|---|---|
| `AgentBootstrap.cs` | Orders the whole sequence: config → options → logging → infrastructure → platform → capabilities |
| `AgentConfigurationLoader.cs` | All remote/local configuration loading, decryption and source ordering |
| `AgentOptionsRegistration.cs` | `IOptions<T>` binding |
| `AgentLoggingRegistration.cs` | Log providers, including the buffer that feeds log shipping |
| `AgentInfrastructureRegistration.cs` | Storage/infrastructure, the event dispatcher, and the capability host |
| `AgentPlatformRegistration.cs` | The seven unconditional `Platform*` workers (ADR-089) |
| `AgentCapabilityRegistration.cs` | Everything behind the `AgentType` switch |

Three projects were added alongside it:

- **`Vivnest.Abstraction`** — contracts only, no project references:
  `ICapability`, `ICapabilityContext`, `CapabilityManifest`,
  `IEventHandler`/`IEventDispatcher`, `ICommandHandler` and its result
  types, the log/error buffer interfaces, `INetworkUsageTracker`.
- **`Vivnest.Runtime`** — implementations of those runtime contracts:
  `EventDispatcher`, `CapabilityHost`, `CapabilityRegistry`,
  `CapabilityContext`, `CapabilityHostedService`.
- **`Vivnest.Domain`** — **currently empty**: a `.csproj` in the solution
  with no source files. Recorded because an empty project in a build is a
  question every reader will otherwise have to ask.

**`CapabilityHostedService` is the Agent's fourteenth hosted service**, and
the only one that starts other things. It starts every registered
`ICapability` in registration order and stops them in reverse. A failure
to *start* is rethrown, which stops the host — correct, because a
capability that cannot start is not a degraded Agent, it is a silently
useless one. A failure to *stop* is logged and swallowed, so one bad
shutdown cannot block the rest.

**Three of six are capabilities (as of `9012603`, 2026-08-22).** Camera,
smart plug and motion sensor run through the capability host; Home
Assistant, Tapo hub liveness and sink cleanliness are still plain hosted
services. **Two mechanisms therefore still do the same job**, and nothing
states which a new worker should use — the standing cost until the
migration finishes.

**The registration verb is load-bearing.** A migrated worker is
`AddSingleton` and started by its capability; an unmigrated one is
`AddHostedService`. Registering one *both* ways starts its loop twice —
two captures, two uploads, two `DeviceEvent` rows per tick — and the
symptom reads as a device misconfiguration rather than a DI mistake.
Verified at `9012603`: `CameraCaptureWorker`, `SmartPlugMonitorWorker` and
`MotionSensorMonitorWorker` are singletons; `HomeAssistantWorker`,
`TapoHubLivenessWorker` and `SinkCleanlinessWorker` are hosted services.
No worker is both.

**A capability-started worker is supervised explicitly (ADR-103).** The
verb also decides who observes the loop. `AddHostedService` means the
framework watches `ExecuteAsync` and applies
`BackgroundServiceExceptionBehavior` (default `StopHost`); starting a
`BackgroundService` by hand assigns `ExecuteTask` and awaits nothing, so a
fault vanishes with no log, no status change and the capability still
reporting `Running`. That happened: `CameraCaptureWorker` died at
22:26:29 on 2026-08-22 and stayed dead for 4h16m behind healthy
heartbeats, while the hosted-service workers resumed across the same gap.
All three capabilities now call `CapabilityWorkerSupervisor.Observe`
after `StartAsync`, which marks the capability `Failed` and logs at
`Error` — and deliberately does **not** stop the host: capability state
is the health signal, and a container that bounces erases the difference
between one capability dying and the Agent dying.

**The manifest is richer than a name.** `CapabilityManifest` carries
`Commands`, `ProducedEvents`, `ConsumedEvents` and `Dependencies`
alongside `Id`/`Name`/`Version`, and `ICapability` exposes a
`CapabilityStatus` (`Registered`/`Starting`/`Running`/`Stopping`/
`Stopped`/`Failed`) that `CapabilityHost` logs on every transition.
`ICapabilityContext` carries `TenantId` and `SiteId` as well as `AgentId`.
None of the descriptor collections is consumed yet — they are declared and
logged, not dispatched on.

**Two capability vocabularies now exist and nothing maps between them.**
The Agent's manifest ids are dotted and lowercase (`camera.capture`,
`motion.sensor`, `smartplug.monitor`). Cloud's capability ids are catalogue
rows in `tblCapabilities` plus the built-in constant
`AgentCommandTypes.ImageCaptureCapabilityId = "ImageCapture"`, which is
what `ExecuteCapability` authorizes and dispatches against (Flow 7).
`ICapabilityRegistry.Get(id)` — the lookup that would join them — is never
called; only `GetAll()` is, by the host. So on-demand capability execution
does **not** go through the new capability system: it still resolves to
`DeviceTriggeredEvent` exactly as before. Any future wiring has to
reconcile the two id shapes first.

**Startup order changed with this refactor.** `AddAgentInfrastructure()`
registers `CapabilityHostedService` before `AddAgentPlatform()` registers
the platform workers, and hosted services start in registration order — so
camera capture now starts *before* heartbeat, command polling and log
shipping, where platform used to start first. Verified live on 1.1.3: no
signal is lost, because the log and error buffers are singletons that
retain anything raised before their workers start. Registering the
capability host after `AddAgentPlatform()` would restore the old order.

**Every new project needs a `Dockerfile` line.** The Agent image builds
from an explicit list of `COPY` steps, not the solution file. The two new
projects were invisible to the container build until they were added, and
the failure is not reachable from `dotnet build` — the local build sees
every project on disk, so only the image build catches it.

### Configuration ownership register (5H)

One question, answered for every configuration surface: **who is
authoritative?** Anything writable from two places is a defect waiting to
be found by whoever changes the wrong one.

| Surface | Owns | Written by | Reaches the Agent via |
|---|---|---|---|
| **Host `appsettings.json`** | `AgentId`, `TenantId`, `SiteId`, `Agent:Type`, storage connection, `CloudApiBaseUrl`, `ApiKey`, credential-encryption key | the installer, on the host | mounted file; **never Cloud-writable** |
| **Image tag** | `FirmwareVersion` | the build (`build-and-push-agent.ps1`) | baked in; not configuration at all |
| **Capability catalogue** (`tblCapabilities`) | `CapabilityKey`, `CapabilityName`, `CapabilityType`, `ConfigurationSchema`, `ConfigurationSchemaVersion`, `DefaultConfiguration`, `Status` | admin, global reference data | indirectly - it defines and validates, it does not travel |
| **AgentCapability** (`tblAgentCapabilities`) | *may this Agent execute X* (`Status`), plus Agent-level `Settings` | admin, per agent+capability | `Capabilities[]` on the agent blob |
| **DeviceCapability** (`tblDeviceCapabilities`) | *how X is configured on this device*: `Settings`, `Enabled`, `ExecutingAgentId` | admin, per device+capability | `capabilities[]` on the device blob |
| **Device registry** (`tblDeviceRegistry`) | identity and connection: `Name`, `Type`, `Location`, `Brand`, `Model`, `Firmware`, `Enabled`, `ParentDeviceId`, `OwningAgentId`, `Settings` (host, credentials, RTSP) | admin, per device | device blob root |
| **Agent registry** (`tblAgentRegistry`) | `RuntimeAgentId`, agent name | admin, per agent | agent blob root |
| **Function app settings** | tables, queues, Telegram, health-monitor and retention crons | deployment | Cloud-only; never published |

**Every capability setting that exists today, and which layer owns it.**
Enumerated from the four `ICapabilityRuntimeProjector` implementations and
checked against live storage:

| Capability | Setting | Stored on | Scope | Delivered to |
|---|---|---|---|---|
| Image Capture | `ScheduleIntervalSeconds` | DeviceCapability | per device | device blob |
| Image Capture | `BurstIntervalSeconds` | DeviceCapability | per device | device blob |
| Image Capture | `BurstDurationSeconds` | DeviceCapability | per device | device blob |
| Motion Detection | `BatteryReportIntervalMinutes` | DeviceCapability | per device | device blob |
| Object Detection | `RoiLeft/Top/Right/Bottom` | DeviceCapability | per device | device blob |
| Object Detection | `ModelPath`, `ConfidenceThreshold`, `ExpectedClasses` | DeviceCapability | per device | **agent blob** |
| Sink Cleanliness | `RoiLeft/Top/Right/Bottom` | DeviceCapability | per device | device blob |
| Sink Cleanliness | `ModelPath`, `ConfidenceThreshold` | DeviceCapability | per device | **agent blob** |

**Two conclusions, both load-bearing for 5I.**

*Delivered to the agent is not the same as owned by the agent.* The model
parameters route into the executing agent's `AiClassification` document
because that is where the classifier runs - but
`AgentCapabilityContribution` is keyed by `RuntimeDeviceId`, so each device
contributes its own entry. Two cameras on one High-type agent can carry
different models, thresholds and ROIs. They are per-device values with an
agent-side destination, not agent-level configuration.

*`AgentCapability.Settings` currently holds nothing.* Every capability
setting in the system is device-scoped. The generic agent-level mechanism
is built and proven (ADR-097) and has no consumer - which is exactly the
state 5G.11 left it in deliberately, after `CaptureIntervalMinutes` was
rejected for being a per-device value wearing agent-level clothes.

**The rule that keeps it that way.** A capability's runtime configuration
lives on the **DeviceCapability** assignment when it varies per device, and
on the **AgentCapability** assignment when it is genuinely one value for
the whole Agent. Capture cadence is the first kind - which is why
`CaptureIntervalMinutes` was rejected at the Agent level (ADR-097, 5G.11).
An upload policy or storage class would be the second.

**Two `Enabled` flags exist and they are not the same switch.**
`Device.Enabled` means *this device is in service*; `DeviceCapability.Enabled`
means *this capability runs on this device*. Both are real and both are
consumed. `AgentCapability` has no `Enabled` - its only on/off is `Status`
(Active/Removed), which is why an "assigned but disabled" Agent capability
cannot be expressed (ADR-096).

**Adapter write discipline.** Each `ICapabilityConfigRuntimeAdapter` flattens
its capability entry onto the device's runtime object. Two patterns exist,
and only one is safe:

- **Own sub-object** - `ObjectDetectionRuntimeAdapter` and
  `SinkCleanlinessRuntimeAdapter` write `flattenedDevice["ObjectDetection"]`
  and `flattenedDevice["SinkCleanliness"]`. Two capabilities on one device
  cannot collide, because neither can reach the other's block.
- **Shared root fields** - `ImageCaptureRuntimeAdapter` and
  `MotionDetectionRuntimeAdapter` both write
  `flattenedDevice["LivenessInterval"]` and
  `flattenedDevice["WarningMultiplier"]`.

**RESOLVED 2026-08-22 (ADR-099): the device owns liveness.**
`LivenessIntervalSeconds` and `WarningMultiplier` are now columns on the
device row, projected onto the device wire section, and removed from both
capability adapters. `DeviceConfigRuntimeAdapter` converts seconds to the
`TimeSpan` the runtime binds, treating zero as unset.
`CapabilityAdapterIsolationTests` asserts the collision cannot return. The
description below is kept because it explains what the rule is for.

**The second pattern was a live ownership defect, latent until fixed.** A
device assigned both *Image Capture* and *Motion Detection* has two
authorities for those two fields, resolved by whichever capability appears
later in the blob's `capabilities[]` array - `DeviceConfigRuntimeAdapter`
applies adapters in array order and each overwrites the last. The two even
disagree on units and key names: `LivenessIntervalSeconds` versus
`LivenessIntervalMinutes`.

Not reachable today - verified against live data, the only multi-capability
device carries *Image Capture* and *Sink Cleanliness*, which write to
different places. It becomes reachable the first time a device is both
captured from and motion-monitored. This is exactly the
`CaptureIntervalMinutes` shape, already shipped, and 5H exists to catch it
rather than to describe it.

### Capability assignment: registry vs. configuration

Two different questions, deliberately answered by two different sources:

> **The capability registry describes what this Agent *can* do. Cloud's
> `AgentCapability` configuration describes what this Agent is *allowed and
> configured* to do.**

Neither alone starts anything. A capability starts only when it is **both**
registered in the Agent **and** enabled in the runtime configuration.

**The path, Cloud to running capability:**

1. `AgentRuntimeConfigurationProjector` reads `tblAgentCapabilities`, keeps
   `Active` assignments, resolves each against the `Capability` catalogue,
   and warns on an assignment whose definition is missing.
2. `AgentRuntimeConfigurationPublisher` writes them to the agent config blob
   as a root-level `Capabilities` array of
   `{ CapabilityId, Name, Enabled }`. **Capabilities participate in the
   content hash**, so assigning or disabling one produces a new version
   rather than a silent no-op.
3. `AgentCapabilityAssignmentFactory` binds that array into
   `RuntimeCapabilityAssignment` records.
4. `RuntimeCapabilityAssignmentStore` exposes `GetAll`/`GetEnabled`/`Get`.
5. `CapabilityHost.StartAsync` warns about every enabled assignment with no
   registered implementation, then starts the intersection of registered and
   enabled, logging `Registered capabilities: N. Enabled assignments: N.
   Capabilities selected for startup: N.`

**The selection matrix, verified against the real `CapabilityHost`,
`CapabilityRegistry` and `RuntimeCapabilityAssignmentStore`:**

| Registered | Assigned | Enabled | Result |
|---|---|---|---|
| yes | yes | yes | starts |
| yes | yes | no | does not start |
| yes | no | – | does not start |
| no | yes | yes | **warning**, nothing starts, Agent continues |
| yes ×3 | 2 of 3 | yes | exactly those 2 start |

**A missing implementation is a warning, not a failure.** An assignment for
a capability this Agent does not implement logs and is skipped, so a fleet
running mixed builds degrades rather than crash-looping. A capability that
*is* selected and then throws during `StartAsync` still brings the host
down — deliberate for now; fault isolation is a separate decision. A
fault *after* startup, inside the worker's own loop, does not: since
ADR-103 it marks that capability `Failed` and leaves the Agent and every
other capability running.

**An enabled capability with no devices is `Failed` (ADR-103).** All three
workers used to return immediately when nothing matched their device type,
leaving the capability at `Running` with nothing happening at all. The
capability now checks before starting its worker, so the status says what
is true. The check lives in the capability, not the worker — a generic
`BackgroundService` should not be inventing a `CapabilityStatus`.

**The binding is the fragile part, and it has already failed once.** The
factory must bind the section as the list it is
(`GetSection("Capabilities").Get<List<T>>()`). The original code called
`GetSection("Capabilities").Bind(options)` against an options object whose
own list property was also named `Capabilities`, so the binder looked for
`Capabilities:Capabilities`, found nothing, and produced an empty list —
no error, no warning. Downstream that is indistinguishable from "Cloud
assigned nothing", and its effect is that **every capability stops
starting**, which on a camera agent means capture silently ceases. Fixed
2026-08-22; recorded because the failure is invisible at every layer that
would normally report it.

### Baked-in platform services vs. Capability-catalog-driven behavior

Two genuinely different mechanisms decide what a given Agent process
does, and they don't overlap:

- **Baked-in, unconditional platform services** — registered in
  `Vivnest.Agent/Bootstrap/AgentPlatformRegistration.cs`, outside the
  `AgentType` branches, so every Agent process runs them regardless of `AgentType`
  (Low/High) and with zero dependency on the `Capability`/
  `DeviceCapability` catalog:
  - `PlatformAgentHeartbeatWorker` → `tblAgentHeartbeat` (liveness, `Name`,
    firmware/runtime version, `ConfigurationVersion`/`Hash` — what makes
    an Agent show Healthy/Offline in the dashboard).
  - `PlatformDeviceHeartbeatWorker` → `tblDeviceHeartbeat` (no-ops cleanly
    on a High-type agent's empty device list — a plain `foreach` over
    `IDeviceRuntimeStore.GetDevices()`).
  - `PlatformAgentMetricsWorker` → periodic CPU/memory/bytes-uploaded
    samples, persisted as `AgentEvent` rows in `tblAgentEvents`.
  - `PlatformCommandPollingWorker` (`agent-restart-commands`) and
    `PlatformAgentCommandPollingWorker` (`agent-commands` — Refresh/Apply
    Configuration, Execute Capability) — Phase 9's command lifecycle,
    ADR-079/080.
  - `PlatformLogShippingWorker` — gated by its own
    `AgentLogShippingOptions.Enabled` config flag, not by the Capability
    catalog.
  - `PlatformErrorEventWorker` (ADR-093) — drains the Error-level log
    signals `AgentLogBufferLoggerProvider` collects into `AgentEvent` rows
    (`AgentEventTypes.ErrorLogged`) and publishes `{PartitionKey, RowKey}`
    onto `agent-events`. A sibling of the log shipper: that one ships the
    whole log for a human to read later, this raises errors for something
    to react to now. It never logs at Error itself — that would feed the
    buffer it just drained.

  ADR-089 (below) gives all six a `Platform` prefix so they're
  distinguishable from Capability-driven workers by name alone when
  searching the codebase.
  - Cloud side: `HealthMonitorService` (Timer-triggered, evaluates
    staleness/Offline/Recovered from the heartbeats above),
    `DeviceEventRetentionTimerFunction`/`AgentEventRetentionTimerFunction`/
    `CommandExpiryTimerFunction`.

  None of these can be assigned, unassigned, or configured through the
  Capability system — they start the moment the process boots and keep
  running for the process's whole lifetime.
- **Capability-catalog-driven behavior** — Image Capture, Sink
  Cleanliness, Object Detection, Motion Detection: real
  `DeviceCapability` assignments with `Enabled`/`ExecutingAgentId`/
  `Settings`, published through `IDeviceRuntimeConfigurationPublisher`/
  `IAgentRuntimeConfigurationPublisher`, and gating genuinely different
  runtime behavior (a disabled or unassigned capability really does stop
  running).

`CapabilityType.System` exists in the enum (doc-commented with "Health
Monitoring" as its own example) but is purely a classification value —
as of this writing no `System`-type `Capability` record has ever been
created in this codebase, and none is needed: cataloging the
always-on services above as a `Capability` would add a toggle in the
dashboard that doesn't actually toggle anything, since nothing in
`AgentCapabilityRegistration` checks Capability assignment to decide
whether to start them.

### Runtime State

In-memory only (`DeviceRuntimeState` /
`Vivnest.Core.Devices.Stores.DeviceRuntimeStateStore`), storing transient
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

`Vivnest.Cloud.Functions/Http` — all routes under `/api`. The list below
started as the read-only tier and is still organized that way, but the API
as a whole has not been read-only since Phase 5; the mutating admin,
publishing and command tiers follow in their own subsections.

- `GET /devices`, `GET /devices/{deviceId}` — `DeviceSummaryDto` now
  includes `AgentId`/`TenantId`/`SiteId` (added for `AgentDetail`'s
  device list and the dashboard's Agent link — previously present on
  `DeviceHeartbeatEntity` but never surfaced through the API). Also
  carries `ThumbnailUrl` (camera devices only, else `null`) — a SAS URL
  for that device's *latest* capture, queried fresh per device via
  `IDeviceEventReader.GetByDeviceAsync(..., take: 1)` and fanned out with
  `Task.WhenAll` for the list endpoint, rather than denormalized onto
  `DeviceHeartbeat` the way Timezone/Brand/Model/Firmware are — the
  heartbeat only republishes on a status change (ADR-005), so a blob name
  stamped there would go stale between status changes instead of tracking
  the actual latest capture. See ADR-030. Also carries
  `SinkCleanlinessEnabled`/`ObjectDetectionEnabled` — unlike
  `ThumbnailUrl`, these *are* denormalized straight onto `DeviceHeartbeat`
  (config, not a live reading — see ADR-034's follow-up for why that's
  safe here despite ADR-030's caution against denormalizing).
- `GET /devices/{deviceId}/events?take=N`
- `GET /events?take=N` — same `DeviceEventDto[]` shape as the per-device
  route above, just across every device in the tenant (the dashboard's
  global Events tab). `DeviceEventDto` now carries `DeviceId`/`DeviceType`
  so a mixed-device feed can tell rows apart. Backed by a new
  `IDeviceEventReader.GetByTenantAsync` — `DeviceEvent` rows are
  partitioned by `DeviceId`, not tenant, so this is an unpartitioned scan
  filtered client-side on `TenantId`/`SiteId`, same shape
  `DeleteOlderThanAsync` already uses; fine at current data volume.
- `GET /devices/{deviceId}/captures?take=N` (flat cap) or
  `?date=yyyy-MM-dd&skip=N&take=N` (one day, paginated — the dashboard's
  capture gallery; `IDeviceEventReader.GetByDeviceAndDateRangeAsync` uses
  a `RowKey` range filter rather than loading the whole partition, since
  `RowKey` is already timestamp-prefixed)
- `GET /devices/{deviceId}/captures/summary?days=N` — per-day counts only,
  no SAS URLs generated, so the gallery can render every day's collapsed
  header cheaply before the user expands anything (see ADR-017)
- `GET /devices/{deviceId}/battery?take=N` — motion sensor battery/signal
  history, filtered on `DeviceEventTypes.BatteryStatus`; same shape as the
  captures endpoint, just a different `eventType` filter (see ADR-022)
- `GET /devices/{deviceId}/capabilities` — for the dashboard's
  Capabilities tab. A different data source from every other endpoint
  here: reads `device-config`/`agent-config` blobs directly
  (`DeviceCapabilitiesQueryService`), not Table Storage, into the existing
  `Vivnest.Core.Options` types the Agent already binds against
  (`DeviceOptions`, `AiClassificationOptions`). The device blob is passed
  through `DeviceConfigRuntimeAdapter.Adapt` first — the same translation
  the Agent applies at startup, moved from `Vivnest.Agent` into
  `Vivnest.Core.Configuration` so both sides share one implementation.
  That matters because the blob at the flat name holds *either* shape: the
  legacy flat `DeviceOptions` shape for any device not republished since
  ADR-064, or the `capabilities[]` wire document the publisher now writes
  to both the versioned blob and the flat name. This endpoint used to
  deserialize straight into `DeviceOptions`, which bound almost nothing on
  the second shape (blank `Name`, `Type` defaulting to `Camera`, no
  derived capabilities, no sensors, no triggers) — a silent wrong answer
  rather than an error, unnoticed because most deployed blobs are still
  legacy-shaped. Adapt passes a legacy document through untouched, so both
  shapes converge on one path.
  Tenant-scoped via `IAgentQueryService.GetAgentAsync(tenant,
  device.OwningAgentId)` rather than a device-heartbeat lookup, so a
  freshly-configured, never-heartbeated device still resolves correctly.
  Returns the device's capabilities — a fixed, canonical name (`Image
  Capture`, `Image Classification`, `Image Analysis`, `Motion Detection`,
  `Power Monitoring`, `Health Monitoring`) each carrying a `Services` list
  of the concrete device/service actually providing it (e.g. `Image
  Classification` → `SinkCleanliness`, with its `ModelPath`/
  `ConfidenceThreshold` read from the executing High-type agent's own blob) —
  every capability has exactly one service today, but the shape is
  one-to-many, not a naming layer over a 1:1 row (see ADR-041). One
  Built-in entry per native device type (`Camera`/`MotionSensing`/
  `PowerMonitoring` — a direct hardware reading, no AI model involved),
  `Image Classification`/`Image Analysis` if configured (Derived — routed
  through a High-type agent's model), and `Health Monitoring` (System) always.
  Also returns which other devices trigger this one (`TriggeredBy`,
  scanning `device-config` for matching `Trigger.DeviceIds`), and its
  hand-authored `Sensors` (`SourceSensors`, with a computed, not stored,
  `UsedByCount` — no generic capability-to-sensor graph exists). Read-only
  by decision, matching ADR-025's precedent. See ADR-040, ADR-041.
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
- `POST /agents/{agentId}/restart` — the dashboard's first mutating
  endpoint and the REST API's first write that reaches the Agent, not
  just Table Storage. As of ADR-079, routes through
  `ICommandDispatcher.DispatchAsync` rather than publishing to
  `agent-restart-commands` directly — validates ownership, persists a
  `tblAgentCommands` row (`Pending` → `Dispatched`), then publishes,
  returning the created `AgentCommandDto` (202) instead of a bare
  accepted-with-no-body response. Gated identically to
  `GET /agents/{agentId}` (403 for `DevicesOnly`, agent must resolve for
  the caller's tenant). See ADR-024, ADR-079.
- `GET /agents/{agentId}/commands` — tenant-scoped command history for
  one Agent (`AgentCommandDto[]`), dashboard-facing, authenticated the
  normal way. `GET /agents/{agentId}/commands/{commandId}` /
  `PUT /agents/{agentId}/commands/{commandId}/status` — Agent-facing
  instead: no tenant API key exists on the Agent, so `tenantId`/`siteId`
  travel explicitly (query params / JSON body) and are trusted directly,
  matching `AgentInstallationManagementService.ReportDeployCompleteAsync`'s
  established precedent for Agent-originated calls. The PUT is the
  idempotent status-transition callback (`PlatformCommandPollingWorker` calls it
  once, best-effort, to report `Received` before restarting) — a call
  against an already-terminal command (`Succeeded`/`Failed`/`Expired`/
  `Cancelled`) is a silent no-op returning the already-persisted result.
  See ADR-079.
- `POST /agents/{agentId}/refresh-config`, `POST /agents/{agentId}/apply-config`
  (ADR-080) — same `ICommandDispatcher.DispatchAsync`/202-with-`AgentCommandDto`
  shape as `/restart`. Refresh takes no body (Cloud resolves "latest
  published version" itself); Apply takes `{ConfigurationVersion: int}`
  (`ApplyConfigurationRequest`), validated against
  `AgentConfigurationEntity.CurrentVersion` before a command row is ever
  created — an out-of-range version rejects immediately
  (`VERSION_NOT_FOUND`), never reaching the Agent. Scoped to the Agent's
  own configuration only; no `TargetDeviceId` support yet.
- `POST /agents/{agentId}/execute-capability` (ADR-081) — body
  `{TargetDeviceId, CapabilityId}` (`ExecuteCapabilityRequest`). Same
  dispatch shape as the other command routes; the URL names the target
  Agent (Agent-centric, like every other command route), letting
  `CommandDispatcher.ValidateAsync`'s existing Built-in/Derived
  authorization branch do the real work. Only `CapabilityId:
  "ImageCapture"` has an Agent-side execution handler this pass — a
  correctly-authorized Derived capability reaches the Agent but reports
  back `Failed(CAPABILITY_UNAVAILABLE)`.
- `GET /agents/{agentId}/logs` — returns `AgentLogsDto {Url}`, a
  15-minute SAS read URI for `agent-logs/{agentId}.txt` (generated via
  `IBlobStorageService.GenerateReadSasUri`, same pattern as capture image
  URLs — no bytes proxied through the Function). Gated identically to the
  other `/agents*` routes. The SAS URI is generated even if the blob
  doesn't exist yet (the agent hasn't shipped logs); opening it just
  404s. See ADR-027.
- `POST /agents/{agentId}/deploy` — publishes to `agent-deploy-commands`,
  consumed by `Vivnest.Agent.Updater` (see "Deploy" below), not
  `Vivnest.Agent`. Gated identically to `/restart` — a deliberate v1
  choice, not an oversight: ADR-024 flagged Deploy as warranting stricter
  gating than Restart once built; today's single-tenant reality is why
  that wasn't done yet. See ADR-028.
- `POST /apikeys`, `GET /apikeys?tenantId=X&siteId=Y`,
  `POST /apikeys/{keyId}/revoke` — key management. `POST /apikeys`
  validates `TenantId`/`SiteId` against `ITenantStore`/`ISiteStore`
  (both must exist and be `Status: Active`) before minting a key — 400
  otherwise. Only gates creation: `ApiKeyAuthenticator` still resolves
  `TenantContext` purely from the key's own denormalized `TenantId`/
  `SiteId`, never re-checking `tblTenants`/`tblSites`, so this doesn't
  affect keys created before Tenant/Site existed. See ADR-052.

### Master-list admin endpoints

A separate, mutating tier from everything else above — the read-only
endpoints derive their responses from Table Storage/config blobs written
by the Agent/Cloud pipeline itself; these instead let a tenant directly
manage reference data. Both live in a new `Vivnest.Cloud.Admin`
namespace, distinct from the query-only services the rest of this
section uses. Neither route starts with `admin/` — Azure Functions
reserves that prefix for its own built-in admin API and rejects any
route under it at startup.

- `GET/POST capabilities-admin`, `PUT/DELETE capabilities-admin/{capabilityId}`
  — CRUD for the `Capability` master list (`CapabilityAdminDto`:
  `CapabilityId`, `CapabilityName`, `CapabilityType`). Backed by a new,
  deliberately global `CapabilityEntity`/`tblCapabilities`
  (constant `PartitionKey`, `RowKey = CapabilityId` — not tenant-scoped,
  since a capability like "Image Capture" is a fixed concept shared
  across every tenant, not owned by one). `x-api-key` auth like every
  other endpoint here, plus `DevicesOnly` → 403 on the three mutating
  routes. `CapabilityType` is `Device`/`Service`/`System` — who provides
  the capability (the device itself / a separate process / the
  platform), not how it's computed; renamed from `BuiltIn`/`Derived`/
  `System` in ADR-061 after that framing proved ambiguous in practice.
  Deliberately unrelated to the `Source` vocabulary at line ~408 below.
  See ADR-042, ADR-061.
- `GET/POST agents-registry-admin`, `PUT/DELETE agents-registry-admin/{agentId}`
  — CRUD for a tenant's **registered** agents (`AgentRegistryDto`:
  `AgentId`, `Name`, `Description`, `Status`, `FirmwareVersion`, `Type`,
  `RuntimeAgentId`, `TenantId`, `SiteId`, `CreatedUtc`, `UpdatedUtc`) —
  pre-registration (an identity to copy into a new device's
  `appsettings.json`), not live monitoring data. Backed by a new,
  tenant-scoped `AgentRegistryEntity`/`tblAgentRegistry`
  (`PartitionKey = "{TenantId}|{SiteId}"`, `RowKey = AgentId`) —
  completely separate from `tblAgentHeartbeat` and the read-only
  `/agents*` endpoints above, which stay exactly as they were, populated
  only by real Agent heartbeats. `TenantId`/`SiteId` come from the
  authenticated `TenantContext`, never the request body. See ADR-043.
  `CapabilityIds` (ADR-046's comma-separated Capability master-list ids)
  **no longer exists on this DTO or entity** — ADR-059 replaced it with
  the `AgentCapability` join (`tblAgentCapabilities`), the same move
  ADR-057 made for `Device.CapabilityIds`; see the domain-model section
  below. What this DTO carries instead is `RuntimeAgentId` (ADR-063) —
  the real Agent process's own `Agent:AgentId`, admin-typed and
  unvalidated. This is also the "Agent" domain concept from the Machine/
  Agent/AgentInstallation spec — `Description`/`Status`
  (`AgentStatus`: `Active`/`Inactive`)/`CreatedUtc`/`UpdatedUtc` were
  added directly to this entity rather than a parallel `tblAgents`, and
  it deliberately carries no `CurrentMachineId`/`CurrentInstallationId` —
  "where is this agent installed" is answered by querying
  `AgentInstallation` (`GetActiveByAgentAsync`), not a denormalized field
  that could drift. Rows that predate this field read `Status` as blank
  and `CreatedUtc`/`UpdatedUtc` as the `DateTime` epoch default — both
  tolerated, not backfilled, except that `UpdateAsync` now normalizes
  `CreatedUtc`'s `Kind` to `Utc` (or backfills it from `UpdatedUtc` if
  still default) before every write, since the Azure Table SDK rejects
  `DateTimeKind.Unspecified` outright. See ADR-053.
- `GET/POST device-types-admin`, `PUT/DELETE device-types-admin/{deviceTypeId}`
  — CRUD for the `Device Type` master list (`DeviceTypeAdminDto`:
  `DeviceTypeId`, `DeviceTypeName`). Structurally identical to the
  Capability master list (global `DeviceTypeEntity`/`tblDeviceTypes`,
  constant `PartitionKey`) — deliberately unrelated to
  `Vivnest.Core.Enums.DeviceType`, the fixed enum real Agent code
  branches on (`CameraCaptureWorker`/`MotionSensorMonitorService`/
  `SmartPlugMonitorService` are each hardcoded to one value); new entries
  here have no code behind them until a matching Agent capability is
  built. A deliberate exception to this project's usual "reuse the fixed
  enum" rule for this kind of classification — see ADR-047.
- `GET/POST devices-registry-admin`, `PUT/DELETE devices-registry-admin/{deviceId}`
  — CRUD for a tenant's **registered** devices (`DeviceRegistryDto`:
  `DeviceId`, `Name`, `DeviceTypeId`, `OwningAgentId`, `Location`,
  `Brand`, `Model`, `Firmware`, `Status`, `RuntimeDeviceId`, `Settings`,
  `TenantId`, `SiteId`; `Enabled` became `Status`
  (`DeviceStatus`) and `CapabilityIds` was dropped for the
  `DeviceCapability` join in ADR-057) — declared identity plus the fields
  `DeviceOptions.cs` itself calls "purely descriptive", not the real
  device-config blob workflow, which is completely unchanged. Backed by a
  new, tenant-scoped `DeviceRegistryEntity`/`tblDeviceRegistry`. `Settings`
  is a JSON-serialized string→string map for connection facts (`Host`,
  `Username`, `RtspUsername`, `MACAddress`, `ChildDeviceId`, …) — it also
  accepts credentials (`Password`, `RtspPassword`, tokens) by direct
  request (ADR-050), a real departure from every other credential in this
  codebase, which stays local-only in the device's `*.secrets.json` file
  (ADR-038): anything entered here is stored and returned as plain text
  by the admin API to any caller with a valid tenant `x-api-key`. See
  ADR-048/050.

Both follow the same `AzureTableStore<T>` pattern as the rest of the
codebase, using its new `DeleteAsync(partitionKey, rowKey, ct)` method —
the first hard-delete capability added to that store.

### Tenant/Site foundation

A separate, operator-only tier below everything above — `TenantsFunction`/
`SitesFunction` (`Vivnest.Cloud.Functions`, root namespace) manage the
`Tenant`/`Site` records that every tenant-scoped entity's `TenantId`/
`SiteId` already implicitly assumes exist, but which had no explicit
model anywhere in the codebase until now:

- `GET/POST tenants`, `GET/PUT tenants/{tenantId}` — CRUD for the global
  `Tenant` master list (`TenantDto`: `TenantId`, `Name`, `Description`,
  `Status`, `CreatedUtc`, `UpdatedUtc`). `TenantId` is a generated Guid
  (ADR-055 — changed from an earlier caller-chosen-id design; see that
  ADR for why) — `POST` never 409s on `TenantId` collision, since one
  can't happen. Backed by `TenantEntity`/`tblTenants` (constant
  `PartitionKey = "TENANT"`, `RowKey = TenantId`).
- `GET/POST tenants/{tenantId}/sites`, `GET/PUT
  tenants/{tenantId}/sites/{siteId}` — CRUD for a Tenant's Sites
  (`SiteDto`: `TenantId`, `SiteId`, `Name`, `Description`, `Status`,
  `CreatedUtc`, `UpdatedUtc`). `SiteId` is also a generated Guid
  (ADR-055) — `POST` 409s only if the parent Tenant doesn't exist, not
  for a `SiteId` collision. Backed by `SiteEntity`/`tblSites` —
  `PartitionKey = TenantId`, `RowKey = SiteId`, a partitioning shape
  unique to this entity (neither the global-constant pattern nor the
  `"{TenantId}|{SiteId}"` composite-key pattern used elsewhere), chosen so
  "list every Site under Tenant X" is a single partition-scoped query.
- Both use `AuthorizationLevel.Function` (the operator tier below), not
  the tenant-scoped `x-api-key` every other admin endpoint above uses —
  a tenant key is scoped to one Tenant/Site, so accepting one here would
  let any tenant list or create every other tenant.
- Neither store exposes `DeleteAsync` — a Tenant/Site is the ownership
  boundary other data scopes under, not disposable reference data. Both
  `DELETE tenants/{tenantId}` and
  `DELETE tenants/{tenantId}/sites/{siteId}` exist, but are a soft
  delete only — a thin wrapper that flips `Status` to `Inactive`
  (`DeactivateAsync`, same effect as `PUT .../{id}` with
  `{"Status": "Inactive"}`), not a row removal.
- New domain layer, `Vivnest.Core.Domain` — `Tenant`/`Site` are
  persistence-agnostic classes (private setters, a validating
  constructor, a `Rehydrate(...)` factory for reconstructing from
  storage) that `TenantManagementService`/`SiteManagementService` map
  to/from their entities, a level of indirection no prior admin feature
  had (they map DTO ↔ entity directly). Also introduces `ISiteScoped`
  (now implemented by `BaseEntity`) and `SiteScope` (a `{TenantId,
  SiteId}` struct with a `PartitionKey` computed property) — `SiteScope`
  replaces the ~6 places that used to hand-roll `$"{TenantId}|{SiteId}"`
  independently: `AgentHeartbeatWriter`/`DeviceHeartbeatWriter`
  (`Vivnest.Infrastructure`, Agent-side), `HealthMonitorService`/
  `DeviceQueryService`/`AgentRegistryManagementService`/
  `DeviceService` (Cloud-side). `DeviceHeartbeatEntity`'s
  3-part key (`"{TenantId}|{SiteId}|{AgentId}"`) composes
  `SiteScope.PartitionKey` with a trailing `|{AgentId}` rather than using
  it alone.
- No dashboard admin screen exists for Tenant/Site yet — unlike
  Capability/AgentRegistry/DeviceType/DeviceRegistry, none was requested.
  See ADR-051.

Auth is now **four tiers, not two** — ADR-012 established the first two;
Phases 7 and 9 added the other two without this section being updated:

- **Operator tier** (`AuthorizationLevel.Function`, an Azure Functions host
  key): the three `/apikeys` endpoints, plus `TenantsFunction`/
  `SitesFunction` (see "Tenant/Site foundation" below). A tenant key can
  never see or revoke other keys, or list/create other tenants.
- **Tenant tier** (`x-api-key` header): every other endpoint — the read
  routes, `/whoami`, *and* every mutating admin/publish/command route
  added since ADR-012. Resolved by `IApiKeyAuthenticator` (unsalted
  SHA-256 → `tblApiKeys` partition-key lookup) → `TenantContext
  {TenantId, SiteId, DevicesOnly}`. `DevicesOnly` keys get 403 from
  `/agents*` and every admin route — enforced server-side on the endpoint
  itself (60+ explicit checks), not just hidden in the dashboard UI.
  `DevicesOnly` is still the *only* authorization dimension: there is no
  user identity and no role model, which is why `AgentCommand.RequestedBy`
  is the hard-coded literal `"Dashboard"`.
- **Install-token tier** (`InstallToken` in the request body):
  `POST agent-installations-admin/register` only. 32 random bytes,
  hash-stored, single-use, 24-hour lifetime (`InstallTokenService`,
  ADR-072). The caller is a brand-new Updater that has no tenant key yet.
- **Agent tier** (`x-api-key` holding an *agent* key): the two Agent-facing
  command callbacks, `GET agents/{agentId}/commands/{commandId}` and
  `PUT agents/{agentId}/commands/{commandId}/status`. An agent key is an
  ordinary `tblApiKeys` row with `AgentId` set to one RuntimeAgentId,
  minted by `RegisterAsync` and returned once, which the Updater writes
  into the Agent's `appsettings.json` as `Agent:ApiKey`. The two tiers are
  mutually exclusive by design: `ApiFunctionBase.AuthenticateAsync`
  (used by all 16 other Function classes) **rejects** a key with an
  AgentId, and `AuthenticateAgentAsync` accepts *only* those - otherwise a
  key minted for one Agent would authenticate against `/devices`,
  `/agents` and every admin route, handing each Agent a full tenant
  credential. These routes no longer trust the `tenantId`/`siteId` the
  caller supplies; they use the ones on the authenticated key.
  Rollout is staged by `AgentAuth:RequireApiKey` (default `false`): an
  Agent with no key is still honoured on its supplied ids but logged by
  name, so agents predating agent keys keep working and are visible.
  An existing Agent is keyed without redeploying via
  `POST agents-registry-admin/{agentId}/issue-key` — registration is the
  normal path but needs an install token and a full container recreate
  through the Updater, which is far too heavy for "this Agent predates
  agent keys". The route resolves the admin AgentId to its RuntimeAgentId
  (the key must bind to what the Agent actually sends), revokes any
  previous key for that Agent, and returns the new one once; re-issuing is
  therefore also rotation. Migration is: call it, paste the key in as
  `Agent:ApiKey`, restart the Agent, watch the warnings stop, then set
  `AgentAuth:RequireApiKey` to `true`.
- **No tier at all**: one endpoint remains -
  `POST agent-installations-admin/{installationId}/deploy-complete`, which
  runs before any agent key exists and so needs its own mechanism; see
  "Known gaps" below.

Capture image URLs are read-only SAS URIs
(`AzureBlobStorageClient.GenerateReadSasUri`, **24 hours** —
`DeviceQueryService.ImageUrlValidFor`; the 15-minute figure this doc used
to quote is now only the agent-log SAS, `AgentsFunction.LogsUrlValidFor`), generated
inline by `DeviceQueryService` when building a capture's response — not a
proxy download through the Function, and not a separately-stored
thumbnail (the dashboard displays the same full-resolution image scaled
down via CSS; see roadmap.md Sprint 5 for why a real thumbnail pipeline
isn't built yet). `DeviceSummaryDto.ThumbnailUrl` (ADR-030) is the same
kind of URL, just pointed at a camera device's latest capture instead of
a specific one requested by the gallery — still the full-resolution
image, still no resize/optimization step.

Capture blobs are uploaded with `Cache-Control: public, max-age=31536000,
immutable` (`AzureBlobStorage.CaptureHeaders`, `Vivnest.Infrastructure`) —
safe since each capture gets its own unique, timestamp-named blob that's
never overwritten. The same directive is also applied as a SAS
response-header override (`GenerateReadSasUri`'s `cacheControl`
parameter) so it covers blobs uploaded before this existed too, not just
new ones. This does **not** currently make repeat page loads cache-hit,
though — `DeviceQueryService` generates a fresh SAS (new signature, new
query string) on every API call, so the URL itself changes each time even
though its content wouldn't. See ADR-029 for the full reasoning and why
that gap wasn't closed yet. `AgentLogBlob`'s SAS (ADR-027) deliberately
does *not* get a cache-control override — that blob's content changes
each time `PlatformLogShippingWorker` flushes.

### Machine / Agent Installation foundation

Same tenant `x-api-key`/`DevicesOnly` tier as the master-list admin
endpoints above (`MachinesFunction`/`AgentInstallationsFunction`,
`Vivnest.Cloud.Functions/Http`) — three identities the spec this
implements separates deliberately: `AgentId` (WHO — the existing
`AgentRegistryEntity`, extended, see above), `MachineId` (WHERE — new),
`InstallationId` (WHICH DEPLOYMENT — new, links the two with history).
`ContainerId` is explicitly *not* a domain identity — it's ephemeral
Docker runtime state, carried only as a free-text field on an
installation record.

- `GET/POST machines-admin`, `PUT machines-admin/{machineId}` — CRUD for
  the physical/virtual host a Vivnest Agent runs on (`MachineDto`:
  `MachineId`, `Name`, `Hostname?`, `Description?`, `Status`,
  `OperatingSystem?`, `Architecture?`, `CreatedUtc`, `UpdatedUtc`,
  `TenantId`, `SiteId`). Backed by tenant-scoped `MachineEntity`/
  `tblMachines` (`PartitionKey = "{TenantId}|{SiteId}"`, `RowKey =
  MachineId`). `MachineId` is a generated Guid — same convention as
  `AgentRegistryDto.AgentId`, not `Tenant`/`Site`'s caller-chosen-id
  pattern (`POST` never 409s on `MachineId` collision, since one can't
  happen). `MachineStatus`: `Active`/`Offline`/`Retired`/`Decommissioned`
  — no `DELETE`; retiring hardware sets `Status: Retired`/`Decommissioned`,
  the id is never reused for different physical hardware.
- `POST agent-installations-admin/install`,
  `POST agent-installations-admin/move`,
  `POST agent-installations-admin/uninstall`,
  `GET agent-installations-admin/by-agent/{agentId}`,
  `GET agent-installations-admin/by-machine/{machineId}`,
  `GET agent-installations-admin/active-by-agent/{agentId}`,
  `GET agent-installations-admin/active-by-machine/{machineId}` —
  lifecycle for a specific deployment of an Agent onto a Machine
  (`AgentInstallationDto`: `InstallationId`, `AgentId`, `MachineId`,
  `ContainerId?`, `ImageName?`, `ImageVersion?`, `Status`,
  `InstalledUtc`, `RemovedUtc?`, `UpdatedUtc`, `TenantId`, `SiteId`,
  `VersionStatus?` — see below, ADR-073).
  Backed by tenant-scoped `AgentInstallationEntity`/
  `tblAgentInstallations` (`RowKey = InstallationId`, a generated Guid —
  unlike Machine, an installation isn't operator-named, it's the record
  of a lifecycle action). `AgentInstallationStatus` is a real
  provisioning lifecycle as of ADR-071 — `Pending` → `Installing` →
  `Installed` → `Active`, `Updating` as a re-entry from `Active` for a
  version bump, `Decommissioned` terminal (renamed from `Removed`; same
  meaning). Deliberately does not add a stored `Offline` value —
  Online/Offline stays exactly as it already was, computed live from
  heartbeat staleness by `HealthMonitorService`, never a stored
  installation status. No separate `tblMachineAgents` relationship table
  — "agents on Machine X" / "an Agent's installation history" are both
  partition-scoped queries over `tblAgentInstallations` alone, filtered
  client-side on `AgentId`/`MachineId`/`Status` (same shape
  `AzureTableDeviceEventReader`'s tenant-wide queries already use) —
  "active" in `GetActiveByAgentAsync`/`GetActiveByMachineAsync` means
  "not Decommissioned" as of ADR-071 (was literally `Status == "Active"`,
  a real bug once `Pending` became the default status for a brand-new
  installation — see ADR-071's writeup).
  `AgentInstallationManagementService` (`Vivnest.Cloud.Admin`)
  orchestrates the three lifecycle actions — `Install` validates the
  Agent and Machine both exist and that the Agent has no existing active
  installation (409 otherwise, enforcing "at most one active installation
  per Agent" at creation time, not via a table constraint), creates the
  new installation `Pending`, and issues a one-time install token (see
  below); `Move` retires the current active installation to
  `Decommissioned` (if any) and creates a new `Pending` one on the new
  Machine with its own fresh token — installation history is preserved,
  never mutated; `Uninstall` marks the active installation
  `Decommissioned` (404 if there wasn't one).
- **Install tokens** (ADR-071): a short-lived (24h), single-use credential
  a not-yet-trusted process can present to register a specific Pending
  installation. New `AgentInstallationTokenEntity`/
  `tblAgentInstallationTokens` mirrors `ApiKeyEntity`'s shape exactly —
  `PartitionKey` is the token's own SHA-256 hash (`ApiKeyHasher.Hash`,
  reused directly), never the raw value, giving an O(1) lookup with no
  tenant context needed — the token itself is the trust. New
  `IInstallTokenService`/`InstallTokenService`
  (`Vivnest.Cloud/Auth/`) mirrors `ApiKeyManagementService.CreateAsync` —
  the raw token is returned exactly once, in the new
  `AgentInstallationCreationResult` DTO (`Installation`/`InstallToken`/
  `InstallTokenExpiresUtc`) that `InstallAsync`/`MoveAsync` now return,
  same one-time-reveal convention `CreateApiKeyResponse` established.
- **Self-registration** (ADR-072): `POST agent-installations-admin/register`
  is the only route in this codebase with no tenant `x-api-key` check at
  all — the install token itself (validated by
  `IInstallTokenService.ValidateAndConsumeAsync`, single-use, checked
  against expiry) is the entire trust model, since the caller
  (`Vivnest.Agent.Updater`, on a fresh Machine) has no tenant identity
  yet. Resolves the `Pending` installation from the token, assigns a
  fresh `RuntimeAgentId` (reusing an existing one instead, for Move onto
  replacement hardware for an already-registered Agent), transitions the
  installation to `Installing`, enqueues a real deploy command, and
  returns `RuntimeAgentId`/`ImageVersion`/`StorageConnectionString` so
  the operator never hand-types any of it. `Vivnest.Agent.Updater` gained
  `--installtoken <token> --registrationurl <url>`: calls this endpoint
  before the host even builds, writes the assigned identity into both its
  own `updater.settings.json` and the local `appsettings.json` it mounts
  into the Agent container, deploys immediately (try/catch-guarded — a
  transient failure here falls through to normal queue-polling rather
  than crashing the whole process, since the identical deploy command is
  already queued and will retry), and reports `deploy-complete` back
  (also no tenant key, best-effort). An optional companion flag,
  `--credentialencryptionkey <key>` (ADR-091), writes
  `CredentialEncryption:Key` into that same `appsettings.json` — the one
  field `RegisterInstallationResponse` can never supply, since it
  deliberately never travels through the response or the shared-config
  blob it decrypts (same reasoning as the ACR credentials below); without
  it, any encrypted field in the shared config (e.g.
  `Messaging:ConnectionString`) arrives as undecryptable ciphertext and
  the Agent crashes at startup. Can also be supplied standalone, without
  `--installtoken`, to patch an already-registered agent's
  `appsettings.json` in place.
- **Heartbeat-driven activation** (ADR-072): `HealthMonitorService`
  gained a best-effort hook (`AgentInstallationManagementService.NoteAgentHeartbeatAsync`,
  via a new `IAgentRegistryStore.GetByRuntimeAgentIdAsync` reverse
  lookup) that collapses an installation straight from
  `Installing`/`Installed`/`Updating` to `Active` on any real heartbeat —
  a heartbeat is unambiguous proof the container is running regardless of
  which sub-state preceded it, making the lifecycle self-healing against
  a missed `deploy-complete` callback rather than fragile to one.
- **Still purely declarative on the Install/Move/Uninstall side itself**
  — those three actions never call Docker directly, only ever through the
  existing queue (`IAgentCommandPublisher`).
- **Real image-tag versioning** (ADR-073): `DeployCommandQueueMessage`
  carries `string? ImageVersion` (`null` = `:latest`, backward compatible
  with any in-flight message); `AgentDeployer.DeployAsync` takes an
  optional tag and builds `{Registry}/{ImageName}:{tag ?? "latest"}`
  instead of always pulling `:latest`. Both places that enqueue a deploy
  resolve the tag from the target's active `AgentInstallation.
  ImageVersion` first — the registration endpoint already had it in hand;
  the pre-existing `POST agents/{agentId}/deploy` (`AgentsFunction`,
  which operates in the **RuntimeAgentId** identity space, not the admin
  AgentId `AgentInstallation` is keyed by) needed a new
  `AgentInstallationManagementService.GetActiveImageVersionByRuntimeAgentIdAsync`
  to reverse-resolve through `IAgentRegistryStore.GetByRuntimeAgentIdAsync`
  first. `scripts/build-and-push-agent.ps1` gained `-Version <tag>` —
  when given, tags/pushes both `vivnest-agent:$Version` and `:latest` and
  bakes `$Version` into `FirmwareVersion`; omitted, today's SHA-`:latest`
  behavior is unchanged.
- **Version-status computation** (ADR-073): new `AgentVersionStatus` enum
  (`NeverDeployed`/`Unknown`/`UpToDate`/`Outdated`) and
  `IAgentVersionStatusService`/`AgentVersionStatusService` mirror
  `ConfigurationSyncStatusService`'s own shape exactly — compares
  `AgentInstallation.ImageVersion` (Desired) against the latest
  `AgentHeartbeat.FirmwareVersion` (Running) via exact
  `StringComparison.Ordinal`, deliberately not semver-aware (a pre-ADR-073
  git-SHA build legitimately isn't the same thing as a real semver
  `ImageVersion` — that's a real `Outdated`/`Unknown`, not a bug to
  special-case). Attached to `AgentInstallationDto.VersionStatus` at the
  Function layer (`AgentInstallationsFunction`'s four read routes), the
  same `with { ... }` pattern `AgentRegistryAdminFunction` already uses
  for `SyncStatus`.
- **Dashboard surfacing** (ADR-073, `AgentInstallationsAdmin.tsx`): the
  status badge now shows the real lifecycle value
  (`Pending`/`Installing`/`Installed`/`Updating`/`Active`/`Decommissioned`,
  color-mapped — `status-online` only for `Active`), a Desired/Running
  version line renders under each row when `VersionStatus` is present,
  and Install/Move responses no longer discard `installToken` — a
  one-time reveal dialog (mirrors `ApiKeysAdmin`'s `createdKey` box) shows
  it immediately after a successful Install or Move.

See ADR-053, ADR-056, ADR-071, ADR-072, ADR-073.

### Command & Control (Phase 9)

The first phase where Admin can actively *affect* running Agents, not
just observe them. `Admin → Command → Agent → Handler/Capability →
Event`. Command **state** lives entirely in a new tenant-scoped
`tblAgentCommands` (`PartitionKey = "{TenantId}|{SiteId}"`, `RowKey =
CommandId`, a generated Guid) — the delivery queue is a thin envelope
only, mirroring `AgentInstallationEntity`/
`AgentInstallationManagementService`'s established
persist-then-orchestrate split. `AgentCommandStatus`: `Pending` →
`Dispatched` → `Received` → `Executing` → one of `Succeeded`/`Failed`/
`Expired`/`Cancelled` (`Cancelled` is an enum value only this pass, not
wired to any action).

- **`ICommandDispatcher`/`CommandDispatcher`** (`Vivnest.Cloud/Admin`) is
  the single write path: validates the target Agent (and, for command
  types that carry one, the target Device/Capability's ownership) exists
  for the caller's tenant, rejects a new *disruptive* command
  (`RestartAgent`, `ApplyConfiguration`) if the Agent already has one
  `Received`/`Executing`, persists the command exactly once (reflecting
  whichever terminal-for-this-request state applies — a validation
  rejection persists straight to `Failed`, a successful enqueue persists
  as `Dispatched` — deliberately never Create-then-Update on the same
  row, to avoid re-triggering the stale-ETag class of bug ADR-077 fixed
  for a different call site).
- **Delivery is split by consumer, not by command type** (ADR-024's
  actual rule, re-confirmed by reading it before this pass): `RestartAgent`
  stays on the existing `agent-restart-commands` queue/
  `PlatformCommandPollingWorker`, untouched in shape — a deliberate
  risk-avoidance choice, since that path already has one documented
  production incident attached to it and this pass adds four new moving
  parts elsewhere. `RefreshConfiguration`/`ApplyConfiguration`/
  `ExecuteCapability` (Pass 2/3, not yet built) will share one new queue,
  `agent-commands` (`AgentCommandQueueMessage(CommandId, AgentId,
  CommandType)` — the Agent fetches full detail via
  `GET /agents/{agentId}/commands/{commandId}` before executing), since
  all three will share the same consumer (a not-yet-built
  `AgentCommandPollingWorker`).
- **`PlatformCommandPollingWorker`** (`Vivnest.Agent/Runtime/Shell`, unchanged in
  shape) now makes one best-effort HTTP callback — `PUT
  .../commands/{commandId}/status {status:"Received"}` — right before
  `_lifetime.StopApplication()`. Failure here is logged and swallowed,
  never blocks the restart: completion is confirmed a different way
  regardless (see below), so a missed `Received` checkpoint just means
  one intermediate status never shows up, not a stuck command.
- **Completion is confirmed via the next heartbeat, never self-reported**
  — the process dies before it could report its own success. A
  best-effort hook (`IAgentCommandManagementService.EvaluateAgentCommandsAsync`)
  sits right next to `AgentInstallationManagementService.NoteAgentHeartbeatAsync`'s
  existing call inside `HealthMonitorService.EvaluateAgentAndNotifyAsync`
  — near-instant, not timer-bound, since that method already fires both
  per-tick and immediately per-heartbeat. For `RestartAgent`: any
  `Dispatched`/`Received` command for that Agent with the heartbeat's
  `StartedUtc` newer than the command's `DispatchedUtc` is marked
  `Succeeded` — a fresh process genuinely started after the command went
  out. (Pass 2/3 will extend this hook: Refresh/Apply additionally
  require the heartbeat's own `ConfigurationVersion`/`Hash` to match;
  ExecuteCapability branches the opposite way — a fresh `StartedUtc`
  while still `Received`/`Executing` means an unexpected crash, not
  success, since that command type is never supposed to cause a
  restart.)
- **A dedicated `CommandExpiryTimerFunction`/`ICommandExpiryService`**
  (`Vivnest.Cloud.Functions/Timer`, `Vivnest.Cloud/Services`) mirrors the
  existing `AgentEventRetentionTimerFunction` pattern exactly — a
  standalone timer, not piggybacked onto `HealthMonitorService`'s already
  tightly-scoped job. Full unpartitioned `GetAllAsync()` scan (same
  accepted-scale shape `HealthMonitorService.RunAsync` already uses),
  flips anything still `Pending`/`Dispatched`/`Received`/`Executing` past
  its `ExpiresUtc` (default 5 minutes from creation) to `Expired`.
- **Idempotency is Cloud-authoritative**: the status-transition PUT only
  applies if the command's current persisted status isn't already
  terminal (`Succeeded`/`Failed`/`Expired`/`Cancelled`) — a duplicate or
  late-arriving call is a silent no-op returning the already-persisted
  result. This is the guard that actually matters; it's what survives an
  Agent restart, unlike any in-process dedup.
- **`RequestedBy` is a hardcoded literal** (`"Dashboard"`, set in
  `AgentsFunction`) — no per-user identity exists in this codebase yet
  (ADR-012), not a fabricated user system.
- `AgentOptions.CloudApiBaseUrl` (new, `Vivnest.Agent`) — the first time
  the Agent itself needs an HTTP base URL back to Cloud Functions; every
  prior Agent-to-Cloud interaction went through Storage Queues/Tables
  directly.

`RestartAgent` (Pass 1) is routed through `CommandDispatcher` instead of
a direct `IAgentCommandPublisher.PublishRestartCommandAsync` call from
`AgentsFunction` — verified live against a real running `Vivnest.Agent`
process: `Pending → Dispatched → Received` (the pre-restart process's
callback) `→ Succeeded` (confirmed only once a genuinely new, post-restart
process's first heartbeat arrived with a newer `StartedUtc`) — and the
expiry sweep, verified against a hand-crafted already-expired row.

**`RefreshConfiguration`/`ApplyConfiguration` (Pass 2)** are the first
genuinely new command handlers — not live config hot-reload (the Agent
has none), the same download-then-restart-to-adopt pattern
`RestartAgent` already uses, just with a real "is this actually
different" check first. Both normalize to one Cloud-computed payload,
`AgentConfigCommandPayload{TargetVersion}` (`Vivnest.Core/Constants`,
a genuine Agent/Cloud shared wire type): `RefreshConfiguration` resolves
`TargetVersion` from whatever's currently published
(`AgentConfigurationEntity.CurrentVersion`); `ApplyConfiguration` takes
an explicit `ConfigurationVersion` from the caller
(`POST agents/{agentId}/apply-config`, body `{ConfigurationVersion}`),
validated against that same `CurrentVersion` before a command row is
ever created (reusing `RollbackAsync`'s own "does version N exist"
precedent — `1..CurrentVersion` is exactly the set of versions that
exist, versions are never deleted). Scoped to the Agent's own
configuration only this pass — `ApplyConfiguration`'s `TargetDeviceId`
support is mechanically feasible (`IDeviceRuntimeStore.GetDevices(id)`
already exposes a device's own `ConfigurationVersion`) but deferred,
since confirming it would need the completion hook to also read a
`DeviceHeartbeatEntity`, not just the `AgentHeartbeatEntity` it has
today. Agent-side, one shared `ConfigVersionCommandHandlerBase`
(`Vivnest.Agent/Runtime/Commands`) does the real work for both command
types (they're identical once Cloud normalizes the payload): compare
`TargetVersion` against `AgentConfigMetadataOptions.ConfigurationVersion`
(already bound from whatever config loaded at startup) — equal →
`Succeeded` immediately, no restart; different → confirm the target
version's blob is real → `Executing` → restart. New `ICommandHandler`/
`AgentCommandPollingWorker` (`Vivnest.Agent`) is a deliberate sibling to
`IEventHandler<T>`/`EventDispatcher` — string-keyed by `CommandType`
rather than CLR-generic-keyed, one handler per type rather than
`EventDispatcher`'s intentional many-per-type — polling the shared
`agent-commands` queue built (but unused) in Pass 1, fetching full
command detail via `GET /agents/{agentId}/commands/{commandId}` before
dispatching, since the queue envelope alone doesn't carry the payload.
Verified live end-to-end: a real `ApplyConfiguration` rejected against
a never-published Agent (`VERSION_NOT_FOUND`, never dispatched); then,
with hand-crafted-but-realistic version data, a real version mismatch
correctly triggered a restart, and — after a real bug was found and
fixed (the completion hook's status filter only checked `Dispatched`/
`Received`, missing the `Executing` status these two command types
report that `RestartAgent` never did) — reached `Succeeded` once the
post-restart heartbeat's `ConfigurationVersion` matched; a second
`RefreshConfiguration` at the same version succeeded immediately with
no restart, confirming the no-op path independently.

**`ExecuteCapability` (Pass 3), scoped to `ImageCapture` only** — reuses
the motion-triggered-capture path verbatim: the new
`ExecuteCapabilityCommandHandler` (`Vivnest.Agent/Runtime/Commands`)
publishes `DeviceTriggeredEvent(deviceId, DeviceType.Camera, "Command",
now)` via the Agent's existing `IEventDispatcher`; the unchanged,
already-registered `CaptureOnTriggerHandler` (built for motion bursts)
does the real work, flowing into the same `CameraCaptureCompletedEvent`
→ persisted `DeviceEvent: CameraCaptured` pipeline every other capture
already uses. Reports `Succeeded` optimistically right after publishing
— actual completion is confirmed separately via that `DeviceEvent`, not
the command's own status. Any other `CapabilityId` reaching the
handler — correctly authorized (a real `DeviceCapability` assignment
exists), but nothing built to execute it yet — reports
`Failed(CAPABILITY_UNAVAILABLE)`. New
`POST /agents/{agentId}/execute-capability` (body `{TargetDeviceId,
CapabilityId}`) is Agent-centric like the other command routes, letting
`CommandDispatcher.ValidateAsync`'s existing authorization branch (built
in Pass 1, exercised for the first time here) do the real work
unchanged. The completion hook gained the opposite rule from
Refresh/Apply's: a heartbeat with a newer `StartedUtc` while
`ExecuteCapability` is still `Received`/`Executing` means an unexpected
crash (this command type never restarts on its own), marked
`Failed(AGENT_RESTARTED)` immediately rather than `Succeeded`.

**A real bug, found live, in Pass 1 code**: `DeviceCapability.ExecutingAgentId`
lives in the *admin* AgentId identity space (validated at assignment
time against `IAgentRegistryStore`), but `CommandDispatcher`'s
`targetAgentId` is always a *RuntimeAgentId* — comparing them directly,
as the original Pass 1 code did, could never match for any real,
correctly-assigned capability. Fixed by reverse-resolving `targetAgentId`
to its admin AgentId via `IAgentRegistryStore.GetByRuntimeAgentIdAsync`
(the same identity-space-crossing lookup `AgentQueryService` already
uses) before comparing against `assignment.ExecutingAgentId`.

Verified live end-to-end against the real running Agent and its real
Tapo C120 Camera: a genuine `ImageCapture` produced both a `Succeeded`
command and a real new `DeviceEvent: CameraCaptured`; `WRONG_AGENT`
rejected against a device owned by a different real Agent; the identity-
space fix verified in both directions (mismatched `ExecutingAgentId` →
`WRONG_EXECUTING_AGENT`; matching → passes validation, Agent reports
`CAPABILITY_UNAVAILABLE`); `AGENT_RESTARTED` verified by hand-crafting a
`Received` command dispatched before the Agent's real `StartedUtc`.

**Reliability (Pass 4)** — both Agent-side polling workers now check
whether Cloud still considers a fetched/queued command live *before*
acting on it, not just after (the existing Cloud-side idempotency guard
only protects the recorded status from a stale update, it never stopped
the Agent from re-running a real side effect for a command already
`Expired` or otherwise terminal). `AgentCommandPollingWorker`
(Refresh/Apply/ExecuteCapability) checks the `Status`/`ExpiresUtc` it
already fetches, before ever reporting `Received`. `PlatformCommandPollingWorker`
(RestartAgent) gained one extra best-effort `GET` to the same command
endpoint right before restarting, failing open (restarts anyway) on any
check failure. Verified live: a command dispatched while the Agent was
genuinely offline stayed `Dispatched` through a real 5-minute expiry,
then — with the stale queue message still undelivered — was correctly
discarded (not executed) the moment the Agent came back online.

**Admin UI (Pass 5)** surfaces all of the above in the dashboard, no
backend changes. New `CommandHistory.tsx` (mirrors `DeviceEventList.tsx`'s
fetch-on-mount template) is mounted on both `AgentDetail` (shows every
command for the Agent) and `DeviceDetail` (client-side filters the same
tenant-scoped `getAgentCommands` list to that one device — no dedicated
per-device endpoint). `AgentDetail` gained "Refresh configuration"/
"Apply configuration" buttons alongside the existing Restart/Deploy
(Apply uses a small inline version-number input row, not `ConfirmDialog`,
since that component has no support for required text input).
`DeviceDetail` gained a Camera-only "Capture now" button calling
`executeDeviceCapability(...,  "ImageCapture")` — its first
`.detail-header-actions` row. `applyAgentConfiguration` has no
`targetDeviceId` parameter, matching Pass 2's Agent-only scope. Not yet
verified live against a real Agent — build/lint clean, but no browser
pass against real data (see ADR-083).

See ADR-079, ADR-080, ADR-081, ADR-082, ADR-083.

### Device / DeviceType / Capability / Agent / AgentCapability domain model

Extends the same "domain class, separate from the Table entity" pattern
`Machine` established (ADR-053) to `Device`, `DeviceType`, and
`Capability` — all three previously had an Entity/DTO/flat CRUD service
but no domain class in between (`DeviceRegistryManagementService`/
`DeviceTypeManagementService`/`CapabilityManagementService` built/mutated
their `Entity` directly). Also introduces `DeviceCapability`, a genuinely
new concept: a capability assigned to a specific device, carrying
`ExecutingAgentId` — which Agent executes *this* capability for *this*
device, distinct from `Device.OwningAgentId` (which Agent owns the
device's hardware connection). A different Agent can execute a capability
than the one that owns the device (e.g. a Low-type agent owns a camera, a
separate High-type agent executes its Object Detection capability) — the
same split `DeviceOptions`/`SinkCleanlinessRoiOptions`/
`ObjectDetectionRoiOptions` already established in the MVP runtime blob,
now expressible as a real persisted, repeatable record instead of one
hardcoded field per capability.

- **Domain** (`Vivnest.Core/Domain`): `DeviceTypeDefinition` (not
  `DeviceType` — that name collides with `Vivnest.Core.Enums.DeviceType`,
  the fixed classification enum documented above; using both namespaces
  together in one file is a real C# `CS0104` ambiguous-reference error,
  confirmed live), `Capability`, `Device`, `DeviceCapability`, `Agent`
  (added in a follow-up pass, same session — see the addendum in
  ADR-057), `AgentCapability` (ADR-059). All six mirror `Machine.cs`'s
  shape (private ctor + validating ctor with an internally-generated Guid
  id + `Rehydrate` + explicit mutators). `DeviceCapability`/
  `AgentCapability` are both modeled after `AgentInstallation`, not the
  flat master lists — an assignment/declaration is a lifecycle (Assign/
  Unassign), so both soft-remove (`Status: Active`/`Removed`) rather than
  hard-deleting, preserving history. `AgentCapability` is "this Agent has
  the ability to execute Capability X," independent of any device —
  distinct from `DeviceCapability.ExecutingAgentId`, which is "this
  Agent is *assigned* to actually run this capability for *this* device."
  `Agent.CapabilityIds` (the flat list this replaced, ADR-046) is
  **removed** — same move ADR-057 already made for `Device.CapabilityIds`.
- **Application** (`Vivnest.Cloud/Admin`): `DeviceRegistryManagementService`
  renamed `DeviceService` (interface `IDeviceService`);
  `AgentRegistryManagementService` now routes through the `Agent` domain
  class the same way (`ToDomain`/`ToEntity`/`ToDto`), still preserving
  two real backward-compat quirks pre-ADR-053 rows depend on (blank
  `Status` → `AgentStatus.Active`; `default(DateTime)` `CreatedUtc` →
  backfilled with `UpdatedUtc` and `SpecifyKind`'d `Utc` before every
  write, since the Azure Table SDK rejects `Kind.Unspecified`). The
  underlying tables/entities (`tblDeviceRegistry`/`DeviceRegistryEntity`,
  `tblAgentRegistry`/`AgentRegistryEntity`) keep their existing names —
  persistence naming is a repository concern independent of the
  application-layer rename (same precedent as declining to rename
  `tblAgentRegistry` itself). New `ICapabilityAssignmentService`/
  `CapabilityAssignmentService` owns the `DeviceCapability` lifecycle
  (`AssignAsync`/`UpdateAssignmentAsync`/`UnassignAsync`/
  `ListByDeviceAsync`), enforcing "at most one active assignment per
  (Device, Capability) pair" the same way
  `AgentInstallationManagementService` enforces "at most one active
  installation per Agent." New `IAgentCapabilityAssignmentService`/
  `AgentCapabilityAssignmentService` (ADR-059) owns the `AgentCapability`
  lifecycle the same way (`AssignAsync`/`UnassignAsync`/`ListByAgentAsync` —
  no `UpdateAssignmentAsync`, a declaration has nothing mutable besides
  its own lifecycle).
- **Persistence**: `DeviceTypeEntity` gained `Description`/`Status`/
  `CreatedUtc`/`UpdatedUtc` (additive, backward-compatible).
  `DeviceRegistryEntity` **dropped `CapabilityIds`** (the old flat
  comma-separated Capability-id list) — superseded by real
  `DeviceCapability` rows — and **dropped `Enabled`**, replaced by
  `Status` (`DeviceStatus`: `Active`/`Disabled`/`Retired`, ADR-058). New
  `DeviceCapabilityEntity`/`tblDeviceCapabilities`
  (`PartitionKey = "{TenantId}|{SiteId}"`, `RowKey = DeviceCapabilityId`)
  + `IDeviceCapabilityStore`/`AzureTableDeviceCapabilityStore`, mirroring
  `AgentInstallationEntity`/`AzureTableAgentInstallationStore` exactly.
- **Device lifecycle, no hard delete** (ADR-058) — same reasoning as
  Machine: a Device's identity must remain stable (historical
  `DeviceCapability` assignments/`DeviceEvent`s may still reference its
  `DeviceId`), so `DELETE devices-registry-admin/{deviceId}` was
  **removed entirely**; retiring a Device is `PUT .../{deviceId}` with
  `Status: Retired`.
- **Tenant/Site authorization boundary** (ADR-058) — `Device.OwningAgentId`
  and `DeviceCapability.ExecutingAgentId` are the only two reference ids
  in this codebase that get real existence validation: both must resolve
  to a real Agent in the *same* Tenant/Site as the caller (checked via
  `IAgentRegistryStore`), or the create/assign/update call is rejected.
  Every other reference id in this codebase (`DeviceTypeId`, `CapabilityId`
  on `Device`, etc.) stays unvalidated by deliberate long-standing
  convention — this is the one deliberate exception.
- **Capability execution validation** (ADR-059, "Phase 4") —
  `ExecutingAgentId` on a `DeviceCapability` must go further than just
  existing: the Agent it names must also have an active `AgentCapability`
  declaration for the *exact* `CapabilityId` being assigned, checked via
  `IAgentCapabilityStore.GetActiveByAgentAndCapabilityAsync` inside
  `CapabilityAssignmentService.IsValidExecutingAgentAsync`. This is the
  validation that turns "which Agent executes this capability" from an
  unvalidated assumption into an enforced rule — assigning
  `ObjectDetection` to a Device with an `ExecutingAgentId` that hasn't
  declared `ObjectDetection` via `AgentCapability` is rejected (409),
  same as if the Agent didn't exist at all. Verified live to be a real,
  re-checked rule, not cached: unassigning the `AgentCapability`
  afterward makes a previously-successful `DeviceCapability` assign fail
  again on retry.
- **Routes**: `GET/POST device-types-admin`,
  `PUT device-types-admin/{deviceTypeId}` (now takes `Description`/
  `Status`); `GET devices-registry-admin` (optional `?ownerAgentId=`/
  `?deviceTypeId=` server-side filters, ADR-058), `POST devices-registry-admin`
  (no longer takes `CapabilityIds` or `Enabled`; rejects an
  `OwningAgentId` that doesn't resolve in this tenant/site with 400),
  `PUT devices-registry-admin/{deviceId}` (takes `Status` instead of
  `Enabled`; same `OwningAgentId` validation); new
  `POST device-capabilities-admin/assign` (rejects an `ExecutingAgentId`
  that doesn't exist in this tenant/site, or doesn't declare this
  capability, with 409, same combined message), `POST device-capabilities-admin/unassign`,
  `PUT device-capabilities-admin/{deviceCapabilityId}`,
  `GET device-capabilities-admin/by-device/{deviceId}` — Assign/Unassign
  are POST lifecycle actions, same shape `AgentInstallationsFunction`
  established for Install/Move/Uninstall. New (ADR-059)
  `POST agent-capabilities-admin/assign`, `POST agent-capabilities-admin/unassign`,
  `GET agent-capabilities-admin/by-agent/{agentId}` — same POST-lifecycle
  shape.
- **Capability assignment UI** (ADR-060) — a "Manage Capabilities"
  icon-button on each Agent/Device admin row opens `AgentCapabilitiesModal`/
  `DeviceCapabilitiesModal`, scoped to that one entity — no drill-down
  detail page and no cross-cutting `Admin > Capability Assignments`
  screen; every other Admin screen is a flat list + modal, and this
  follows the same shape. The two modals are deliberately NOT
  symmetrical: `AgentCapabilitiesModal` is a plain list + Add/Remove (a
  declaration has nothing beyond its own lifecycle); `DeviceCapabilitiesModal`
  is richer — each row shows an `Enabled`/`Disabled` status badge that's
  itself a toggle button, "Executed by: {Agent}", and the Add form's
  Executing Agent dropdown is filtered client-side to only Agents that
  have an active `AgentCapability` for the selected Capability (fetched
  via `Promise.all` over all agents when the modal opens) — the UI
  surfacing the exact rule `CapabilityAssignmentService.IsValidExecutingAgentAsync`
  already enforces server-side, so the picker never offers an agent that
  would be rejected anyway.
- **The admin `Device.DeviceId` and the real `DeviceId` `tblDeviceEvents`/
  `tblDeviceHeartbeat` key on are two unrelated identity spaces** — this
  has been true since ADR-048 ("registering a device here does not
  configure a real device") and ADR-058 leaves it unchanged deliberately;
  reconciling them (making the admin Device model a real source of truth
  the runtime blob config projects from) is its own future phase, not
  something folded into this one.

See ADR-057, ADR-058, ADR-059, ADR-060.

### Capability configuration, dependencies & compatibility (Phase 5)

ADR-057 explicitly deferred a DeviceType→Capability compatibility matrix
and a real configuration schema "to a future phase." This is that phase —
it adds the rules that make `Capability` assignments meaningful, folded
into one real validation algorithm `CapabilityAssignmentService.AssignAsync`
runs before a `DeviceCapability` is ever created.

- **Domain** (`Vivnest.Core/Domain`): `Capability` extended with `Status`
  (`CapabilityStatus`: `Active`/`Retired`), `ConfigurationSchema`
  (`IReadOnlyList<CapabilityConfigurationField>`),
  `ConfigurationSchemaVersion` (int, informational only — no migration
  engine), `DefaultConfiguration` (`IReadOnlyDictionary<string,string>`,
  same shape as `DeviceCapability.Settings`). New
  `CapabilityConfigurationField` value object (`Name`/`Type`
  (`CapabilityConfigurationFieldType`: `String`/`Number`/`Boolean`)/
  `Required`/`Minimum`/`Maximum`/`AllowedValues`/`DefaultValue`) — not its
  own entity/table, just structure serialized inside `CapabilityEntity`.
  New `CapabilityDependency` (global: `CapabilityId` requires
  `DependsOnCapabilityId`) and `DeviceTypeCapability` (global:
  `DeviceTypeId` is compatible with `CapabilityId`) — both hard-deletable
  (row existence is the fact, no history), unlike `DeviceCapability`/
  `AgentCapability`'s soft-remove-with-`Status` assignment lifecycle.
- **Deliberately global, not tenant-scoped**: `CapabilityDependencyEntity`/
  `tblCapabilityDependencies` and `DeviceTypeCapabilityEntity`/
  `tblDeviceTypeCapabilities` both use a constant `PartitionKey`, same as
  `CapabilityEntity`/`DeviceTypeEntity` — a dependency or compatibility
  fact is a property of two pieces of shared reference data, not any one
  tenant's. New `ICapabilityDependencyStore`/
  `AzureTableCapabilityDependencyStore`, `IDeviceTypeCapabilityStore`/
  `AzureTableDeviceTypeCapabilityStore`.
- **Application** (`Vivnest.Cloud/Admin`): new
  `CapabilityConfigurationService` (pure logic, no store) —
  `ApplyDefaults` merges supplied `Settings` over
  `Capability.DefaultConfiguration` then each field's own `DefaultValue`;
  `Validate` checks required-missing/wrong-type/out-of-range/not-in-
  `AllowedValues`. New `CapabilityDependencyService` — `AddAsync` runs a
  cycle check (BFS the existing global edge set forward from
  `DependsOnCapabilityId`; if `CapabilityId` is reachable, the new edge
  would close a cycle, rejected). New `CapabilityCompatibilityService` —
  existence + duplicate checks only.
  `CapabilityManagementService.DeleteAsync` now rejects (409) if a
  `CapabilityDependency`/`DeviceTypeCapability` still references this
  Capability — retire (`Status = Retired`) instead. This check is scoped
  to those two *global* tables only; it does not scan tenant-owned
  `DeviceCapability`/`AgentCapability` rows (a cross-tenant scan this
  codebase has never done anywhere) — same "no FK validation on this id,
  by deliberate long-standing convention" boundary every other
  cross-entity id already has.
- **`CapabilityAssignmentService.AssignAsync` — the complete assignment
  algorithm**: Device exists → Capability exists → Device has a
  `DeviceTypeId` set → Capability compatible with that DeviceType
  (`IDeviceTypeCapabilityStore`) → ExecutingAgent valid + declares this
  Capability (ADR-058/059, unchanged) → at most one active assignment per
  (Device, Capability) pair → each **direct** `CapabilityDependency` of
  this Capability satisfied by an active `DeviceCapability` on this same
  Device (not transitive — each capability already enforced its own
  direct deps when *it* was added) → `Settings` merged with
  `Capability.DefaultConfiguration`/field defaults, then validated.
  `UpdateAssignmentAsync` only re-validates ExecutingAgent/Settings, not
  compatibility/dependencies — those don't change from an Update, and
  re-checking them would retroactively break assignments that predate
  this ADR's compatibility data.
- **Typed result replaces bare nullable `Dto`**:
  `ICapabilityAssignmentService.AssignAsync`/`UpdateAssignmentAsync`
  return `CapabilityAssignmentResult` (`DeviceCapabilityDto?`,
  `CapabilityAssignmentErrorCode?`, `string? ErrorMessage`) instead of a
  bare `DeviceCapabilityDto?` with every failure folded into one combined
  409 string — the Function layer switches on the error code to return
  the specific 400/404/409 message. The wire format is unchanged (still a
  plain string body via `BadRequestObjectResult`/`ConflictObjectResult`).
  `CapabilityManagementService.DeleteAsync` has the analogous
  `CapabilityDeleteResult`.
- **Routes**: new `GET/POST capability-dependencies-admin`,
  `POST capability-dependencies-admin/add`,
  `DELETE capability-dependencies-admin/{dependencyId}`; new
  `GET/POST device-type-capabilities-admin`,
  `POST device-type-capabilities-admin/add`,
  `DELETE device-type-capabilities-admin/{deviceTypeCapabilityId}` — both
  follow `CapabilitiesAdminFunction`'s shape (tenant `x-api-key` +
  `DevicesOnly` 403 even though the data is global — auth is about who
  may call the admin API, not about the data being tenant-scoped).
  `POST/PUT capabilities-admin` now accept `Status`/`ConfigurationSchema`/
  `ConfigurationSchemaVersion`/`DefaultConfiguration`.
- **Dashboard**: `CapabilityFormModal.tsx` gained a Status dropdown
  (edit-only) and a repeatable Configuration Schema row editor — no
  separate top-level Default Configuration editor, each field's own
  Default Value is the only place the UI sets a default. New `LinkIcon` +
  `CapabilityRelationshipsModal.tsx` (Dependencies list + Compatible
  Device Types list, both simple Add/Remove) triggered per-row from
  `CapabilitiesAdmin.tsx` — only direct dependencies shown, no transitive
  chain. `DeviceCapabilitiesModal.tsx` is where the rules become visible:
  the Add form's Capability dropdown is filtered to what's compatible
  with the Device's DeviceType; selecting a Capability fetches its direct
  dependencies and disables Assign with an inline explanation if any are
  unmet; a new `ConfigFields` component (shared between Add and a
  per-row "Configure" affordance on already-assigned capabilities)
  renders one input per `ConfigurationSchema` field, driven entirely by
  the schema rather than hard-coded per-Capability UI.

See ADR-062.

### Runtime configuration boundary: identity mapping + publishing

The Admin domain above and the real `Vivnest.Agent` runtime configuration
(`appsettings.json` + `device-config/*.json`, see the "MVP device
configuration" section below) are connected by two independent
publishing pipelines — a Device one and an Agent one — that converge only
at Blob Storage. Neither writes the other's blob.

- **Identity mapping**: `Device.RuntimeDeviceId` (`Vivnest.Core/Domain/Device.cs`)
  and `Agent.RuntimeAgentId` (`Vivnest.Core/Domain/Agent.cs`) are additive,
  admin-typed, unvalidated string fields — the real `device-config/*.json`
  blob's own `DeviceId` / the real `appsettings.json`'s `Agent:AgentId`
  this admin record corresponds to. Empty means not linked yet.
- **`ICapabilityRuntimeProjector`** (`Vivnest.Cloud/Admin/CapabilityProjection/`)
  — the shared contract both pipelines dispatch a Device's `DeviceCapability`
  rows through, keyed by `Capability.CapabilityName` (matched
  case/whitespace-insensitively, same convention as `DeviceType` matching).
  A projector returns `DeviceEntry` (this device's own capability entry)
  and/or `AgentEntry` (a contribution to the *executing* agent's own
  document), since different capabilities affect different runtime
  locations — `ObjectDetection`/`SinkCleanliness` need ROI on the device's
  entry and model params on the executing agent's. **Four concrete
  projectors are registered**: `ImageCaptureRuntimeProjector`,
  `ObjectDetectionRuntimeProjector`, `SinkCleanlinessRuntimeProjector`,
  `MotionDetectionRuntimeProjector` (all
  `Vivnest.Cloud/Admin/CapabilityProjection/`). `Image Classification`
  still has none — a pure Phase 5 demo placeholder with no Options
  class, classifier interface, or worker behind it anywhere. Any device
  assigned it currently produces a "no runtime projector registered"
  warning and is excluded from any published document rather than
  guessed at, which is why the real Kitchen Camera (assigned it) stays
  blocked from publishing.
- **Device pipeline** — `IDeviceRuntimeConfigurationProjector`/
  `DeviceRuntimeConfigurationProjector` (renamed from ADR-063's
  `IDeviceConfigurationProjector`) produces a
  `DeviceRuntimeConfigurationDocumentDto` (identity + `Settings` +
  each capability's `DeviceEntry`) from the admin `Device`/
  `DeviceTypeDefinition`/owning `Agent`, with a `Warnings` list naming
  every unresolved gap. `IDeviceRuntimeConfigurationPublisher`/
  `DeviceRuntimeConfigurationPublisher` writes it as a full,
  self-contained overwrite of `device-config/{runtimeDeviceId}.json` —
  hard-gated on zero `Warnings`. `Schedule`/`Trigger`/`Sensors`/
  `LivenessInterval` are still not projected.
- **Agent pipeline** — `IAgentRuntimeConfigurationProjector`/
  `AgentRuntimeConfigurationProjector` queries every `DeviceCapability`
  across the tenant/site whose `ExecutingAgentId` is this Agent
  (`IDeviceCapabilityStore.GetByExecutingAgentAsync`), runs each through
  the same registry, and assembles an `AgentRuntimeConfigurationDocumentDto`
  matching the real `AiClassificationOptions.Devices[]` shape exactly —
  rebuilt fresh each time, so reassignment/removal needs no explicit
  "unpublish." `IAgentRuntimeConfigurationPublisher`/
  `AgentRuntimeConfigurationPublisher` writes it by replacing *only* the
  `AiClassification` top-level key on `agent-config/{runtimeAgentId}.json`,
  leaving every other section (e.g. a Low-type agent's `HomeAssistant`)
  untouched.
- **Credential encryption, not stripping**: both publishers write
  `Settings` (Device connection settings, each capability's own Settings,
  and each Agent device entry's `ObjectDetection`/`SinkCleanliness`
  settings), encrypting any credential-shaped key (`Password`,
  `RtspPassword`, `AccessToken`, etc.) with AES-256-GCM under a shared
  symmetric key (`CredentialCipher.EncryptFields`,
  `Vivnest.Core/Security/CredentialCipher.cs`) before it reaches a live,
  immutable-versioned runtime blob — `"enc:v1:..."` ciphertext, not
  plaintext. `Device.Settings`/`DeviceCapability.Settings` still accept
  credentials in plain text in Table Storage by deliberate design
  (ADR-050); it's only the *publish* write path that now encrypts them.
  A missing/invalid `CredentialEncryption:Key` blocks publish outright
  (`Cannot publish: ...`) rather than silently falling back to plaintext.
  `Vivnest.Agent` holds the same key independently (`appsettings.json`,
  same bootstrap tier as `Storage:ConnectionString` — ADR-086) and
  decrypts every `"enc:v1:"` value anywhere in a downloaded config blob
  before using it (`CredentialCipher.DecryptInPlace`). This replaced a
  strip-and-warn `CredentialSettingsFilter` (ADR-064), briefly removed to
  plaintext-only (ADR-084), now superseded by this encrypt-in-place
  design — see ADR-085/ADR-086. No rotation story yet: it's a single
  static key on both sides.
- **`Vivnest.Agent` Runtime Adapter** (`Vivnest.Agent/Runtime/Configuration/DeviceConfigRuntimeAdapter.cs`,
  Device blob only) — detects a top-level `Capabilities` key in each
  downloaded `device-config/*.json` blob; a legacy-shape blob is
  untouched, a new-shape one is flattened back into the identity fields
  `DeviceOptions` already binds. The Agent blob needs no equivalent —
  `IAgentRuntimeConfigurationPublisher` writes exactly the shape
  `Vivnest.Agent` already parses.
- **Routes**: `GET`/`POST devices-registry-admin/{deviceId}/projected-config`/
  `publish-config`, `GET`/`POST agents-registry-admin/{agentId}/projected-config`/
  `publish-config`. Dashboard: `ProjectedConfigModal.tsx`/
  `AgentProjectedConfigModal.tsx` show the preview + warnings + a
  "Publish" button disabled while any warning is present.
- **First real capability projector/adapter — Image Capture** (Phase 6C):
  `ImageCaptureRuntimeProjector` (Cloud) and `ImageCaptureRuntimeAdapter`
  (Agent, `Vivnest.Agent/Runtime/Configuration/`, dispatched by a small
  mirrored `ICapabilityConfigRuntimeAdapter` registry) prove the
  `capabilities[]` wire shape doesn't need to change per capability —
  Image Capture's real runtime shape (`Schedule`/`LivenessInterval`/
  `WarningMultiplier`, flat fields directly on `DeviceOptions`, not a
  nested sub-object like `ObjectDetection`/`SinkCleanliness`) is
  translated purely as an implementation detail on each side. No worker
  code changed.
- **Cross-agent capability pair — Object Detection/Sink Cleanliness**
  (ADR-066): `ObjectDetectionRuntimeProjector`/
  `SinkCleanlinessRuntimeProjector` (Cloud) are the first projectors to
  actually populate both `DeviceEntry` (ROI —
  `RoiLeft`/`RoiTop`/`RoiRight`/`RoiBottom`) and `AgentEntry` (model
  params — `ModelPath`/`ConfidenceThreshold`, plus optional
  `ExpectedClasses` on Object Detection) from one assignment, gated
  together by a single `Warnings` list so a half-configured capability
  never publishes as "working" on just one side.
  `ObjectDetectionRuntimeAdapter`/`SinkCleanlinessRuntimeAdapter` (Agent)
  write a nested `DeviceOptions.ObjectDetection`/`.SinkCleanliness`
  sub-object (`{Enabled, RoiLeft, RoiTop, RoiRight, RoiBottom,
  ExecutingAgentId}`) — no model-param handling on this side at all,
  since those bind directly into `AiClassificationOptions` on the
  executing agent's own blob with no adapter needed, exactly as ADR-064
  established. Confirmed the existing `AgentRuntimeConfigurationProjector`/
  `AgentRuntimeConfigurationPublisher` needed zero changes to support
  this — their `AgentEntry` consumption was already fully generic across
  capabilities.
- **Configuration versioning**: both publishers stamp `PublishedUtc`
  (UTC) at write time — a top-level field on the Device document, a
  sibling `ConfigurationPublishedUtc` key next to `AiClassification` on
  the Agent's blob (bound via a new root-bound `AgentConfigMetadataOptions`).
  `DeviceOptions`/`DeviceHeartbeat`/`AgentHeartbeat` all carry
  `ConfigurationPublishedUtc`, reported on every heartbeat. Consumed by
  `ConfigurationSyncStatusService` (ADR-068, below) for the
  Desired/Published/Applied comparison.
- **Agent display name** (ADR-087): the dashboard's Agent `Name` traces
  back to `AgentRegistryEntity.Name` (Admin-set), not a locally-typed
  value — `AgentRuntimeConfigurationPublisher` writes it as another
  top-level sibling key (`AgentConfigMetadataOptions.Name`) on every
  publish/rollback, and `PlatformAgentHeartbeatWorker` reports whatever it reads
  from there. `AgentOptions.Name`/`appsettings.json`'s old `Agent:Name`
  field no longer exist — null until the Agent has been published
  through this pipeline at least once.
- **Schema versioning** (ADR-066): both wire documents also carry a
  `SchemaVersion`/`ConfigurationSchemaVersion` top-level field (both
  currently `1`, `RuntimeConfigurationSchemaVersions` in
  `Vivnest.Core/Constants/`). `DeviceConfigRuntimeAdapter.Adapt` checks
  it before flattening — absent is tolerated as version 1, present-but-mismatched
  throws `UnsupportedConfigurationSchemaException`, caught by a dedicated
  try/catch in `AgentConfigurationLoader`'s `TryLoadRemoteDeviceConfigsAsync` so one
  device declaring an unrecognized schema is skipped rather than
  aborting every other device. `PlatformAgentHeartbeatWorker` does an analogous
  one-time (not per-tick) check that only logs a warning.
- **Device-type-gated capability — Motion Detection** (ADR-067):
  `MotionDetectionRuntimeProjector` (Cloud) mirrors Image Capture's
  device-local, flat-field shape (`LivenessInterval`/`WarningMultiplier`/
  optional `Schedule.Interval` via `BatteryReportIntervalMinutes`) but
  only produces a real entry when the device actually resolves to
  `DeviceType.MotionSensor` — any other device type (e.g. a Camera) gets
  a warning and is excluded, rather than colliding with
  `ImageCaptureRuntimeProjector`'s own claim on those same root fields.
  It resolves the device's runtime type itself via its own
  `IDeviceTypeStore` dependency (`Project` is deliberately synchronous,
  so this one lookup runs via `.GetAwaiter().GetResult()`, confined to
  this file) rather than threading a pre-resolved type through the
  shared `ICapabilityRuntimeProjector` interface for one consumer.
  `MotionDetectionRuntimeAdapter` (Agent) mirrors
  `ImageCaptureRuntimeAdapter` minus the Burst fields. Verification
  surfaced a real data gap, not a code bug: the `DeviceTypeCapability`
  compatibility table had Motion Detection registered against Camera
  only, never Motion Sensor — fixed as an admin data change (a new
  compatibility row), left in place after verification.
- **Configuration lifecycle status** (ADR-068, a scoped slice of the
  "Phase 6D" spec): `ConfigurationSyncStatus` (`NeverPublished`/
  `Pending`/`UpToDate`/`Failed`/`Unknown`) is computed at read time by
  `ConfigurationSyncStatusService` (`Vivnest.Cloud/Admin/`) and attached
  to both `GET .../projected-config` responses as `SyncStatus` — compares
  the currently published blob's own `PublishedUtc` against the latest
  heartbeat's `ConfigurationPublishedUtc`/`ConfigurationLoadError`, both
  best-effort (a missing blob or heartbeat is a real reportable state,
  not an error). "Desired" is never persisted separately — it's just the
  same already-projected document. Both publishers also auto-dispatch a
  restart after a successful publish or rollback, closing the loop for an
  online agent automatically. This originally went straight to
  `IAgentCommandPublisher`/`agent-restart-commands`, leaving no trace in
  command history; it now goes through `ICommandDispatcher` like every
  other restart since ADR-079, so it is a tracked `tblAgentCommands` row
  with `RequestedBy` of `ConfigPublish`/`ConfigRollback` (vs `Dashboard`
  for the operator-initiated route). Still best-effort — a publish that
  succeeded never fails on the follow-up restart — but the dispatcher can
  now decline in two ways the blind enqueue couldn't: no command at all
  when the owning Agent has no heartbeat (nothing running to restart), and
  `AGENT_BUSY` when another disruptive command is mid-flight (that one
  picks up the new config when it restarts). Both are logged, not
  surfaced. A coarse, Agent-level (not
  per-device) `ConfigurationLoadError` on `AgentHeartbeat` — set when
  `AgentConfigurationLoader` catches an `UnsupportedConfigurationSchemaException` for
  any device it owns — is what lets Status distinguish `Failed` from
  `Pending`.
- **Monotonic versioning, immutable blobs, manifest, hash, concurrency**
  (ADR-069, Configuration Lifecycle Pass 1): additive, alongside the flat
  `device-config/{id}.json`/`agent-config/{id}.json` from ADR-068 — every
  publish now *also* writes an immutable
  `.../{id}/versions/{n}.json` (conditional upload, `IfNoneMatch: "*"`,
  a real 409 on a name collision — never overwritten once written) and a
  mutable `.../{id}/current.json` manifest
  (`{ConfigurationVersion, ConfigurationHash, ConfigurationUri, PublishedUtc}`,
  the shared `Vivnest.Core.Constants.ConfigurationManifest` record both
  Cloud and Agent reference). New `DeviceConfigurationEntity`/
  `AgentConfigurationEntity` (`tblDeviceConfiguration`/`tblAgentConfiguration`)
  track only `CurrentVersion`/`CurrentHash`/`PublishedUtc` — Desired stays
  unpersisted (ADR-068's own principle), Applied stays on the heartbeat.
  A SHA-256 hash of the content-only portion of the document (excluding
  `PublishedUtc`/`SchemaVersion`/`ConfigurationVersion`/`ConfigurationHash`
  themselves) gates every publish — an unchanged hash is a no-op
  (`Published: false, Reason: "Configuration unchanged since version {n}."`),
  never bumping the version for a no-op republish. `AzureTableStore.UpdateAsync`'s
  existing ETag-based optimistic concurrency (no new mechanism needed)
  guards the metadata row; a 409 (version-blob collision) or 412
  (stale metadata `ETag`) both retry the whole publish cycle from a fresh
  read (bounded, 3 attempts) — a losing concurrent attempt can leave one
  orphaned, unreferenced version blob, a deliberate, tolerable cost for
  correctness over gap-free version numbers, never data loss.
  `AgentConfigurationLoader`'s device/agent-config loaders try the manifest path
  first, falling back to the legacy flat blob on a 404 — true dual-shape
  "run alongside," not a special case; a device republished through the
  new pipeline is processed before any stale legacy blob with the same
  identity. `ConfigurationSyncStatusService` compares `PublishedVersion`/
  `AppliedVersion`/hash directly when both sides have them (more precise
  than the ADR-068 timestamp comparison, which still works as the
  fallback for anything still on the legacy path only).
- **Last-known-good fallback and rollback** (ADR-070, Configuration
  Lifecycle Pass 2): `AgentConfigurationLoader`'s device-config loader caches every
  successfully-loaded device document locally
  (`config-cache/devices/{deviceId}.json`, the same directory class as
  `common-config.json`) and, on `UnsupportedConfigurationSchemaException`,
  falls back to that cache instead of dropping the device outright — the
  device keeps running its last-good config, `ConfigurationLoadError`
  still reports `Failed` so Admin can see it, and a device with no prior
  successful load still degrades to the original drop behavior (nothing
  to fall back to). `IDeviceRuntimeConfigurationPublisher`/
  `IAgentRuntimeConfigurationPublisher` gained `RollbackAsync(tenant, id,
  targetVersion)` — reads an old `versions/{n}.json` verbatim (never
  re-projected from live Admin state) and republishes it as a brand-new
  version via the same write cycle `PublishAsync` uses (today
  `RuntimeConfigurationWriter<TEntity>.WriteVersionAsync`, shared by both
  publishers), always creating a new version regardless
  of hash. New `ConfigRolledBack` audit event type distinguishes a
  rollback from a routine publish. Routes: `POST
  devices-registry-admin/{deviceId}/rollback-config/{targetVersion:int}`,
  `POST agents-registry-admin/{agentId}/rollback-config/{targetVersion:int}`;
  a minimal version-input + "Roll back" control sits next to the Sync
  Status block in both projected-config dashboard modals. The
  offline-while-config-changes scenario (Agent picks up the latest
  version directly, never processing intermediate ones) was verified
  against the existing ADR-069 manifest-first design and needed no code
  change. Still not built: true Agent-side periodic self-restart polling
  independent of a publish event, a forced migration of existing
  production blobs to the new layout, and per-device (as opposed to
  Agent-level) failure attribution.

See ADR-063, ADR-064, ADR-065, ADR-066, ADR-067, ADR-068, ADR-069, ADR-070.

### Identity spaces: the complete map (Command Routing 1.x checkpoint)

Four identities exist, deliberately. Every bug in this area has been one
of them being used where another was expected, so this is the reference
for which is which and who translates.

| Concept | Admin / registry | Runtime | Translated by |
|---|---|---|---|
| Agent | `AgentRegistry.RowKey` | `RuntimeAgentId` | `IAgentRegistryStore.GetByRuntimeAgentIdAsync` (ADR-072) |
| Device | `DeviceRegistry.RowKey` | `RuntimeDeviceId` | `IDeviceRegistryStore.GetByRuntimeDeviceIdAsync` (ADR-104) |
| Capability | catalogue GUID (`Capability.RowKey`) | `CapabilityKey` (`camera.capture`) | `AgentCommandsFunction.ToRuntimeIdentityAsync` (ADR-102) |
| Device capability assignment | registry `DeviceId` + registry `ExecutingAgentId` | projected into the runtime config document | `DeviceRuntimeConfigurationProjector` (see the section above) |

**The rule.** Admin identities never reach the Agent; runtime identities
never reach an admin-keyed store. Translation happens at the boundary and
nowhere else - no store accepts both, and no lookup falls back from one to
the other. A fallback would make every future mismatch silent, which is
exactly how ADR-104's bug survived.

**Commands, end to end.** Two translations, in opposite directions, on the
same request:

```
Dashboard                  resolves camera.capture -> catalogue GUID
   POST execute-capability  TargetDeviceId = RuntimeDeviceId
                            CapabilityId   = catalogue GUID
        |
   CommandDispatcher.ValidateAsync
        |  RuntimeDeviceId -> registry DeviceId   (ADR-104)
        |  RuntimeAgentId  -> registry AgentId    (ADR-081)
        |  DeviceCapability assignment checked with BOTH registry ids
        v
   tblAgentCommands         CapabilityId = catalogue GUID   (storage identity)
        |
   Agent GET /agents/{id}/commands/{id}
        |  catalogue GUID -> CapabilityKey        (ADR-102)
        v
   AgentCommandDetails      CapabilityKey = camera.capture  (wire identity)
        |
   CapabilityRegistry -> CameraCapability -> DeviceTriggeredEvent
        |
   CaptureOnTriggerHandler -> CameraCaptureExecutor -> capture
```

Storage keeps the catalogue GUID; only the wire carries the runtime key.
The command row is history and must stay readable against the catalogue,
so it is never rewritten to `CapabilityKey`.

**One execution path.** `ExecuteCapabilityCommandHandler` publishes
`DeviceTriggeredEvent` and nothing else - its constructor cannot reach a
capture service, asserted structurally by
`TheHandlerCannotExecuteACaptureItself`. `CameraCaptureExecutor` was
extracted so the worker and the trigger handler could not drift into two
capture implementations; routing that invoked a capability directly would
have recreated that split.

**`"ImageCapture"` is retired (ADR-105).** It was a fourth identity - a
Cloud command alias that was neither a catalogue RowKey nor a
`CapabilityKey` - carried by a validation short-circuit that skipped the
assignment lookup entirely. Verified live on 2026-08-23: `"ImageCapture"`
and `"Image Capture"` both return `CAPABILITY_NOT_FOUND` and are never
dispatched; the catalogue GUID dispatches and routes to `camera.capture`.
The agent log contains no mention of either rejected identity, because
neither left Cloud.

**Evidence, and why it took contact with the live system.** Three
successive diagnoses of the same symptom were wrong, each overturned by
the next piece of evidence, and each pointed at a plausible fix that would
have made things worse:

1. *"Blob vs table disagree"* - the read model does not read the blob for
   this capability.
2. *"Image Capture is built-in, so the validator is wrong"* - it has a
   catalogue row, a `DeviceTypeCapability` row, a `DeviceCapability` row
   and a projector. It is plainly assignable.
3. *"The registry and runtime spaces have zero overlap"* - an artefact of
   a column filter. `RuntimeDeviceId`/`RuntimeAgentId` were there all
   along.

The fix only became findable after querying the tables globally rather
than for the one device in question. Worth remembering the next time a
lookup returns nothing: *empty* and *asked with the wrong key* look
identical from the caller.

**Still inconsistent, and known.** `DeviceCapabilitiesQueryService` has no
assignment store among its three dependencies and synthesises the
capability list from `device.Type`, so
`GET /devices/{id}/capabilities` would report `Image Capture` for any
camera whether or not it is assigned. It happens to agree with the
assignment today. See EVOLUTION-PLAN.md's deferred list.


## Dashboard

`Vivnest.Dashboard` — React + Vite + TypeScript, no UI framework
dependency; a hand-rolled CSS custom-property token system (`App.css`)
instead — see ADR-018. Navigation is responsive with two presentations
of the same menu: on narrow viewports, four bottom tabs — **Overview**,
**Devices**, **Agents**, **Events** — plus a hamburger Admin drawer; at
≥1024px a persistent left `Sidebar` (brand + tenant/site, the four main
views, the Admin section, Log out) replaces the top header row, the
bottom tabs, and the Admin screens' back buttons (all still rendered —
`App.css` media queries decide which chrome shows). A `DevicesOnly` key
(decided from `GET /whoami` right after login) sees only the Devices
view, with the other tabs, the drawer, and the sidebar hidden entirely
(not just disabled). Both lists render as status-accented row cards
(`.entity-list`/`.entity-row`), not raw tables, so they reflow at
narrow widths instead of horizontally scrolling.

Four views total, all state-driven (no router): `DeviceList` →
`DeviceDetail`, and `AgentList` → `AgentDetail` (new — previously agents
had no drill-down). `DeviceDetail`'s metric grid includes a clickable
link to the device's `AgentDetail` page (hidden for `DevicesOnly` keys,
which get 403 from `/agents*`); `AgentDetail` lists that agent's devices,
filtered client-side from the already-fetched device list rather than a
dedicated endpoint, linking back into `DeviceDetail`.

`AgentDetail` uses the same three-tab split as `DeviceDetail`:
**Overview** (last-heartbeat / interval / uptime trio, `AgentMetricsChart`
Resource usage, Devices on this agent), **Activity** (`CommandHistory`),
and **Configuration** — the Configuration cell (status badge +
Desired/Applied version) and Software cell (Desired/Running version),
with the Refresh configuration / Apply configuration / Deploy latest
actions moved beside those cells rather than in the page header (which
keeps only Download logs + Restart), followed by an **Agent info**
identity section (Hostname / Firmware / Runtime / OS). Both status cells
reuse the `ConfigurationStatus`/`VersionStatus` fields the API has
carried on `AgentSummaryDto`/`DeviceSummaryDto` since ADR-075 but the
dashboard never rendered until ADR-077 (Phase 8 Pass 4). The shared `AgentRow`/
`DeviceRow` row components gained small inline "cfg"/"ver" indicators
next to the status dot, shown only when that status isn't
`UpToDate`/`NeverPublished`/`NeverDeployed`, so a healthy row stays
uncluttered. `Overview` leads with a health hero banner ("Everything is
healthy" / "N need attention" with a severity breakdown, tinted
green/amber/red), then a KPI row (Agents online X/Y, Devices online X/Y,
Events in the last 24h), the Agents/Devices summary cards — each with a
thin clickable status-distribution bar above the same pre-filtering
`StatusFilterChips` — the ADR-077 config/version rollup rendered as a
pair of meters ("Configuration 7/9 up to date", "Software 2/3 up to
date"; `NeverPublished`/`NeverDeployed` still excluded from the
denominator for the same "hasn't been asked to be current yet isn't out
of date" reasoning), a "Needs attention" row list (rendered only when
something actually needs attention — the hero carries the all-clear
case), and a six-row "Recent activity" preview of the Events feed
(fetched via the same `GET /events` the Events tab uses; a failure there
hides the section rather than blanking the page).

Device detail is split into three underlined tabs (`.detail-tabs`, per
the device-detail mockups — AI Inferences/Diagnostics from those mockups
are deliberately absent, no backend exists for them): **Overview**
(runtime state: a last-heartbeat / interval / last-activity metric trio —
heartbeat next to its expected interval reads as "is it late?"; the old
per-capability On/Off cells were config flags duplicating the
Configuration tab's richer Enabled + operational-status rows, so they
were removed — `BatteryStatus` for MotionSensors, Hub /
Connected devices, and the camera capture-preview hero + `CaptureGallery`),
**Activity** (`DeviceEventList` for non-cameras plus `CommandHistory`;
hidden entirely for a camera on a `DevicesOnly` key, where both halves
would be empty), and **Configuration** (configurable state first — the
ADR-077 configuration sync cell, then what was previously the
Capabilities tab: `CapabilitiesTab`'s grouped capabilities, Triggered
By, and Source Sensors — followed by a labeled **Device info** identity
section: Brand / Model / Firmware / Timezone. Identity facts aren't
configuration, but four static cells don't earn their own tab either;
same reasoning for why there's no per-device System Info tab — system
metrics are agent-level in Vivnest, so the section ends with a "View
{agent} →" link to the owning agent's Resource usage instead, hidden for
`DevicesOnly` keys).

The Overview tab shows — gated behind
`device.deviceType === "Camera"`, see ADR-007's frontend addendum — a
`CaptureGallery` component: a 30-day, day-grouped capture timeline
(`Today`, `Yesterday`, then full dates), collapsed by default except the
current day. Expanding a day (or the initial Today auto-expand) fetches
its captures one page at a time (50 at a time, newest first, "Load more"
for the rest) rather than the whole window or the whole day up front —
see ADR-017. Above the gallery sits a capture-preview hero panel showing
the device's latest capture thumbnail by default — the earlier static
"Live" placeholder was removed rather than imply a stream that doesn't
exist (no streaming pipeline yet, see ADR-018), and a device with no
captures gets no panel at all. Clicking a gallery thumbnail swaps that
same panel to the selected capture with a "Back to latest" control,
rather than opening a separate preview or a lightbox, so there's at most
one large-image panel on the page. The selected-capture `img` is keyed
per capture so the detection overlay's natural-size measurement re-fires
even when the selected capture's URL equals the latest-capture thumbnail
(the common case).
Non-camera devices show `DeviceEventList` (extracted from what was
originally inline in `DeviceDetail`) on the Activity tab instead of the
gallery, and — since every event a camera produces is `CameraCaptured`,
already shown richer in the gallery — cameras never call `GET .../events`
at all. `MotionSensor` devices additionally get a `BatteryStatus`
component on the Overview tab (events are still wanted too, on
Activity): a `Status`/`Last checked` cell pair plus a history list,
sourced from `GET .../battery`, self-contained fetch like
`CaptureGallery`. It's a status badge, not a chart — the T100 only ever
reports a low-battery boolean, no numeric percentage (confirmed against
the real device); see ADR-022.

A hamburger button in the header (left of the `Vivnest` title, `MenuIcon`)
opens `AdminDrawer`, a slide-out panel separate from the Devices/Agents
tab bar (hidden while any admin view is open). Six real items today —
**Capabilities** (`CapabilitiesAdmin`/`CapabilityFormModal`, ADR-042),
**Device Types** (`DeviceTypesAdmin`/`DeviceTypeFormModal`, ADR-047),
**Devices** (`DeviceRegistryAdmin`/`DeviceRegistryFormModal`, ADR-048),
**Agents** (`AgentRegistryAdmin`/`AgentRegistryFormModal`, ADR-043),
**Machines** (`MachinesAdmin`/`MachineFormModal`, ADR-056), and
**Agent Installations** (`AgentInstallationsAdmin`/`InstallAgentModal`,
ADR-056) — each a filterable list with Add/Edit (`.form-dialog`
modal)/Delete (reusing `ConfirmDialog`), except Machine and Devices (no
Delete route exists for either — Status is edited instead, see
ADR-053/056 for Machine, ADR-058 for Device) and Agent Installations (not
CRUD at all — Install/Move/Uninstall lifecycle actions per registered
Agent, shown via `InstallAgentModal` shared across Install/Move).
**Services**/**Automations** are shown but disabled ("soon") since those
master lists don't exist yet. Both the "Devices" and "Agents" admin lists
are unrelated to the bottom-nav **Devices**/**Agents** tabs above — the
admin ones manage `tblDeviceRegistry`/`tblAgentRegistry` pre-registration
entries, the tabs show real `tblDeviceHeartbeat`/`tblAgentHeartbeat`-derived
monitoring data; neither pair shares a component or an endpoint. The
Device form (identity, Device Type/Owning Agent dropdowns, four
descriptive fields, a Status dropdown shown only when editing — same
Create-always-starts-Active pattern `MachineFormModal` established,
ADR-058 — and a free-form Settings key-value editor — the Capabilities
checklist that used to sit here was removed in ADR-057, since it
read/wrote the now-retired `CapabilityIds` field; assigning capabilities
to a device has no dashboard UI yet, see ADR-057) is tall enough that
`.form-dialog` needed a
`max-height: calc(100vh - 2rem)` + `overflow-y: auto` cap so it scrolls
internally instead of pushing its own Save button off a real laptop-height
screen — found live during this build, fixed for every `.form-dialog`
user at once (see ADR-048).

A seventh drawer item, **API Keys** (`ApiKeysAdmin`, ADR-054), sits below
the other six behind a `.admin-drawer-divider` — it's the one item
backed by the Azure Functions host key (operator tier), not this
tenant's own `x-api-key`. Selecting it renders `OperatorKeyGate`
(mirrors `ApiKeyGate` but stores the host key under its own
`localStorage` key, `vivnest.operatorKey`, entirely separate from the
tenant session) until a host key is entered and validated by a real
`GET /tenants` call. Once past that gate: a dependent Tenant → Site
dropdown pair (by Name, resolved to Id on every request), the selected
Site's existing keys (`GET /apikeys?tenantId=&siteId=`, with Revoke),
and a create form (Name, `DevicesOnly`) whose response is shown exactly
once in a copy-and-dismiss box — `tblApiKeys` only ever stores a hash,
so this is the only place the raw value is ever visible again after
creation.

## Deploy

`Vivnest.Agent.Updater` (repo root project, deployed as a standalone
executable — never inside `Vivnest.Agent`'s container, and never built
into its Docker image) is the host-side half of the "Deploy latest"
button on the Agent Detail page. It's a separate, minimal process rather
than a feature of `Vivnest.Agent` because it needs Docker access the
Agent container is deliberately refused (ADR-020) — see ADR-028 for the
full reasoning, including why Watchtower was considered and deferred.

- **Runs directly on the host** (a Windows Scheduled Task today; a
  systemd unit on a future Raspberry Pi host — the executable itself is
  identical either way, since it's plain, self-contained .NET).
- **Reads its own `updater.settings.json`** (`Agent:AgentId`,
  `Messaging:ConnectionString`, `Messaging:DeployCommandQueue`,
  `Deploy:PollInterval`, plus `Deploy:ContainerName`/`Deploy:AcrUsername`/
  `Deploy:AcrPassword` added later — all in **plain text**, including the
  full storage connection string; `CredentialCipher` is not used anywhere
  in the Updater), deployed into the same folder as the Agent's
  `appsettings.json` but deliberately not the same file — both
  executables' publish output lands in the same host folder, and sharing
  the name `appsettings.json` would mean redeploying the Updater risks
  overwriting the Agent's real, secret-bearing config. Inserted before
  the environment-variables source (same ordering convention
  `TryLoadRemoteConfigAsync` already uses Agent-side), so an env var
  override still wins over this file.
- **Polls `agent-deploy-commands`** (`DeployPollingWorker`, 30s default
  interval — configurable via `Deploy:PollInterval`, unlike
  `PlatformCommandPollingWorker`'s hardcoded 15s — delete-before-process, same
  non-retrying shape as `PlatformCommandPollingWorker`)
  and on a matching command runs `docker pull` / `stop` / `rm` / `run` via
  `Process.Start` (`ProcessStartInfo.ArgumentList`, not a concatenated
  command string) — the same four commands `scripts/update-agent.ps1`
  already runs by hand, now automated. Registry and image name are still
  hardcoded `const`s in `AgentDeployer` (`vivnestagent2acr.azurecr.io` /
  `vivnest-agent` — ADR-090 was a code change to move registries), but
  container name and ACR credentials are now `DeployOptions`-driven and
  `DeployCommandQueueMessage` carries `{AgentId, IssuedAtUtc, ImageVersion?}`
  since ADR-073 — a null `ImageVersion` still falls back to `:latest`.
  `docker run` also unconditionally passes
  `-e HomeAssistant__BaseUrl=http://host.docker.internal:8123/` for every
  agent on every host regardless of type; see "Known gaps" below.
  `stop`/`rm` run with `allowFailure: true` and `run` with
  `allowFailure: false`, so a new image that starts and immediately
  crashes leaves no old container to fall back to.
- **Firmware version needs no new code to stay accurate.** Each image
  already bakes its build's commit SHA into `Agent__FirmwareVersion` at
  `docker build` time (ADR-020's follow-up); a newly-recreated container
  is a newly-started process, so its first heartbeat after a deploy
  reports the new SHA automatically.

## Device Types: three implemented, the rest still modeled-not-implemented

[`DeviceType`](../../Vivnest.Core/Enums/DeviceType.cs) lists nine values:
`Camera`, `HumiditySensor`, `SmokeAlarm`, `WaterLeak`, `HeatPump`,
`MotionSensor`, `DoorSensor`, `SmartPlug`, `Hub` — the domain model was
written with a multi-device-type future in mind. Three now have real
capture paths:

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
generalization). What *did* carry over for free, unchanged: `PlatformDeviceHeartbeatWorker`,
`OfflineDetection`, and the whole persistence/eventing pipeline — all
already operated on generic `DeviceRuntimeState`/`DeviceEvent` fields, so
a second device type just started flowing through them without any
changes there. That's the split current-architecture predicted: the
*capture* layer is device-specific, everything downstream of it isn't.

Six device types remain modeled-not-implemented: `HumiditySensor`,
`SmokeAlarm`, `WaterLeak`, `HeatPump`, `DoorSensor`, `Hub` — no reader,
no worker, no capability behind any of them yet. (`Hub` is a partial
exception: `TapoHubLivenessWorker`/`TapoHubReachabilityChecker` treat the
H100 as a reachability target, but there is no `IHub` reader interface or
capture path in the sense the three above have.)

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

- **Inbound** (`Vivnest.Agent/Capabilities/Bridges/HomeAssistant/HomeAssistantWorker.cs`): a
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
  `HomeAssistantStateChangedHandler` (same `Capabilities/Bridges/HomeAssistant` folder)
  persists a `DeviceEvent` and publishes to the existing `DeviceEventQueue`
  — no Cloud-side code needed, same pipeline every other device event uses.
  Every event also goes through `IHomeAssistantLivenessTracker`
  (same folder), which is what actually keeps
  `DeviceHeartbeatEntity` current for HA-sourced devices (see below) — HA's
  own `state == "unavailable"` is the offline signal, everything else
  counts as evidence of reachability. `HomeAssistantWorker` also calls it
  directly (bypassing the DeviceEvent/notification pipeline) with a
  one-off REST state read per mapped entity on every successful
  (re)connect, so a status can't stay frozen across an agent restart with
  no subsequent HA event.
- **Outbound**: `IHomeAssistantCommandSender`/`HomeAssistantCommandSender`
  (`Vivnest.Agent/Capabilities/Bridges/HomeAssistant`), a typed `HttpClient` calling HA's REST
  `/api/services/<domain>/<service>` to control a device through HA (e.g.
  `switch.turn_off`). Built and manually verified; no automatic trigger
  wired to it yet (there's no motion-triggered-capture or AI-detection
  consumer built yet either — see roadmap.md Phase 5).
- **A device reachable multiple ways keeps one `DeviceId`.** The HS110 is
  monitored both directly (`SmartPlugMonitorWorker` over the Kasa
  protocol) and via HA — both write into the same `DeviceId`'s
  `DeviceEvent` timeline. `DeviceEvent` has no single-writer assumption, so
  this isn't a special case; see ADR-016 for why this doesn't collide with
  `IDeviceRuntimeStateStore`/`DeviceRuntimeState`, and for why `Devices[]` and
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
  `IHomeAssistantConnectionTracker` (`Vivnest.Agent/Capabilities/Bridges/HomeAssistant`) tracks the
  latter, surfaced as `AgentHeartbeat.HomeAssistantLastConnectedUtc`, and
  `DeviceStatusResolver` cascades any `DeviceHeartbeatSource.HomeAssistant`
  device to `Unknown` (with notifications suppressed, same as the
  agent-level cascade) once that connection's been stale past
  `HealthMonitor:HomeAssistantConnectionStaleAfter`. See ADR-016's newest
  entry in [decision-log.md](decision-log.md) for the full build, which
  was triggered by a real bug (a `localhost:8123`-inside-Docker
  misconfiguration going undetected because nothing tracked HA connection
  health at all).

## Operational Alerting (Agent errors → notifications)

**Status: IMPLEMENTED**, disabled by default, verified end to end against
real Azure on 2026-08-20. Decision and full reasoning in ADR-093.

Closes a gap that stood since ADR-027: the Agent shipped its Warning/Error
log lines to `agent-logs/{agentId}.txt` for a human to download, and nothing
reacted to them. You found out an Agent was failing by thinking to look.

### The chain

```
Agent: any Error-level log call, any category
  → AgentLogBufferLoggerProvider also writes to IAgentErrorSignalBuffer
  → PlatformErrorEventWorker drains it every 15s
      · writes an AgentEvent row (EventType = ErrorLogged)
      · publishes {PartitionKey, RowKey} to the agent-events queue
  → Cloud: AgentEventQueueFunction refetches the row
  → AgentEventQueueHandler branches on EventType
  → AgentAlertThrottle decides whether a human should be told
  → INotificationDispatcher (no new channel)
```

`Who calls it → what it calls → what it reads → what it writes`:

| Component | Called by | Calls | Reads | Writes |
|---|---|---|---|---|
| `AgentLogBufferLoggerProvider` | the logging framework, every category | `IAgentErrorSignalBuffer.Add` | — | in-memory buffer |
| `PlatformErrorEventWorker` | host (`AddHostedService`) | `IAgentEventWriter`, `IQueuePublisher` | the buffer | `tblAgentEvents`, `agent-events` queue |
| `AgentEventQueueFunction` | `agent-events` queue trigger | `IAgentEventQueueHandler` | queue message | — |
| `AgentEventQueueHandler` | the Function | `IAgentEventReader`, `IAgentAlertThrottle`, `INotificationDispatcher` | `tblAgentEvents` | — |
| `AgentAlertThrottle` | the handler | `IAgentAlertStateStore` | `tblAgentAlertState` | `tblAgentAlertState` |

### Deliberately no LLM

Roadmap Sprint 8 designed an `ILlmService` between the event and the
notification, to turn a stack trace into a triage summary. v1 forwards the
raw error instead. That is the expensive, non-deterministic part and the
hardest to judge without real traffic; adding it later changes neither the
notification type nor the payload shape.

### Throttling — the reason this was blocked before it was built

Without it a crash-looping worker turns one fault into hundreds of identical
notifications. Two independent gates, because they fail differently:

1. **Per `(agent, signature)` cooldown** (default 10 min) — suppresses the
   *same* fault repeating. Keyed on the signature rather than the agent, so a
   noisy subsystem cannot mask a different fault on the same agent.
2. **Per-agent hourly ceiling** (default 12) — the backstop for what the
   cooldown structurally cannot catch: many *distinct* errors at once, each a
   new signature, each therefore passing gate 1.

The ceiling is consumed only when a notification actually goes out, so
suppressed duplicates never eat the budget.

The signature is SHA-256 over `category|message` with GUIDs, timestamps and
bare numbers normalised to `#`. Both halves matter: without normalisation
`"capture 41 failed"` and `"capture 42 failed"` differ and the cooldown never
engages; and it is not `string.GetHashCode` because the value is persisted as
a RowKey and .NET randomises string hashing per process.

`tblAgentAlertState` holds one row per `(agent, signature)` plus one reserved
`__ceiling` row per agent, partitioned `"{TenantId}|{SiteId}|{RuntimeAgentId}"`.

### Re-entrancy, handled by construction

The error path must never log at Error. The provider fills a buffer, a
`BackgroundService` drains it, and that worker logs its own failures at
**Warning** — below its own trigger level. Otherwise a storage failure feeds
itself forever, and storage being unhappy is exactly when this most needs to
work. The buffer drops the *newest* signal when full, the opposite of
`AgentLogBuffer`'s ring, because under a crash loop the first errors are the
informative ones.

### A sibling worth knowing about: `tblDeviceSnapshotState`

Not part of alerting, but the same idea and easy to confuse with it.
`CameraCapturedHandler` uses `IDeviceSnapshotStateReader` /
`tblDeviceSnapshotState` (one row per device, holding `LastNotifiedUtc`) to
enforce `SnapshotNotification:MinInterval` — so a camera capturing every five
minutes does not produce a notification every five minutes.

Same shape as `AgentAlertThrottle`, different axis: this throttles a
*successful, expected* event per device, whereas the alert throttle
suppresses *repeated faults* per agent per error signature. Neither uses the
other, deliberately — one is about notification volume for something working,
the other about noise from something broken.

### Configuration

Off unless `OperationalAlert:Enabled`. `AzureTableAgentAlertStateStore`
builds its table client lazily for the same reason — an un-opted-in
deployment has no reason to have set `Tables:AgentAlertState`, yet the Agent
may still publish to `agent-events` from its own config. Disabled and
unconfigured is silent; enabled and unconfigured throws naming the setting.

### Verified, and what is not

Verified on live Azure by pointing a camera at an unroutable address: four
induced failures produced one notification, the fourth suppressed inside the
cooldown, no poisoned messages.

**PARTIAL — final delivery is unproven.** `Telegram__Enabled` is `false` on
`vivnestcloud2`, so no alert has reached a human. Everything up to and
including the dispatch decision is verified; delivery is not.

**RISKY — a healthy agent and a broken pipeline look identical.** Both
produce no notifications. There is no heartbeat or self-test on this path.

---

## Known gaps, risks & inconsistencies

Everything above describes what was built and why. This section is the
counterpart: what is unfinished, duplicated, unused, inconsistent or
genuinely risky, found by reading the code as of ADR-090. Nothing here is
a proposal — each item is a statement about the code as it stands.

Labels: **RISKY** (can cause real harm or data loss) · **PARTIAL** (works,
but not for the case its name implies) · **PLACEHOLDER** (exists, does
nothing) · **UNUSED** (no consumer) · **DUPLICATED** (two copies that must
be edited in lockstep) · **INCONSISTENT** (two conventions for one idea).

### Security

- **FIXED (staged) — the two Agent-facing command endpoints now
  authenticate.** They previously called no authenticator, taking
  `tenantId`/`siteId` from the query string and request body and trusting
  them: anyone knowing a TenantId, SiteId and CommandId — all visible to
  any tenant-key holder, with CommandIds returned in API responses — could
  read a command's payload and drive it to `Succeeded`, which also
  suppressed the real Agent's later update via the terminal-status guard.
  Both routes now require an **agent key**: a `tblApiKeys` row with
  `AgentId` set to one RuntimeAgentId, minted by `RegisterAsync`, written
  into the Agent's `appsettings.json` by the Updater, and required to name
  the same agent as the route. Tenant/Site come from the key, not the
  request.
  Two things worth carrying forward. First, `ApiFunctionBase` had to be
  split into `AuthenticateAsync` (rejects agent keys) and
  `AuthenticateAgentAsync` (accepts only agent keys) — without that, a key
  minted for one Agent would have authenticated against all 16 other
  Function classes, turning every Agent into a full tenant credential and
  making things worse than the hole being closed. Second, the cutover is
  staged by `AgentAuth:RequireApiKey`, default `false`: an Agent without a
  key is still honoured but logged by name. **Until that flag is turned
  on, the endpoints are still open** — a hard cutover would have silently
  broken Refresh/Apply/ExecuteCapability on every agent deployed before
  agent keys existed, because `TryFetchCommandAsync` treats a non-success
  response as "no command" and returns without executing. Flip it once
  every Agent carries a key. See §"REST API & Auth".
- **RISKY — registration hands out the full storage connection string.**
  `RegisterInstallationResponse.StorageConnectionString` (ADR-072) returns
  a credential granting read/write to every table, blob and queue in the
  account, for every tenant, in exchange for one single-use install token
  over an otherwise-unauthenticated endpoint. A leaked or intercepted
  install token is a full account compromise, not a single-agent one. The
  Agent does genuinely need storage access; a scoped SAS would bound the
  blast radius, and there is none today.
- **RISKY — `deploy-complete` is unauthenticated too.** Same
  trust-the-supplied-ids pattern. Impact is low on its own (the status
  self-corrects on the next heartbeat), but it is the precedent the two
  command endpoints above were modelled on.
- **RISKY — every Agent can read every tenant's device configuration.**
  `agent-config` and `device-config` are flat containers keyed by runtime
  id with no tenant or site prefix, and
  `TryLoadRemoteDeviceConfigsAsync` lists the *entire* `device-config`
  container, downloads every blob, and only then filters on
  `OwningAgentId`. Decryption (`CredentialCipher.DecryptInPlace`) happens
  in `TryProcessDeviceBlob` **before** the ownership check, so every Agent
  briefly holds every other site's device credentials in plaintext.
- **RISKY — API keys have no expiry or rotation.** `ApiKeyHasher.Hash` is
  a bare unsalted `SHA256.HashData`, and the resulting hash is itself the
  partition key. `ApiKeyEntity` has `Enabled` and `CreatedUtc` but no
  `ExpiresUtc` and no last-used tracking; revocation is a manual flag
  flip. The dashboard stores the raw key in `localStorage` with no
  session or refresh concept.
- **RISKY — Updater credentials are stored in plaintext on the host.**
  ACR username/password and the full storage connection string are
  written to `updater.settings.json` in the working directory.
- **INCONSISTENT — tenant-scoped keys can mutate globally-shared data.**
  `tblCapabilities`, `tblDeviceTypes`, `tblDeviceTypeCapabilities` and
  `tblCapabilityDependencies` all use a constant `PartitionKey` and carry
  no `TenantId`, yet their routes authenticate a tenant key. Any tenant
  can rename or **delete** a capability or device type every other tenant
  depends on. Invisible while there is one tenant; a data-integrity
  problem the moment there are two.

### Identity and partitioning

- **RISKY — two identity spaces, both typed `string`.**
  AgentId/RuntimeAgentId and DeviceId/RuntimeDeviceId meet in at least six
  files with nothing but comments distinguishing them. ADR-081 records one
  live bug from exactly this confusion (`ExecuteCapability` comparing a
  RuntimeAgentId against an admin AgentId, which "would never match for
  ANY real, correctly-assigned capability"). The reverse lookups added
  since fix the instances, not the class — wrapping the two spaces in
  distinct types would.
- **RISKY — event tables are not tenant-partitioned.**
  `tblDeviceEvents.PartitionKey = DeviceId` and
  `tblAgentEvents.PartitionKey = AgentId`. Tenant isolation on reads is a
  *non-key property filter* (`e.TenantId == tenantId && e.SiteId ==
  siteId`) written into each query. The isolation is real today, but any
  new query that omits those clauses leaks silently. This is the one place
  where cross-tenant safety is a coding convention rather than a storage
  guarantee.
- **INCONSISTENT — six partition-key shapes are in use.** Site-scoped
  (`{Tenant}|{Site}`), site+agent (`{Tenant}|{Site}|{Agent}`),
  tenant-scoped (`{Tenant}`), global constant, entity-keyed (DeviceId /
  AgentId) and secret-hash. The device-heartbeat shape in particular means
  re-homing a device to a different Agent orphans its old row rather than
  updating it.
- **DUPLICATED — `SiteScope` did not actually absorb all the hand-rolled
  keys.** Despite the claim in §"Tenant/Site foundation",
  `DeviceHeartbeatWriter`, `AzureTableDeviceSnapshotStateReader` and
  `ConfigurationSyncStatusService` still build the partition key by string
  interpolation.

### Configuration pipeline

- **RISKY — the four-step publish is not atomic.** Version blob →
  manifest → legacy flat blob → metadata row are four separate calls with
  no transaction. A failure between the manifest upload and the metadata
  update leaves Blob Storage advertising version N+1 while
  `tblAgentConfiguration` still says N — and `CommandDispatcher` resolves
  "the current version" from the *table*, so `RefreshConfiguration` would
  then target a version older than what the Agent will actually download.
- **RISKY — capability→code binding is a fuzzy match on an editable display
  name.** `CapabilityRuntimeProjectorLookup.Find` matches
  `capabilityName.Replace(" ","")` case-insensitively. Renaming a
  capability in the admin UI silently unbinds its projector: the
  capability drops out of every published document with only a warning,
  and the running Agent keeps its last config. There is no
  `CapabilityCode`/slug field to bind on instead.
- **RISKY — configuration is only applied by restarting the process.**
  Nothing reloads config in place. Both `RefreshConfiguration` and
  `ApplyConfiguration` return `CommandHandlerOutcome.Restart`, which calls
  `IHostApplicationLifetime.StopApplication()` and relies on Docker's
  `--restart unless-stopped`. Outside a container with that policy, the
  Agent exits and stays down.
### Deliberately kept, and findings that did not survive scrutiny

From the 2026-08 dead-code audit (`VIVNEST-DEAD-LEGACY-CODE.md`, since
retired — see git history). Recorded here because "unused" and "should be
deleted" are different claims, and because a future audit will otherwise
re-raise all of it.

**Unused but deliberately kept:**

- `IHomeAssistantCommandSender.CallServiceAsync` — no caller, but the
  containing class is live via `GetStateAsync`, and this is the intended
  outbound path for the not-yet-built HA control feature.
- `MachineStatus.Offline` — never assigned by code, but it is a persisted
  string in `tblMachines.Status` and an admin UI option, so an existing row
  could hold it.
- `AgentCommandStatus.Cancelled` — never assigned, and a live query
  confirmed no stored row uses it, so removing it would break nothing. Kept
  anyway: it is one enum member recording an intended terminal state,
  `AgentCommandStatusExtensions.IsTerminal` already handles it correctly,
  and a test asserts that. Deleting it is churn a cancel feature reverses.

**Modelled ahead of implementation, not oversights:**

- `DeviceType.HumiditySensor/SmokeAlarm/WaterLeak/HeatPump/DoorSensor` —
  deliberate multi-device-type modelling per ADR-007, and serialization
  targets: removing a member breaks deserialization of historical rows.
  `Camera`, `SmartPlug` and `MotionSensor` are the three with real capture
  paths.
- `RuntimeAgentId` / `RuntimeDeviceId` — the bridge between admin-generated
  Guids and hand-typed runtime ids. Explicitly transitional; the intended
  end state is one identity space. ADR-081 records a live bug from
  comparing one against the other.
- `LoadLocalSettings` (`Vivnest.Agent/Bootstrap/AgentConfigurationLoader.cs`) — dev-only mirror of the
  remote config fetch. Dead in every deployed configuration, live in local
  development; the Updater forces it to `false` when it writes
  `appsettings.json`.
- `scripts/update-agent.ps1` — superseded for routine deploys by
  `Vivnest.Agent.Updater` + `agent-deploy-commands` (ADR-028), but retained
  as the documented break-glass procedure, and used as exactly that on
  2026-08-20.

**Findings that were investigated and dismissed:**

- *"Three overlapping blob-storage abstractions."* Wrong. `IPhotoStorage`
  (write-only), `IBlobStorageService` (read-only) and `AzureBlobStorageClient`
  (full) have disjoint surfaces. The evidence for the claim turned out to be
  a grep matching comments.
- *"`ImageCapture` vs `Image Capture` name mismatch is a live bug."*
  Overstated. `DeviceDetail.tsx` passes the matching literal; the mismatch
  is not reachable.

- **RISKY (known, deferred) — device credentials are plaintext at rest in
  `tblDeviceRegistry.Settings`.** The same secret has three conventions:
  plaintext at rest (ADR-050, deliberate), `enc:v1:` ciphertext once
  published (ADR-085/086), and local `*.secrets.json` on the Agent
  (ADR-038). The publish path moved on; the registry table did not, so it
  is the one place an `RtspPassword` still sits in the clear — and the
  admin API returns it verbatim to any valid tenant key. Deferred pending
  a product decision, not a technical one: once encrypted at rest, the API
  either decrypts on read (UX unchanged, but the API stays the disclosure
  point) or masks (stronger, but the dashboard stops showing secrets after
  they are set). See the "Known cleanup backlog" section of
  [EVOLUTION-PLAN.md](../roadmap/EVOLUTION-PLAN.md) for the trade-off. `CredentialCipher` already does everything the fix needs.
- **PARTIAL — configuration blobs are tenant/site scoped, but the storage
  credential still is not.** Blob names now carry `{tenantId}/{siteId}/`
  (ADR-091), so the Agent startup scan and
  `DeviceCapabilitiesQueryService.BuildTriggeredByAsync` read only their
  own prefix instead of downloading every tenant's device documents and
  discarding the unwanted ones. The old flat layout is gone as of
  2026-08-21 - dual-write, read fallbacks, backfill and blobs - so this is
  the only layout, and an empty tenant or site is now rejected rather than
  selecting a second one. This is *not* a tenant boundary on its own:
  Agents hold an account-level
  `Storage:ConnectionString` and can read any prefix. Closing that means
  per-prefix SAS, which this change is the prerequisite for.
- **~~RISKY~~ FIXED — the content-hash no-op guard was defeated by
  encryption.** Both publishers used to encrypt the credential-shaped
  `Settings` keys *before* computing the content hash, and
  `CredentialCipher.Encrypt` draws a fresh random AES-GCM nonce per call.
  Identical admin data therefore hashed differently every time, so for any
  device carrying a credential-shaped key (`RtspPassword` — essentially
  every real camera) the ADR-069 no-op guard never fired: every *Publish*
  click burned a version number and restarted the owning agent. The guard
  worked only for entities with no credentials at all, which is why it went
  unnoticed for so long. Both publishers now hash the plaintext and encrypt
  afterwards, covered by `TheNoOpGuardStillHoldsWhenACredentialFieldIsPresent`
  on each side. **Operational note:** every already-published entity's
  stored `CurrentHash` was computed over ciphertext, so the first publish
  after this change bumps one version for everything and dispatches one
  restart. Self-correcting from then on.
- **~~DUPLICATED~~ FIXED — the two publishers shared one algorithm.** The
  publish cycle (read state row → compare hash → claim the next version
  with `failIfExists` → repoint manifest → rewrite the legacy flat blob →
  update the row under its ETag, retrying on 409 and 412) now lives once in
  `RuntimeConfigurationWriter<TEntity>`, along with `ComputeHash`,
  `TryGetEncryptionKey` and `TryEnqueueRestartAsync`. Each publisher
  supplies only a static `ConfigurationPublishTarget<TEntity>` (container,
  blob names, log noun, state-row factory) and two callbacks: what the
  versioned document contains, and what the legacy flat blob gets — the
  Device side reuses the versioned bytes verbatim, the Agent side
  merge-patches its own keys so an agent-local `HomeAssistant` section
  survives. A concurrency fix is now applied once.
- **DUPLICATED — projector names and adapter names are two independent
  lists.** Four `*RuntimeProjector` classes (Cloud) and four
  `*RuntimeAdapter` classes (Agent) hard-code the same four capability
  name strings in different assemblies, each with its own lookup helper.
- **PARTIAL — the last-known-good cache doesn't survive a redeploy.**
  `config-cache/devices/{id}.json` lives inside the container: a
  `docker restart` keeps it, an Updater redeploy (`rm` + `run`) discards it.
- **PARTIAL — publishing always enqueues a blind restart.**
  `TryEnqueueRestartAsync` fires on every publish and rollback regardless
  of whether the Agent is online, mid-capture, or already at that version
  — and does so through the raw queue, so it is *not* recorded in
  `tblAgentCommands` the way `CommandDispatcher`'s restarts are.
- **RISKY — sync status costs a blob round-trip per row.**
  `ConfigurationSyncStatusService` is called once per row on `GET /devices`
  and `GET /agents`; each call is 1–2 blob downloads plus 1–2 table gets,
  with no caching. A 30-device site is ~90 storage round-trips per list
  render.

### Commands

- **INCONSISTENT — four command transports coexist.** (a) `RestartAgent`
  via `agent-restart-commands` + `PlatformCommandPollingWorker`, tracked in
  `tblAgentCommands`. (b) Everything else via `agent-commands` +
  `PlatformAgentCommandPollingWorker`, with an HTTP round-trip. (c)
  `Deploy` via `agent-deploy-commands` to the Updater, **not tracked in
  `tblAgentCommands` at all** — `AgentsFunction.DeployAgent` bypasses
  `ICommandDispatcher` entirely and returns a bare 202 with no command id.
  (d) The publishers' own untracked restart enqueue, above.
- **RISKY — the Agent-side queues are shared broadcast channels.**
  `agent-commands`, `agent-restart-commands` and `agent-deploy-commands`
  are single queues every Agent polls. Each worker deletes the message
  *before* checking whether `envelope.AgentId` matches its own, so with two
  Agents polling the same queue one can consume and discard a message
  addressed to the other. The filter is described in-code as
  "load-bearing"; it is also racy.
- **PARTIAL — `ExecuteCapability` handles exactly one capability and
  doesn't await it.** `ExecuteCapabilityCommandHandler` rejects anything
  that isn't the literal `"ImageCapture"`, and for that one it publishes a
  `DeviceTriggeredEvent` and immediately returns
  `Succeeded("Capture triggered.")` — reporting success whether or not the
  capture then works. There is no correlation id linking the command to
  the `DeviceEvent` it produced. Note also the two literals for one
  concept: `"ImageCapture"` (dispatcher) vs `"Image Capture"` (projector).
- **UNUSED — `AgentCommandStatus.Cancelled` is never set** by any route,
  service or timer; it appears only in the Agent's terminal-status check.

### Test coverage that is switched off

- **`AgentConfigurationPublisherTests` and `ConfigBlobLayoutTests` are
  entirely commented out** (2026-08-22, during the capability refactor):
  22 test methods, 452 of 563 lines behind `//`, no note explaining why.
  They compile, they are discovered by nobody, and the files still read as
  populated test suites to anyone opening them — which is the part that
  makes this worse than deleting them.

  What they covered is exactly the path the capability work depends on:
  version/manifest/flat-blob writes, the content-hash no-op guard, ETag
  retry, rollback-as-a-new-version and the scoped blob layout. The suite
  reports green at 87 tests, and that number is not comparable to the 112
  before. Restore or delete them deliberately; leaving them commented is
  the one option that misleads.

- **`AgentCapabilityConfigurationLoader` is dead.** It is a byte-for-byte
  duplicate of `AgentCapabilityAssignmentFactory` and is referenced
  nowhere. Only the factory is registered.

### Dead and unwired code

- **RESOLVED — `AgentCapability` now controls what the Agent runs.**
  The join is live end to end as of 2026-08-22: projector → publisher →
  `Capabilities[]` on the agent config blob → `AgentCapabilityAssignmentFactory`
  → `RuntimeCapabilityAssignmentStore` → `CapabilityHost` selection. See
  "Capability assignment: registry vs. configuration" above for the rule and
  the failure modes. The paragraph below describes the intermediate state and
  is kept because it dates the transition.
- **~~PARTIAL — `AgentCapability` reaches the wire, but not the Agent.~~**
  As of 2026-08-22 `AgentRuntimeConfigurationProjector` *does* read
  `tblAgentCapabilities`: it resolves every `Active` assignment against the
  `Capability` catalogue and projects the result into the agent
  configuration document as `AgentCapabilityRuntimeDto`
  (`CapabilityId`/`Name`/`Enabled`), warning on an assignment whose
  capability definition is missing. That closes half of what used to be a
  fully dead join.

  **The Agent still ignores it.** Neither `AgentConfigurationLoader` nor
  `AgentOptions` binds a `Capabilities` section, so the projected list is
  carried and discarded. `RuntimeCapabilityAssignmentStore` in
  `Vivnest.Runtime` is the obvious intended consumer and is not registered
  in DI or referenced anywhere. Declaring a capability on an Agent still
  changes nothing about what that Agent does — the difference is that the
  information now arrives, and something has to pick it up.
- **RESOLVED — `ICapability`, `SnapshotScheduler`,
  `IDeviceRuntimeStateStore.TryGet`, `DeviceRuntimeStateStore.All`,
  `MessagingOptions.Transport` and
  `AzureTableDeviceEventReader.MarkProcessingAsync` were removed** in the
  dead-code pass (audit since retired; see git history).
  All six had no consumers, no persistence footprint and no ordering
  constraints.
- **PARTIAL — the DeviceEvent processing-status machinery is still
  half-wired.** With `MarkProcessingAsync` gone, `ProcessingStatus` can
  only ever hold `Completed`/`Failed` — and only for camera captures,
  since `MarkCompletedAsync`/`MarkFailedAsync` are called by
  `CameraCapturedHandler` alone. `DeviceEventQueueHandler`, which handles
  every other event type, calls neither, so the column stays permanently
  null for those. Either wire it up for all event types or drop the four
  columns; the middle state is what makes it misleading.
- **UNUSED — `MachineStatus.Offline`** is never assigned by code;
  operational offline-ness is computed separately and returned as a
  `DeviceHeartbeatStatus`. Two vocabularies for one idea.
- **INCONSISTENT — the admin `DeviceType` never reaches the runtime.**
  Agent code branches on the compiled-in `DeviceType` enum; the admin
  `DeviceTypeDefinition` a Device points at is used only for
  capability-compatibility checks and UI labels. Adding a device type in
  the admin registry produces a row nothing can execute (ADR-047 accepted
  this; it is restated here because it is easy to forget).

### Storage and runtime

- **RISKY — the Agent writes to tables Cloud also owns, without ETags.**
  Both `AgentHeartbeatWriter` (Agent) and `HealthMonitorService` (Cloud)
  write `tblAgentHeartbeat`. The Agent uses `UpsertAsync` with
  `TableUpdateMode.Replace` after a read-modify-write that copies three
  Cloud-owned fields forward — a genuine lost-update race with any
  concurrent notification-state write. Same shape for
  `tblDeviceHeartbeat`.
- **RISKY — every query is an unbounded full materialization.**
  `AzureTableStore<T>.QueryAsync` drains the whole `AsyncPageable` into a
  `List<T>`. `HealthMonitorService.RunAsync` pulls all device and agent
  heartbeats across all tenants on every tick;
  `DeviceQueryService.GetDeviceAsync(deviceId)` loads every heartbeat in
  the tenant and then `FirstOrDefault`s, an O(n) read for what could be a
  keyed lookup. No pagination or caching anywhere.
- **RISKY — blocking I/O in constructors.** `AzureTableStore<T>`'s
  constructor calls `_table.CreateIfNotExists()` synchronously; with ~20
  singletons that is ~20 blocking network calls during DI resolution, on
  the Functions cold-start path.
- **RISKY — optional integrations are eagerly resolved and can kill
  startup.** `IHomeAssistantConnectionTracker` and `INetworkUsageTracker`
  each carry comments recording that they were *found live* crashing
  High-type agents when registered conditionally.
  `HomeAssistantCommandSender` hit the same class from the other side — an
  unguarded `new Uri(settings.BaseUrl)` in its constructor crashed any
  Agent booting without a `HomeAssistant` section, which is exactly what
  self-registration produces. That one is now guarded
  (`settings.Enabled && !IsNullOrWhiteSpace(BaseUrl)`), but constructor-time
  validation plus eager resolution means the next optional integration can
  repeat it.
- **RISKY — `docker run` hard-codes the Home Assistant URL.** Every deploy
  passes `-e HomeAssistant__BaseUrl=http://host.docker.internal:8123/`
  regardless of agent type or whether HA exists on that host. This is why
  the crash above only surfaced in a bare local run, and it means an env
  var permanently shadows anything published for that key.
- **RISKY — queue names are configurable on the producer and hard-coded on
  the consumer.** The Agent publishes to
  `MessagingOptions.CameraCapturedQueue`; `CameraCapturedFunction` triggers
  on the literal `"camera-captured"`. Same for `agent-heartbeats`,
  `device-heartbeats`, `device-events` and `classify-requests`. They agree
  only because `common-config.json` happens to match. Change a name in
  shared config and the pipeline breaks silently — the Agent writes to a
  new queue, the Function keeps listening to the old one, nothing errors.
- **RISKY — generic event dispatch is bound at compile time.**
  `EventDispatcher.PublishAsync<TEvent>` resolves handlers from the
  *static* type of the argument. Publishing through a base-class or
  `object` reference resolves zero handlers and completes successfully —
  no handler, no error, no log. There is also no retry, no dead-letter,
  and fan-out order is DI registration order (load-bearing for
  `CameraCaptureCompletedEvent`, expressed nowhere).
- **RISKY — capability workers exit permanently when idle at startup.**
  `CameraCaptureWorker`, `SmartPlugMonitorWorker` and `HomeAssistantWorker`
  snapshot their device list once and `return` if empty. Consistent with
  restart-only config application today, but a worker that logged "nothing
  to do" is dead for the process lifetime, not idle.
- **INCONSISTENT — `agent-logs` has no retention.**
  `{agentId}.txt` is a single ever-growing blob per agent, overwritten
  wholesale each flush. Device and agent events each have a retention
  timer; log blobs have none and there is no rotation.
- **INCONSISTENT — `DeviceEventTypes` mixes `const` and `static`.**
  Fifteen members are `const string`; `CameraCaptureFailed`,
  `SmartPlugReadingFailed` and `MotionSensorReadingFailed` are
  `public static string` — mutable, and unusable in a `switch` case.

### Process

- **Automated tests barely exist.** There is now one project,
  `Vivnest.Tests` (xunit, in the solution), but it covers `Vivnest.Core`
  primitives only: the device-config runtime adapter, the event RowKey
  format, free-text name matching, and command-status terminality. It was
  seeded from real defects — the `capabilities[]` shape that made the
  Capabilities tab return blank devices, and the host-culture RowKey that
  renders 2026 as 2569 on a Buddhist-calendar host — rather than written
  for coverage.
  Everything else is still untested: no Cloud service, no repository, no
  Agent worker and no Function has a test. Every finding in this document
  was found by reading, and most still would not be caught by anything
  running today. Treat a green `dotnet test` as "the shared primitives did
  not regress", not "the system works".
- **This document drifts.** The renames, DTO fields, SAS lifetime, device
  type count and auth tiers corrected in this pass had all been stale for
  at least one phase. The structural cause is that the body is written by
  accretion — a new ADR appends a bullet rather than rewriting the
  statement it supersedes, so several sections describe a state that no
  longer exists while a later section describes the state that replaced it
  (`CapabilityIds` and the flat-blob-only publish are both in the document
  twice, in both forms). Per [CLAUDE.md](../../CLAUDE.md), doc updates
  belong in the same change as the code that invalidates them; the
  practical addition is to *edit the superseded bullet* rather than only
  appending a new one.
