# Vivnest — Dead / Legacy Code Analysis

**Repo:** `Vivnest2` · **HEAD:** `c797e9e` (ADR-090) · **Scope:** all six C#
projects in `Vivnest.slnx`, plus `Vivnest.Dashboard`, `tools/`, `scripts/`,
`devops/`.

**Status: 6 of the 9 DEAD items have since been removed** (see
"Actioned" below). The remaining three are deliberately retained and each
says why. Nothing in the LEGACY, TRANSITIONAL, DUPLICATE or UNCERTAIN
sections has been touched — those need design decisions, not deletions.

### Actioned — removed in commit following this report

| Item | What was removed | Verification |
|---|---|---|
| D1 | `Vivnest.Agent/Interfaces/ICapability.cs` (whole file) | no references anywhere |
| D2 | `Vivnest.Agent/Capabilities/SnapshotScheduler.cs` (whole file) | empty class, no references |
| D4 | `AzureTableDeviceEventReader.MarkProcessingAsync` | not on `IDeviceEventReader`, unreachable through DI, no callers ever (`git log -S`) |
| D5 | `ICaptureStatusStore.TryGet` + its implementation | all 12 consumers use `GetOrAdd` |
| D6 | `MessagingOptions.Transport` + the `Messaging:Transport` key in `common-config.json` | zero C# readers |
| D9 | `DeviceEventProcessingStatus.Processing` | only writer was D4; enum is never parsed, only `.ToString()`-ed |
| — | `CaptureStatusStore.All` (**not in the original report**) | public property not on the interface, no callers — found while editing the file |

`Vivnest.Core`, `Vivnest.Infrastructure`, `Vivnest.Cloud`,
`Vivnest.Cloud.Functions` and `Vivnest.Agent` all build clean with zero
warnings afterwards. `Vivnest.Agent.Updater` could not be rebuilt (a
running instance held a file lock) but references none of the removed
symbols.

**Two corrections to this report, from re-verification before deleting:**

1. **D9 was wrongly classified as unsafe.** The original text said enum
   members "may exist as persisted strings in live tables."
   `DeviceEventProcessingStatus` is only ever written via `.ToString()`
   and **never parsed back**, so no stored value can break — it was safe
   to remove. `AgentCommandStatus` and `MachineStatus` *are* parsed with
   `Enum.Parse` (which throws), so the caution was correct for D7/D8 and
   wrong only for D9.
2. **D7's stated reason was imprecise.** `MachineStatus.Offline` is not
   "never assigned by any code path" — `PUT machines-admin/{machineId}`
   accepts any valid `MachineStatus`, validated by
   `MachinesFunction.cs:164`, so an operator can set it. No *automatic*
   transition sets it. That makes it more clearly retained, not less.

**Two method gaps worth noting**, both found while actioning the report
rather than while writing it:

1. The original pass scanned type-level references and interface methods.
   It did **not** scan public members of concrete classes that aren't on
   any interface — which is how `MarkProcessingAsync` (caught by hand) and
   `CaptureStatusStore.All` (missed entirely) slipped through.
2. The `Vivnest.Dashboard` scan checked only exported *functions* in
   `api.ts`, not exported types, and not `icons.tsx` at all. A correct
   re-scan (count references excluding the declaration line, not excluding
   the declaring file — intra-file type usage is real usage) finds **five**
   dead exports:

   | File | Export | Orphaned by |
   |---|---|---|
   | `api.ts` | `getDeviceCaptures` | `1c5a7a1` — superseded by the by-day gallery variant |
   | `api.ts` | `getInstallationsByMachine` | `b3006d2` |
   | `icons.tsx` | `IntervalIcon`, `HeartbeatIcon` | `78de32a` |
   | `icons.tsx` | `LiveFeedIcon` | `9ea2767` — **today's** sidebar/tabbed-detail redesign |

   **Deliberately not removed.** `LiveFeedIcon` went dead in a commit from
   the current working session, so the Dashboard is mid-redesign and these
   may be about to be re-used. Deleting them would be stepping on live
   work for no benefit — they cost nothing to keep. Worth a second look
   once that redesign settles.

Any repeat of this analysis should add both axes.

---

**Original report follows, unmodified except for the status markers
above.** Nothing below is a deletion recommendation; several entries are
explicitly *not safe* to remove.

---

## Method, and what it can't tell you

531 type declarations were extracted across 469 `.cs` files and
cross-referenced against every other file. Reference counting alone
produces large numbers of false positives in this codebase, so each
zero/low-reference candidate was then checked by hand against the
mechanisms that make a type live without a direct textual reference:

| Mechanism | Why a name grep misses it | Example caught here |
|---|---|---|
| Azure Functions attribute discovery | `[Function]` classes are instantiated by the host, never referenced | All 20 HTTP + 5 queue + 4 timer Function classes showed 0 refs and are **ACTIVE** |
| DI resolution by interface | Handler/service classes are named once (registration) and resolved by interface | All 15 `IEventHandler<T>` implementations show exactly 1 ref (`Program.cs`) and are **ACTIVE** |
| Extension-method call syntax | `entity?.ToModel()` never mentions the declaring class | `AgentHeartbeatMapping` / `DeviceHeartbeatMapping` showed 0 refs and are **ACTIVE** |
| `IConfiguration` binding | Options types are bound by shape, properties read reflectively | `BurstOptions`, `SensorOptions`, `DeviceSettings` — all **ACTIVE** |
| Generic store instantiation | `AzureTableStore<T>` makes every `ITableEntity` live | All 22 entity types **ACTIVE** |
| JSON (de)serialization | Wire records are matched by property name | Queue messages, DTOs, `*WireDocument` records — **ACTIVE** |
| File-local `internal` records | Declared and used in one file | `AgentConfigWireDocument`, `CommandStatusCheck`, `RegistrationBootstrap` — **ACTIVE** |

**Limits of this pass.** It is static only. It cannot tell you whether a
legacy blob still exists in the live storage account, whether a
config key is set in the deployed environment but not in the repo's
sample files, or whether an operator still runs a superseded script by
hand. Items depending on those are classified **UNCERTAIN** with the
runtime check named.

### Result summary

| Classification | Count |
|---|---|
| DEAD | 9 |
| LEGACY | 7 |
| TRANSITIONAL | 5 |
| DUPLICATE | 8 |
| UNCERTAIN | 4 |

Everything not listed below was confirmed **ACTIVE** — reachable from a
worker tick, a Function trigger, a DI-resolved interface, or configuration
binding.

---

## 1. DEAD

No executable path, registration, configuration reference, reflection
usage, serialization usage, startup usage or external trigger could be
identified.

### D1 — `ICapability`

- **File:** `Vivnest.Agent/Interfaces/ICapability.cs`
- **Class:** `ICapability` (interface)
- **Method:** `Name`, `StartAsync`, `StopAsync`
- **References/callers:** none — zero implementations, zero usages anywhere
- **DI registration:** none
- **Configuration references:** none
- **Function/worker entry point:** none
- **Tables / Queues / Blobs:** none
- **Runtime path:** none
- **Related newer implementation:** `IEventHandler<T>` + `EventDispatcher`
  are the actual dispatch mechanism; `ICommandHandler` is the actual
  command-handler contract
- **Reason:** Aspirational stub for the Capability Host described in
  `vivnest-runtime-overview.md`, which does not exist. Also the only type
  in the codebase declared in the **global namespace** — the file has no
  `namespace` statement, which is itself evidence it was never wired in.
- **Verdict:** OLD AND UNUSED
- **Confidence:** HIGH

### D2 — `SnapshotScheduler`

- **File:** `Vivnest.Agent/Capabilities/SnapshotScheduler.cs`
- **Class:** `SnapshotScheduler` (`internal`)
- **Method:** none — the class body is empty
- **References/callers:** none
- **DI registration / Configuration / Trigger / Tables / Queues / Blobs:** none
- **Runtime path:** none
- **Related newer implementation:** scheduling lives inline in
  `CameraCaptureWorker.RunCaptureLoopAsync` (interval + burst window from
  `DeviceRuntimeState.BurstUntilUtc`/`BurstInterval`)
- **Reason:** Placeholder that was never filled in; the responsibility was
  implemented elsewhere instead.
- **Verdict:** OLD AND UNUSED
- **Confidence:** HIGH

### D3 — `IHomeAssistantCommandSender.CallServiceAsync`

- **File:** `Vivnest.Agent/Capabilities/Bridges/HomeAssistant/IHomeAssistantCommandSender.cs`
  (impl: `HomeAssistantCommandSender.cs`)
- **Class:** `IHomeAssistantCommandSender` / `HomeAssistantCommandSender`
- **Method:** `CallServiceAsync(domain, service, entityId, ct)`
- **References/callers:** none. The *interface* is live —
  `HomeAssistantWorker.SyncLivenessAsync` calls the sibling
  `GetStateAsync` — but `CallServiceAsync` has no caller in any project.
- **DI registration:** `Program.cs` line ~249,
  `AddHttpClient<IHomeAssistantCommandSender, HomeAssistantCommandSender>()`
  (Low-type agents only)
- **Configuration references:** `HomeAssistant:BaseUrl`, `:AccessToken`
- **Function/worker entry point:** — (would be reached from
  `HomeAssistantWorker`, but isn't)
- **Tables / Queues / Blobs:** none
- **Runtime path:** none for this method
- **Related newer implementation:** none — no replacement exists; the
  outbound half of the HA bridge was simply never wired to a trigger
- **Reason:** `current-architecture.md` states this openly ("Built and
  manually verified; no automatic trigger wired to it yet"). Confirmed:
  still true at ADR-090.
- **Verdict:** OLD AND UNUSED — but **do not remove**. The containing class
  is live (`GetStateAsync`), and this is the intended outbound path for
  the not-yet-built HA control feature.
- **Confidence:** HIGH

### D4 — `AzureTableDeviceEventReader.MarkProcessingAsync`

- **File:** `Vivnest.Cloud/Repositories/AzureTableDeviceEventReader.cs:159`
- **Class:** `AzureTableDeviceEventReader`
- **Method:** `MarkProcessingAsync`
- **References/callers:** none. Notably it is **not declared on
  `IDeviceEventReader`**, so no DI consumer can even reach it — every
  consumer holds the interface.
- **DI registration:** the class is registered
  (`IDeviceEventReader → AzureTableDeviceEventReader`), the method is not
  reachable through it
- **Tables:** `tblDeviceEvents` (would write `ProcessingStatus = Processing`)
- **Queues / Blobs:** none
- **Runtime path:** none
- **Related newer implementation:** its siblings `MarkCompletedAsync` /
  `MarkFailedAsync` *are* on the interface and *are* called — but only by
  `CameraCapturedHandler`
- **Reason:** Half-built event-processing state machine. The "Processing"
  state is never entered, so `ProcessingStatus` only ever holds
  `Completed`/`Failed`, and only for camera captures —
  `DeviceEventQueueHandler`, which handles every other event type, calls
  none of the three.
- **Verdict:** REPLACED BUT NOT YET REMOVED (the ambition was per-event
  processing tracking; what shipped is partial)
- **Confidence:** HIGH

### D5 — `ICaptureStatusStore.TryGet`

- **File:** `Vivnest.Core/Camera/Stores/ICaptureStatusStore.cs`
  (impl: `CaptureStatusStore.cs:14`)
- **Class:** `ICaptureStatusStore` / `CaptureStatusStore`
- **Method:** `bool TryGet(string deviceId, out DeviceRuntimeState status)`
- **References/callers:** none. All 12 consumers use `GetOrAdd` exclusively.
- **DI registration:** `Program.cs`,
  `AddSingleton<ICaptureStatusStore, CaptureStatusStore>()`
- **Tables / Queues / Blobs:** none (in-memory `ConcurrentDictionary`)
- **Runtime path:** none for this method
- **Related newer implementation:** `GetOrAdd` — every call site wants
  create-if-absent semantics, so the probe variant has no use
- **Reason:** Interface surface that no consumer ever needed.
- **Verdict:** OLD AND UNUSED
- **Confidence:** HIGH

### D6 — `MessagingOptions.Transport`

- **File:** `Vivnest.Core/Options/MessagingOptions.cs`
- **Class:** `MessagingOptions`
- **Method:** property `Transport`
- **References/callers:** **none in any C# file.** Set to
  `"AzureStorageQueues"` in `Vivnest.Agent/common-config.json` and shipped
  to every Agent, read by nothing.
- **DI registration:** the class is bound
  (`Configure<MessagingOptions>`), this property is never read
- **Configuration references:** `Messaging:Transport` in
  `common-config.json` (and therefore in the deployed
  `shared-config/common-config.json` blob)
- **Tables / Queues / Blobs:** none
- **Runtime path:** none
- **Related newer implementation:** none — the transport is hard-committed
  to Azure Storage Queues via `AzureQueuePublisher` and
  `QueueServiceClient`
- **Reason:** MVP-era pluggable-transport placeholder. Changing this value
  has no effect whatsoever, which is a live foot-gun: it reads as a
  supported switch.
- **Verdict:** OLD AND UNUSED
- **Confidence:** HIGH

### D7 — `MachineStatus.Offline`

- **File:** `Vivnest.Core/Enums/MachineStatus.cs`
- **Reason:** Never assigned by any code path. Machine operational state is
  computed separately by
  `AgentInstallationManagementService.GetMachineOperationalStatusAsync`
  and returned as a `DeviceHeartbeatStatus`, a different enum.
- **Related newer implementation:** `DeviceHeartbeatStatus` (ADR-076)
- **Verdict:** REPLACED BUT NOT YET REMOVED — **do not remove the member**:
  it is a persisted string in `tblMachines.Status` and an admin UI option,
  so an existing row could hold it.
- **Confidence:** HIGH

### D8 — `AgentCommandStatus.Cancelled`

- **File:** `Vivnest.Core/Enums/AgentCommandStatus.cs`
- **Reason:** Never assigned. No route, service or timer transitions a
  command to `Cancelled`; it appears only in the Agent's `IsTerminal`
  string-literal set in `PlatformAgentCommandPollingWorker.cs:239`.
- **Related newer implementation:** `CommandExpiryService` sweeps stale
  commands to `Expired` instead
- **Verdict:** OLD AND UNUSED (aspirational — a cancel feature was
  designed for but never built)
- **Confidence:** HIGH

### D9 — `DeviceEventProcessingStatus.Processing`

- **File:** `Vivnest.Core/Enums/DeviceEventProcessingStatus.cs`
- **Reason:** Only written by `MarkProcessingAsync` (D4), which has no
  callers. Unreachable transitively.
- **Verdict:** REPLACED BUT NOT YET REMOVED
- **Confidence:** HIGH

---

## 2. LEGACY

Old implementation still referenced or executed, superseded by newer
architecture.

### L1 — Legacy flat configuration blobs (`{id}.json`)

- **File:** `Vivnest.Cloud/Admin/AgentRuntimeConfigurationPublisher.cs:272-285`,
  `DeviceRuntimeConfigurationPublisher.cs:296`,
  `Vivnest.Agent/Program.cs:343-345` and `:563-586`
- **Class:** both publishers; `TryLoadRemoteConfigAsync` /
  `TryLoadRemoteDeviceConfigsAsync`
- **Method:** `WriteVersionAsync` (write side), the legacy-name loop
  (read side)
- **References/callers:** every publish and every Agent startup
- **Blobs:** `agent-config/{runtimeAgentId}.json`,
  `device-config/{runtimeDeviceId}.json`
- **Tables:** `tblAgentConfiguration` / `tblDeviceConfiguration` track the
  *new* path only
- **Runtime path:** publish → merge-patch write of the flat blob (after
  the version blob and manifest); Agent startup → manifest first, flat
  blob on 404
- **Related newer implementation:** `{id}/versions/{n}.json` +
  `{id}/current.json` manifest (ADR-069)
- **Reason:** Deliberate "run alongside" dual-write. Still written on every
  publish and still read as fallback.
- **Verdict:** **OLD BUT STILL EXECUTED** — and see L2 before considering
  retirement.
- **Confidence:** HIGH

### L2 — `DeviceCapabilitiesQueryService` reads the legacy path *only* — **FIXED, and this entry was wrong**

> **Correction.** This entry described a latent risk ("correct today, breaks
> when the dual-write is retired"). It was in fact a **live bug**. The
> publisher writes the *same* `capabilities[]` bytes to both the versioned
> blob and the flat name — the flat *name* was kept, the flat *shape* was
> not. So `Deserialize<DeviceOptions>` on a republished device bound only
> `OwningAgentId` and the three `Configuration*` fields; `Name`, `Type`,
> `Enabled`, `Location`, `Brand`, `Model`, `Firmware` (under `Device`) and
> `Schedule`/`Trigger`/`SinkCleanliness`/`ObjectDetection`/`Sensors`
> (inside `Capabilities`) all silently defaulted. The endpoint returned a
> blank name and `Type = Camera` for any device, with no derived
> capabilities — a wrong answer, not an error.
>
> Shape-checking the five real cached device documents showed four still
> legacy and one (`55cc8aa6`, Kitchen Camera) already new-shaped — which is
> both why the bug was real and why it went unnoticed. **This also answers
> U3 below: yes, deployed blobs still use the pre-`Capabilities` shape.**
>
> **Fix applied:** `DeviceConfigRuntimeAdapter` and the four
> `ICapabilityConfigRuntimeAdapter` implementations moved from
> `Vivnest.Agent/Runtime/Configuration` to `Vivnest.Core/Configuration`
> (they had no Agent-only dependencies), and
> `TryLoadDeviceAsync` now runs `Adapt` before deserializing. Verified
> against all five real documents: the new-shape device goes from
> `name='' type=Camera enabled=False` to
> `name='Kitchen Camera' enabled=True host='192.168.50.166' sink=yes`, and
> all four legacy documents are byte-identical before and after.
>
> **Still open:** the read is still against the flat blob name, so
> retiring the dual-write (L1) *does* still require switching this to
> manifest-first. That was left undone deliberately — manifest-first costs
> two blob GETs per device instead of one on an endpoint that already does
> an O(N) scan, and it buys nothing while the dual-write exists.

- **File:** `Vivnest.Cloud/Api/DeviceCapabilitiesQueryService.cs:114` and `:299`
- **Class:** `DeviceCapabilitiesQueryService`
- **Method:** the blob reads backing `GET /devices/{deviceId}/capabilities`
- **References/callers:** `DeviceCapabilitiesFunction` (HTTP trigger)
- **DI registration:** `IDeviceCapabilitiesQueryService` in
  `Vivnest.Cloud/DependencyInjection/ServiceCollectionExtensions.cs:86`
- **Blobs:** `DeviceConfigBlob.BlobName(deviceId)`,
  `AgentConfigBlob.BlobName(executingAgentId)` — the **flat** names, with
  no manifest attempt and no fallback
- **Function entry point:** `GET /devices/{deviceId}/capabilities`
- **Related newer implementation:** `ConfigurationSyncStatusService` and
  `Program.cs` both do manifest-first with flat fallback; this service
  does not
- **Reason:** Written before ADR-069 and never migrated. It is correct
  today *only because* L1's dual-write keeps the flat blob current. This
  is the single hard dependency that makes the legacy write path
  non-removable — retiring L1 without fixing this silently breaks the
  dashboard's Capabilities tab.
- **Verdict:** **OLD BUT STILL EXECUTED**, and **STILL REQUIRED BY THE
  CURRENT RUNTIME** in its legacy form
- **Confidence:** HIGH

### L3 — `DeviceConfigRuntimeAdapter` legacy-shape branch

- **File:** `Vivnest.Agent/Runtime/Configuration/DeviceConfigRuntimeAdapter.cs`
- **Class:** `DeviceConfigRuntimeAdapter`
- **Method:** `Adapt` — the branch taken when no top-level `Capabilities`
  key is present
- **References/callers:** `Program.cs.TryProcessDeviceBlob` (every device
  blob, every Agent startup)
- **Blobs:** every `device-config/*` blob
- **Related newer implementation:** the `capabilities[]` shape produced by
  `DeviceRuntimeConfigurationProjector` + the four
  `ICapabilityConfigRuntimeAdapter`s
- **Reason:** Pass-through for any device blob never republished through
  the Phase 6 pipeline.
- **Verdict:** **OLD BUT STILL EXECUTED** — removable only once every
  device in every environment has been republished (see U3)
- **Confidence:** HIGH

### L4 — `Vivnest.Core.Camera.Stores` namespace holding device-generic state

- **File:** `Vivnest.Core/Camera/Stores/CaptureStatusStore.cs`,
  `DeviceRuntimeState.cs`, `ICaptureStatusStore.cs`
- **Class:** `CaptureStatusStore`, `DeviceRuntimeState`
- **References/callers:** 12 files spanning Camera, SmartPlug,
  MotionSensor, HomeAssistant, TapoHub and DeviceHealth
- **DI registration:** `Program.cs`,
  `AddSingleton<ICaptureStatusStore, CaptureStatusStore>()` — unconditional
- **Runtime path:** every capability worker's liveness bookkeeping
- **Related newer implementation:** none — the type is right, only its
  name and namespace are wrong
- **Reason:** MVP naming from when the platform was camera-only. `Capture`
  and `Camera` are now misnomers: a smart plug reading and a Home
  Assistant state change both write here.
- **Verdict:** **STILL REQUIRED BY THE CURRENT RUNTIME** (legacy naming
  only, zero behavioural risk)
- **Confidence:** HIGH

### L5 — `scripts/update-agent.ps1`

- **File:** `scripts/update-agent.ps1`
- **References/callers:** none in code; invoked by hand, if at all
- **Related newer implementation:** `Vivnest.Agent.Updater` +
  `agent-deploy-commands` queue (ADR-028) — `current-architecture.md`
  states the Updater automates "the same four commands
  `scripts/update-agent.ps1` already runs by hand"
- **Reason:** Manual predecessor of the automated deploy path.
- **Verdict:** REPLACED BUT NOT YET REMOVED
- **Confidence:** MEDIUM — it may still be the documented break-glass
  procedure; that's an operational question, not a static one

### L6 — Publishers' raw restart enqueue — **FIXED**

> Both publishers now take `ICommandDispatcher` instead of
> `IAgentCommandPublisher` and dispatch a tracked `RestartAgent` command,
> so a publish-triggered restart appears in `tblAgentCommands` like every
> other restart since ADR-079. `RequestedBy` distinguishes the trigger:
> `"ConfigPublish"` or `"ConfigRollback"` (vs `"Dashboard"` for the
> operator-initiated route).
>
> Still best-effort — a publish that already succeeded never fails because
> the restart couldn't be dispatched — but two dispatcher outcomes exist
> now that the raw enqueue didn't have, and both are logged rather than
> surfaced: a **null result** (owning Agent not resolvable for the tenant,
> i.e. no heartbeat — nothing running to restart) and an **`AGENT_BUSY`
> rejection** (another disruptive command in flight, which will pick up
> this config when it restarts). Both are behaviour changes from the
> unconditional blind enqueue, and both are improvements, but they are
> changes.

- **File:** `Vivnest.Cloud/Admin/AgentRuntimeConfigurationPublisher.cs:359-370`
  (`TryEnqueueRestartAsync`), mirrored in `DeviceRuntimeConfigurationPublisher`
- **Method:** `TryEnqueueRestartAsync` →
  `IAgentCommandPublisher.PublishRestartCommandAsync`
- **Queues:** `agent-restart-commands`
- **Tables:** **none** — this restart is *not* recorded in `tblAgentCommands`
- **Related newer implementation:** `ICommandDispatcher.DispatchAsync`
  (ADR-079), which persists a command row before enqueueing
- **Reason:** Pre-ADR-079 fire-and-forget path, left in place when
  restarts became tracked commands everywhere else. A publish-triggered
  restart is therefore invisible in command history.
- **Verdict:** **OLD BUT STILL EXECUTED**
- **Confidence:** HIGH

### L7 — `AgentsFunction.DeployAgent` bypassing the dispatcher — **BLOCKED, not a cleanup**

> Attempted alongside L6 and deliberately stopped. This is not a
> one-file change; it is cross-process feature work, because **the Updater
> has no way to report command status back to Cloud**:
>
> 1. `DeployCommandQueueMessage` carries `{AgentId, IssuedAtUtc, ImageVersion?}`
>    and **no `CommandId`** — unlike `RestartCommandQueueMessage`, which
>    already has one.
> 2. The Updater has no status-reporting code at all. Its only two Cloud
>    calls are `register` and `deploy-complete`, both in the registration
>    flow.
> 3. **The blocker:** `registrationUrl` is a *local variable* in
>    `TryRegisterFromInstallTokenAsync`, used once and discarded.
>    `WriteUpdaterSettingsFromRegistration` persists `Agent:AgentId`,
>    `Messaging:ConnectionString` and `Messaging:DeployCommandQueue` — no
>    Cloud base URL. So on a later poll-driven deploy the Updater
>    physically cannot call back.
>
> Routing Deploy through `ICommandDispatcher` without fixing all three
> would make things **worse**, not better: every deploy would create a
> command row that never leaves `Dispatched` and then gets swept to
> `Expired` by `CommandExpiryTimerFunction` after 5 minutes — reporting
> failure for deploys that actually succeeded. Untracked-but-honest beats
> tracked-and-wrong.
>
> Minimum real scope: persist a Cloud base URL into `updater.settings.json`
> at registration; add `CommandId` to `DeployCommandQueueMessage`; add a
> `DeployAgent` command type; give the Updater the fetch/report round-trip
> `PlatformAgentCommandPollingWorker` already has. Worth doing — it is the
> last untracked command path — but it needs its own change and probably
> its own ADR.

- **File:** `Vivnest.Cloud.Functions/Http/AgentsFunction.cs:292-339`
- **Method:** `DeployAgent`
- **References/callers:** `POST /agents/{agentId}/deploy` (dashboard)
- **Queues:** `agent-deploy-commands` via
  `IAgentCommandPublisher.PublishDeployCommandAsync`
- **Tables:** none — no `tblAgentCommands` row is created
- **Related newer implementation:** every sibling route on the same class
  (`restart`, `refresh-config`, `apply-config`, `execute-capability`) goes
  through `ICommandDispatcher` and returns an `AgentCommandDto`
- **Reason:** Deploy predates Phase 9 and was never migrated. It returns a
  bare `202` with no command id, so a deploy cannot be tracked, expired or
  correlated the way every other command can.
- **Verdict:** **OLD BUT STILL EXECUTED**
- **Confidence:** HIGH

---

## 3. TRANSITIONAL

Intentionally retained while migrating from the MVP architecture to the
new domain model.

### T1 — `AgentCapability` / `tblAgentCapabilities`

- **File:** `Vivnest.Core/Domain/AgentCapability.cs`,
  `Vivnest.Core/DataStores/Entities/AgentCapabilityEntity.cs`,
  `Vivnest.Cloud/Admin/AgentCapabilityAssignmentService.cs`,
  `Vivnest.Cloud/Repositories/AzureTableAgentCapabilityStore.cs`
- **Method:** `AssignAsync`, `UnassignAsync`, `GetByAgentAsync`
- **References/callers:** `AgentCapabilitiesAdminFunction` (3 routes) and
  the dashboard only
- **DI registration:** `IAgentCapabilityStore`,
  `IAgentCapabilityAssignmentService`
- **Tables:** `tblAgentCapabilities`
- **Function entry point:** `GET/POST agent-capabilities-admin/*`
- **Related newer implementation:** none yet — the runtime equivalent is
  the hard-coded `if (agentType == AgentType.Low/High)` blocks in
  `Program.cs`
- **Reason:** Full admin CRUD with **no runtime consumer whatsoever**.
  Neither projector reads it; neither `Program.cs` branch knows it exists.
  Assigning a capability to an Agent changes nothing about what that Agent
  does. This is the admin half of a migration whose runtime half is not
  built.
- **Verdict:** REPLACED BUT NOT YET REMOVED — or, more accurately,
  *arrived before its replacement*. Removing it would discard the
  data model the Capability Host is meant to consume.
- **Confidence:** HIGH

### T2 — Modeled-not-implemented `DeviceType` members

- **File:** `Vivnest.Core/Enums/DeviceType.cs`
- **Members:** `HumiditySensor`, `SmokeAlarm`, `WaterLeak`, `HeatPump`,
  `DoorSensor` — zero C# references outside the enum; each referenced once
  in `Vivnest.Dashboard/src` (label/icon maps). `Hub` is a partial
  exception (`TapoHubLivenessWorker` treats it as a reachability target,
  but there is no `IHub` reader).
- **Tables:** persisted as strings in `tblDeviceEvents.DeviceType` and
  `tblDeviceHeartbeat.DeviceType`
- **Related newer implementation:** none — `Camera`, `SmartPlug`,
  `MotionSensor` are the three with real capture paths
- **Reason:** Deliberate multi-device-type modelling ahead of
  implementation (ADR-007).
- **Verdict:** STILL REQUIRED BY THE CURRENT RUNTIME — they are
  serialization targets; removing a member would break deserialization of
  any historical row.
- **Confidence:** HIGH

### T3 — Plaintext credentials in `Device.Settings` / `DeviceRegistryEntity.Settings`

- **File:** `Vivnest.Core/Domain/Device.cs`,
  `Vivnest.Core/DataStores/Entities/DeviceRegistryEntity.cs`,
  `Vivnest.Cloud/Admin/DeviceService.cs`
- **Tables:** `tblDeviceRegistry.Settings` (JSON string)
- **Related newer implementation:** `CredentialCipher.EncryptFields` on the
  *publish* path (ADR-085/086) and local-only `*.secrets.json` on the
  Agent (ADR-038)
- **Reason:** ADR-050 deliberately accepted plaintext credentials in Table
  Storage, returned verbatim by the admin API to any valid tenant key,
  while the publish path encrypts. Three different credential-handling
  conventions coexist for the same secrets.
- **Verdict:** STILL REQUIRED BY THE CURRENT RUNTIME (the publish pipeline
  reads these to encrypt them)
- **Confidence:** HIGH

### T4 — `RuntimeAgentId` / `RuntimeDeviceId` identity-mapping fields

- **File:** `Vivnest.Core/Domain/Agent.cs`, `Device.cs`, and every
  reverse lookup (`IAgentRegistryStore.GetByRuntimeAgentIdAsync`)
- **Reason:** The bridge between admin-generated Guids and hand-typed
  runtime ids. Explicitly transitional by design — the intended end state
  is one identity space. Both are `string`, and ADR-081 records a live bug
  from comparing one against the other.
- **Verdict:** STILL REQUIRED BY THE CURRENT RUNTIME
- **Confidence:** HIGH

### T5 — `LoadLocalSettings` local-dev configuration path

- **File:** `Vivnest.Agent/Program.cs:62-66`, `:888-954`
  (`TryLoadLocalSharedConfig`, `TryLoadLocalConfig`)
- **Configuration references:** `LoadLocalSettings` in
  `Vivnest.Agent/appsettings.json` (currently `false`), and forced to
  `false` by the Updater when it writes a fresh `appsettings.json`
  (`Agent.Updater/Program.cs:307`, `:498`)
- **Blobs:** none when active — reads local `common-config.json` /
  `{agentId}.json` from disk instead
- **Reason:** Dev-only mirror of the remote fetch, reading identically
  shaped files from disk. Dead in every deployed configuration; live in
  local development.
- **Verdict:** STILL REQUIRED (development only) — not dead, but never
  executed in production
- **Confidence:** HIGH

---

## 4. DUPLICATE

### U-D1 — Three blob-storage abstractions — **THIS ENTRY WAS WRONG**

> **Retracted.** The original text claimed "three layers for one
> responsibility, with no rule for which to use," and that
> "`DeviceQueryService` alone references `IBlobStorageService`,
> `AzureBlobStorageClient` *and* `AzureBlobStorage`." Both claims are
> false, and acting on this entry as written would have meant a pointless
> cross-assembly refactor.
>
> The `DeviceQueryService` claim was a grep artifact: it matches
> `AzureBlobStorageClient` and `AzureBlobStorage` only in **comments**
> (lines 25-26). The file injects exactly one thing, `IBlobStorageService`.
> `Vivnest.Cloud` has **zero** reference to `Vivnest.Infrastructure`, so it
> could not use `AzureBlobStorage` even if it wanted to.
>
> What is actually there is a defensible layering with **disjoint**
> surfaces, not three ways to do one thing:
>
> | Type | Surface | Consumers |
> |---|---|---|
> | `AzureBlobStorageClient` (Core) | Upload, Download, OpenRead, ListBlobNames, GenerateReadSasUri | the shared low-level client both sides wrap |
> | `IPhotoStorage` (Core/Infrastructure) | **write-only** — `UploadAsync` and nothing else | 1: `CameraCaptureService` |
> | `IBlobStorageService` (Cloud) | **read-only** — Download, OpenRead, ListBlobNames, GenerateReadSasUri; **no Upload** | 5 |
>
> The apparent Cloud split — `Admin/` using the raw client while
> `Api/`+`Handlers/` use the interface — is therefore a real capability
> boundary, not an accident: the two publishers each call `UploadAsync`
> three times, and `IBlobStorageService` has no write method at all.
>
> **One genuine nit, fixed:** `ConfigurationSyncStatusService` sat on the
> `Admin/` side of that split while making only two `DownloadAsync`
> calls — reaching past the read interface for no reason. Switched to
> `IBlobStorageService`.
>
> **Residual, not worth churn:** `IPhotoStorage` is an MVP-era name — a
> generic one-method blob-upload interface called "photo storage", with a
> single consumer. Renaming it is cosmetic.
>
> **Downgraded:** DUPLICATE → mostly justified layering.
> **Confidence in the original entry: was HIGH, should have been LOW.**

### U-D2 — The two runtime-configuration publishers — **FIXED**

> This entry said the blocker was "no tests". That was wrong, and saying it
> twice delayed the work: the real blocker was that **the publishers had no
> seams**. Every storage dependency was a concrete type -
> `AzureBlobStorageClient`, and `AzureTableStore<T>` constructed inline in
> the constructor - and `AzureTableStore<T>`'s constructor calls
> `CreateIfNotExists()`, so merely *building* a publisher reached Azure. No
> amount of test project would have helped.
>
> Seams now exist: `IBlobStorageClient` (extracted from the concrete client
> in `Vivnest.Core.Storage`, mirroring its surface exactly so the existing
> class implements it unchanged), plus `IAgentConfigurationStore` /
> `IDeviceConfigurationStore` and `IAgentEventStore` / `IDeviceEventStore`
> in Cloud. Those last four also close the "two tables with no repository"
> inconsistency recorded elsewhere in this report, and give Cloud a proper
> home for the event writes U-D8 flagged.
>
> 12 tests now cover the Agent publisher: monotonic versioning, immutable
> version blobs, the manifest pointer, the content-hash no-op guard, the
> ADR-087 Name-only change that must *defeat* that guard, ETag-412 retry,
> warnings blocking publish, a missing encryption key blocking publish
> rather than falling back to plaintext, audit event + restart dispatch,
> and rollback creating a new version without mutating the old one.
>
> **The de-duplication is now done.** The shared algorithm lives once, in
> `Vivnest.Cloud/Admin/RuntimeConfigurationWriter.cs`: the read-row →
> compare-hash → claim-the-next-version-with-`failIfExists` →
> repoint-manifest → rewrite-flat-blob → update-row-under-ETag cycle, with
> its 409 and 412 retries, plus the three verbatim-identical helpers
> (`ComputeHash`, `TryGetEncryptionKey`, `TryEnqueueRestartAsync`). Each
> publisher now supplies only what genuinely differs:
>
> | Differs | Carried by |
> |---|---|
> | container + blob names, log noun, state-row construction | `ConfigurationPublishTarget<TEntity>`, one static field per publisher |
> | what the versioned document contains | `buildVersionJson` callback |
> | what the legacy flat blob gets | `buildFlatJson` callback - Device reuses the versioned bytes verbatim, Agent merge-patches its own keys so an agent-local `HomeAssistant` section survives |
>
> Supporting changes: `IConfigurationStateEntity` on both config entities
> (the sliver of row shape the pipeline reads); `IConfigurationStateStore<T>`
> with `IAgentConfigurationStore`/`IDeviceConfigurationStore` kept as
> derived names so `CommandDispatcher` and the DI lines still read as the
> specific thing they mean; and `AzureTableConfigurationStore<T>` collapsing
> the two near-identical repository wrappers into a base plus two
> constructors. Non-comment lines across the three files: 663 → 619, but
> the number that matters is that the ~150-line retry algorithm went from
> two copies to one.
>
> The Device side got its own 14 tests at the same time, rather than
> trusting "it's the same code now" - including the one behaviour that
> genuinely differs (the flat blob being the versioned bytes verbatim) and
> the one the Agent side cannot express (a device with a null
> `OwningAgentId` publishes fine and restarts nothing). 66 tests pass.
>
> Worth recording, because it nearly became a false bug report: the first
> version of the ETag-retry test failed, and the cause was the *test*. It
> threw 412 without advancing the stored row, which Azure cannot do - a 412
> means a competing publisher already moved it. With the row frozen, the
> publisher re-reads the same version, recomputes the same next version,
> and collides with the version blob its own failed attempt just wrote.
> The retry loop is correct; the fake was not.

### U-D2a — The content-hash no-op guard is defeated by encryption — **OPEN, live defect**

- **Files:** `Vivnest.Cloud/Admin/DeviceRuntimeConfigurationPublisher.cs`,
  `AgentRuntimeConfigurationPublisher.cs`
- **Classification:** ACTIVE, but wrong.
- **Found by:** the Device tests added alongside the U-D2 de-duplication.
  Pre-dates that work - the ordering has been this way since ADR-085.
- **Reason:** both publishers encrypt the credential-shaped Settings keys
  **first**, then hash the result. `CredentialCipher.Encrypt` draws a fresh
  random AES-GCM nonce on every call, so byte-identical admin data produces
  a different hash every single time. For any device carrying a
  credential-shaped key - `RtspPassword`, i.e. essentially every real
  camera - the no-op guard in ADR-069 never fires: every dashboard
  *Publish* click burns a version number, writes a new immutable blob, and
  dispatches a restart of the owning agent. The guard works only for
  devices with no credentials at all, which is why it was never noticed.
- **Fix:** hash the plaintext content, then encrypt for the wire document.
  The hash is meant to answer "did the admin change anything", and
  ciphertext is not admin data.
- **Migration note:** every already-published entity's stored `CurrentHash`
  was computed over ciphertext, so the first publish after the fix bumps
  one version for everything. Benign, and self-correcting from then on.
- **Confidence:** HIGH (pinned by
  `DeviceConfigurationPublisherTests.RepublishingIsNotANoOpWhileACredentialFieldIsPresent`).

### U-D2 (original entry) — The two runtime-configuration publishers

- **Files:** `Vivnest.Cloud/Admin/AgentRuntimeConfigurationPublisher.cs`,
  `DeviceRuntimeConfigurationPublisher.cs` (~420 lines each)
- **Shared methods:** `WriteVersionAsync`, `ComputeHash`,
  `TryGetEncryptionKey`, `TryEnqueueRestartAsync`, `LoadExistingBlobAsync`,
  `WriteAuditEventAsync`
- **Reason:** The source comments say "mirrored here" and "see the Device
  publisher for the full reasoning." Any concurrency or retry fix must be
  applied twice.
- **Confidence:** HIGH

### U-D3 — Capability name lists, Cloud vs Agent — **PARTLY FIXED, and the entry undercounted**

> This entry named two duplicated lookups. There were **four** copies of
> the matching rule, not two — the entry missed the DeviceType pair:
>
> | Site | Matches |
> |---|---|
> | `CapabilityRuntimeProjectorLookup` (Cloud) | capability name → projector |
> | `CapabilityConfigRuntimeAdapterLookup` (Core) | capability name → adapter |
> | `DeviceRuntimeConfigurationProjector.MatchRuntimeDeviceType` | admin DeviceTypeName → `DeviceType` enum |
> | `MotionDetectionRuntimeProjector` (own private copy) | the same DeviceType match again |
>
> All four ran the identical rule — strip spaces, compare
> `OrdinalIgnoreCase` — and their comments openly said so ("mirrors …
> exactly", "same convention … already uses"). That is the shape of
> duplication that drifts silently here: the failure mode is not an
> exception, it is a capability quietly dropping out of a published
> document with only a warning.
>
> **Fixed:** one `RuntimeNameMatch` helper in `Vivnest.Core/Configuration`
> (`Normalize`, `Matches`, generic `Find<T>`, `ToDeviceType`), used by all
> four. `ToDeviceType` deliberately matches against `Enum.GetNames` rather
> than `Enum.TryParse`, because `TryParse` also accepts the underlying
> numeric value — an admin DeviceTypeName of `"0"` would otherwise
> silently resolve to `Camera`. Both original sites used `GetNames`; that
> is preserved exactly.
>
> Differential-tested against the pre-consolidation logic across 33
> comparisons (spacing, casing, double spaces, leading/trailing spaces,
> numeric strings, empty, unknown): **0 differences**.
>
> **Still open:** the eight `CapabilityName` string literals themselves
> (four projectors in Cloud, four adapters in Core) remain independent
> hard-coded values. Adding a capability still means two matching edits in
> two assemblies. Unifying those needs a shared capability registry, which
> is the same design question as T1 — not a de-duplication.

- **Files:** `Vivnest.Cloud/Admin/CapabilityProjection/*RuntimeProjector.cs`
  (4 classes) vs `Vivnest.Agent/Runtime/Configuration/*RuntimeAdapter.cs`
  (4 classes), plus two lookup helpers
  (`CapabilityRuntimeProjectorLookup`, `CapabilityConfigRuntimeAdapterLookup`)
- **Reason:** The same four hard-coded capability-name strings in two
  assemblies with two independent fuzzy-match lookups. Adding a capability
  requires two identical edits with nothing enforcing agreement.
- **Confidence:** HIGH

### U-D4 — `"ImageCapture"` vs `"Image Capture"` — investigated, **not a live bug**

> Traced end to end: `DeviceDetail.tsx:136` passes the literal
> `"ImageCapture"`, which matches `AgentCommandTypes.ImageCaptureCapabilityId`
> exactly, so `CommandDispatcher.ValidateAsync`'s Built-in branch and
> `ExecuteCapabilityCommandHandler` both hit. The chain works today.
>
> It remains a real smell, and a sharper one than "two spellings": the
> `CapabilityId` field carries **two different id spaces**. For
> ImageCapture it is the magic string `"ImageCapture"`; for any Derived
> capability it is a real `tblCapabilities` RowKey (a GUID). A caller
> can't tell which to send from the field name, and the string is
> hard-coded in three places across two languages (the Core constant, the
> dashboard literal, and the projector's `"Image Capture"` display name
> used for a different purpose entirely). Fixing it means deciding what
> `CapabilityId` means — a design call, not a rename, so left open.

- **Files:** `Vivnest.Core/Constants/AgentCommandTypes.cs`
  (`ImageCaptureCapabilityId = "ImageCapture"`) vs
  `ImageCaptureRuntimeProjector.CapabilityName` (`"Image Capture"`)
- **Runtime path:** `CommandDispatcher.ValidateAsync` and
  `ExecuteCapabilityCommandHandler` compare against the first; the
  projection pipeline matches on the second
- **Reason:** Two string literals for one concept, in two assemblies. The
  projector lookup happens to whitespace-strip, so they *would* match
  there — but the command path uses `StringComparison.Ordinal` against the
  unspaced form only.
- **Confidence:** HIGH

### U-D5 — Command status vocabulary in three places — **FIXED**

> The three copies were the terminal/non-terminal partition of
> `AgentCommandStatus`: `AgentCommandManagementService.IsTerminal` (enum),
> `PlatformAgentCommandPollingWorker.IsTerminal` (string literals), and
> `CommandExpiryService.ExpirableStatuses` — which was the *exact
> complement*, spelled out as its own `HashSet`. Three edits needed to add
> a status, each failing differently if missed: Cloud would re-apply it,
> the timer would expire it out from under itself, and the Agent would
> re-execute it.
>
> Now one definition — `AgentCommandStatusExtensions.IsTerminal` in
> `Vivnest.Core/Enums`, beside the enum it partitions. All three call it.
> The Agent still transacts status as plain strings over HTTP (that
> convention is unchanged); it parses first and treats an unparseable
> status as non-terminal, exactly as the literal set did, so a Cloud
> returning an unknown status doesn't cause the Agent to silently discard
> a command.
>
> Note this does **not** address U-D4 below — `"ImageCapture"` vs
> `"Image Capture"` is a different duplication and remains open.
>
> **Correction: the first pass at this missed a fourth copy.** There were
> *four* definitions, not three — `PlatformCommandPollingWorker.TryIsAlreadyResolvedAsync`
> had its own `detail.Status is "Succeeded" or "Failed" or "Expired" or
> "Cancelled"` inside the pre-restart liveness check, which the original
> consolidation commit left behind. Found while reading the same file for
> U-D6 and now routed through `AgentCommandStatusExtensions` as well, with
> the same treat-unparseable-as-non-terminal behaviour (fails open and
> restarts, matching what the literal set did). The repo now has zero
> hand-written terminal-status sets.

- **Files:** `Vivnest.Core/Enums/AgentCommandStatus.cs`;
  `PlatformAgentCommandPollingWorker.cs:239` (literal set
  `"Succeeded" or "Failed" or "Expired" or "Cancelled"`); plain status
  strings over HTTP in both directions
- **Reason:** Documented as deliberate ("transacting in plain status
  strings over HTTP"), but it means adding a status requires three
  coordinated edits.
- **Confidence:** HIGH

### U-D6 — Two Agent command-polling workers — **FIXED (two of three copies)**

> There were **three** copies of this skeleton, not two:
> `PlatformCommandPollingWorker`, `PlatformAgentCommandPollingWorker`, and
> `DeployPollingWorker` in `Vivnest.Agent.Updater`. Each hand-wrote: guard
> on the queue name being configured → `CreateIfNotExists` → poll every 15s
> at `maxMessages: 10` → **delete before processing** → deserialize →
> null-check → discard anything not addressed to this Agent.
>
> That skeleton is load-bearing rather than incidental, which is what made
> it worth extracting: delete-before-process is a deliberate non-retrying
> design (ADR-024), and the AgentId filter is what stops one Agent acting
> on another's messages on these shared broadcast queues. **The filter runs
> after the delete**, so two Agents polling concurrently can have one
> consume and discard a message meant for the other — the race recorded
> elsewhere in this report. That race is unchanged by this extraction, but
> it now has exactly one place to be fixed instead of three.
>
> **Fixed:** `QueuePollingWorkerBase<TMessage>` in
> `Vivnest.Agent/Runtime/Shell`. Subclasses supply the queue name and
> setting name, this Agent's id, a log noun, an `AgentIdOf` selector, and
> `HandleAsync`. Net −182/+25 across the two workers.
>
> Verified beyond compilation: the real Agent host was started and both
> workers log exactly what they logged before —
> `Command Polling Worker started, polling agent-restart-commands every
> 00:00:15.` and `Agent Command Polling Worker started, polling
> agent-commands every 00:00:15.` — with no exceptions and 0 build
> warnings.
>
> **Third copy deliberately left:** `DeployPollingWorker` lives in its own
> assembly, and `Vivnest.Core` carries no `Microsoft.Extensions.Hosting`
> reference, so there is nowhere the Agent and the Updater could share a
> `BackgroundService` base from without adding that package to a project
> the Cloud stack also consumes. Note this is a *package* constraint, not
> the target-framework split — that was a red herring.

- **Files:** `Vivnest.Agent/Runtime/Shell/PlatformCommandPollingWorker.cs`,
  `PlatformAgentCommandPollingWorker.cs`
- **Queues:** `agent-restart-commands` and `agent-commands` respectively
- **Reason:** Same poll loop, same delete-before-process semantics, same
  envelope deserialization, same agent-id filter. The second's header
  comment calls itself "a deliberate sibling, not a rewrite." They already
  share `CommandStatusUpdateBody` across files.
- **Confidence:** MEDIUM — the split is defensible (different envelope
  shapes, different post-processing); the loop scaffolding is not
- **Related:** see L6/L7 — a third and fourth restart/deploy path exist
  outside both workers

### U-D7 — `BaseIdentity` vs `ISiteScoped`

- **Files:** `Vivnest.Core/Domain/BaseIdentity.cs`,
  `Vivnest.Core/Domain/ISiteScoped.cs`
- **Reason:** Two mechanisms carrying the same TenantId/SiteId/AgentId
  triple — an abstract base with `required init` for the four telemetry
  models, an interface for the 13 aggregates.
- **Confidence:** MEDIUM

### U-D8 — Agent event writers vs Cloud's raw `AzureTableStore<T>` — **PARTLY FIXED**

> **Fixed: the RowKey format.** `{timestamp}-{uniquifier}` was written out
> at six call sites across two assemblies (two Agent-side writers, two
> publisher audit writes, two in `HealthMonitorService`). All six agreed,
> but nothing made them agree — and RowKey doubles as the time-range
> filter for `IDeviceEventReader.GetByDeviceAndDateRangeAsync`, so a
> single site drifting would silently break date-range queries for the
> rows it wrote rather than throwing. Now one `EventRowKey` helper in
> `Vivnest.Core/DataStores`, used by all six. The uniquifier still differs
> by caller on purpose: Agent-originated rows pass their existing
> `EventId`, Cloud-originated rows mint one.
>
> **Latent culture bug found and fixed while verifying.** Every original
> site used an interpolated `$"{t:yyyyMMddHHmmssfff}"`, which formats
> under `CurrentCulture` — and `yyyy` is the year *in that culture's
> calendar*. Measured directly: the instant 2026-08-17 renders as
> `25690817…` under `th-TH` (Buddhist) and `14480304…` under `ar-SA`
> (Umm al-Qura) — a different date entirely, not just a different year.
> Such rows would sort and range-filter wrongly against every row written
> elsewhere. The helper pins `CultureInfo.InvariantCulture`. Verified as a
> byte-for-byte no-op on every Gregorian culture, so no existing row is
> affected; it only changes output on hosts where the old behaviour was
> already wrong.
>
> **Still open: two behavioural asymmetries, deliberately not changed.**
>
> 1. **The `Enabled` flag only half-applies.** The Agent's writers respect
>    `DeviceEvents:Enabled` / `AgentEvents:Enabled`; Cloud's four direct
>    write sites check neither (`HealthMonitorService` references neither
>    options type). The *reader* is gated. So with
>    `DeviceEvents:Enabled=false`, Cloud still writes DeviceOffline/
>    DeviceRecovered rows that nothing can read — they accumulate
>    invisibly. Currently theoretical, since both flags are `true` in
>    `common-config.json`, but the flag is incoherent as written.
> 2. **Add vs Upsert.** Agent writers use `AddEntityAsync` (throws on
>    duplicate — correct for an append-only table); Cloud uses
>    `UpsertAsync` with `TableUpdateMode.Replace` (silently overwrites).
>    Equivalent in practice because the RowKey embeds a fresh Guid, but
>    the two express opposite intents about whether a collision is a bug.
>
> Both are behaviour changes rather than cleanups, so they are left for a
> decision rather than folded into a de-duplication commit. The underlying
> cause of the split is unchanged and structural: `Vivnest.Cloud` has no
> `Vivnest.Infrastructure` reference, so it cannot use
> `IDeviceEventWriter`/`IAgentEventWriter` at all.

- **Files:** `Vivnest.Infrastructure/DataStores/AzureTableDeviceEventWriter.cs`
  / `AzureTableAgentEventWriter.cs` (Agent side) vs
  `Vivnest.Cloud/Services/HealthMonitorService.cs:31-32` and both
  publishers, which construct `AzureTableStore<DeviceEventEntity>` /
  `<AgentEventEntity>` directly
- **Tables:** `tblDeviceEvents`, `tblAgentEvents`
- **Reason:** Two write paths into the same two tables, with different
  RowKey generation and no shared validation. The source comment explains
  the cause (`Vivnest.Cloud` has no `Vivnest.Infrastructure` reference) —
  it's a project-reference constraint, not a design choice.
- **Confidence:** HIGH

---

## 5. UNCERTAIN — requires runtime verification

### U1 — Do legacy flat blobs still back any live entity?

L1/L3 are removable only if **every** Agent and Device in **every**
environment has been republished through the ADR-069 pipeline. Static
analysis cannot see the storage account.
**Check:** list `agent-config/` and `device-config/`; for each `{id}.json`
confirm a sibling `{id}/current.json` exists with a matching hash.

### U2 — Is `Messaging:Transport` set anywhere outside the repo?

D6 is confirmed unread by code, but the deployed
`shared-config/common-config.json` blob is not in the repo.
**Check:** download the live blob and confirm the key is inert there too
before treating it as removable configuration surface.

### U3 — Do any deployed device blobs still use the pre-`Capabilities` shape?

Determines whether L3's legacy branch is genuinely exercised in production
or only theoretically reachable.
**Check:** inspect each `device-config/*` blob for a top-level
`Capabilities` key.

### U4 — `tools/object-detection-tester`, `tools/sink-cleanliness-tester` — **RESOLVED: ACTIVE**

> Ran the check this entry called for. Both projects build clean against
> the current `Vivnest.Agent`/`Vivnest.Core` — including after this
> session's adapter move to `Vivnest.Core.Configuration` — so they have
> not rotted and are live dev harnesses, not dead code.
>
> The underlying risk stands and is unchanged: neither is in
> `Vivnest.slnx`, so a normal solution build never compiles them. They can
> break silently at any time; today they simply haven't. Adding them to
> the solution (or to whatever CI exists) would convert that from luck
> into a guarantee.

- **Files:** `tools/*/Program.cs`, `*.csproj`
- **DI registration / triggers:** none — standalone `Main` entry points
- **Reason:** Both `ProjectReference` `Vivnest.Agent` and `Vivnest.Core`,
  but **neither is listed in `Vivnest.slnx`**, so a solution build never
  compiles them. They are reverse dependencies that can silently rot
  against Agent API changes and nothing will fail.
- **Verdict:** likely ACTIVE-as-dev-harness, but unverifiable statically
- **Check:** `dotnet build tools/object-detection-tester` — if it no longer
  compiles, they are effectively dead.
- **Confidence:** MEDIUM

---

## 6. MVP mechanism verdicts

Direct answers for the areas named in the request.

| Mechanism | Verdict | Notes |
|---|---|---|
| `appsettings.json` (Agent) | **STILL REQUIRED** | Bootstrap tier — `AgentId`, `TenantId`, `SiteId`, `Storage:ConnectionString`, `CredentialEncryption:Key`, `CloudApiBaseUrl`. Correctly gitignored; the account key in the working copy is not in version control. |
| Device configuration JSON | **STILL REQUIRED** | The `capabilities[]` shape is current (ADR-064/069). |
| `device-config` container | **STILL REQUIRED** | But flat, un-scoped, and listed in full by every Agent. |
| Legacy flat `{id}.json` blobs | **OLD BUT STILL EXECUTED** | Dual-written and read as fallback; L2 depends on them exclusively. |
| Camera configuration | **STILL REQUIRED** | `DeviceOptions.Schedule`/`Trigger`/`SinkCleanliness`/`ObjectDetection` all bound and read. |
| Agent configuration (`agent-config`) | **STILL REQUIRED** | Manifest path current; flat path legacy-but-live. |
| Old Device models | **NONE FOUND** | `Device.CapabilityIds` (ADR-057) and `Agent.CapabilityIds` (ADR-059) were genuinely removed from code — only `current-architecture.md` still described them, now corrected. |
| Old DeviceId handling | **STILL REQUIRED** | `DeviceId` vs `RuntimeDeviceId` is T4 — transitional by design, both live. |
| Old capability models | **REPLACED BUT NOT YET REMOVED** | `AgentCapability` (T1) has admin CRUD and zero runtime consumers. |
| Old heartbeat implementations | **NONE FOUND** | One writer and one reader per level; both mapping helpers live via extension syntax. Dual-writer race (Agent + Cloud) is a correctness issue, not a legacy one. |
| Old event implementations | **PARTIALLY REPLACED** | `DeviceEventProcessingStatus` machinery is half-wired (D4/D9). Event write path itself is current. |
| Old repositories | **NONE FOUND** | All 19 `AzureTable*Store` types are registered and resolved. Two tables (`tblAgentConfiguration`, `tblDeviceConfiguration`) have no repository at all — inconsistent, not legacy. |
| Old configuration services | **OLD BUT STILL EXECUTED** | `DeviceCapabilitiesQueryService` (L2) never migrated to manifest-first. |

---

## 7. Removal-order note

Nothing here should be removed in isolation. The one real ordering
constraint found:

**L2 must be migrated to manifest-first before L1's dual-write can be
retired.** `DeviceCapabilitiesQueryService` is the only consumer that
reads the legacy flat blob with no fallback, so dropping the flat write
first breaks `GET /devices/{deviceId}/capabilities` silently — a 404
swallowed into an empty capabilities list, not an error.

The genuinely inert items — D1 (`ICapability`), D2 (`SnapshotScheduler`),
D5 (`TryGet`), D6 (`Messaging:Transport`) — have no consumers, no
persistence footprint and no ordering constraints. D3, D7, D8 and D9 look
inert but are not safe to delete: D3 is the intended outbound HA path, and
D7/D8/D9 are enum members that may exist as persisted strings in live
tables.
