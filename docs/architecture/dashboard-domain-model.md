# Dashboard Domain Model — Vocabulary and Relationships

**Status:** Reference document, reconciling the dashboard's intuitive
mental model (Tenant → Site → Agent → Devices → Capabilities →
Schedule/Triggers) against what's actually decided (binding ADRs) and
actually built (real code) today. Nothing here authorizes building a Site
table, a Capability Host, a Schedule entity, or a Brand catalog — those
stay conditional on the triggers named in §9.

## 1. Definitions

| Term | Status today | Definition |
|---|---|---|
| **Tenant** | String prefix only (`PartitionKey = "{TenantId}\|{SiteId}"`), no table | A customer account. Resolved per-request from an API key into `TenantContext` (`Vivnest.Cloud/Auth/TenantContext.cs`). Never a row. |
| **Site** | String prefix only, no table, no page — [ADR-018](decision-log.md) explicitly rejected a Site entity/page | A physical location under a tenant. Same status as Tenant: a partition-key prefix and a field on `TenantContext`/heartbeats. Stays this way — confirmed still fine as-is, not hitting a real limitation yet. |
| **Agent** | Heartbeat/event rows only (`AgentHeartbeatEntity`, `AgentEventEntity`), no registry table | One running `Vivnest.Agent` process. Known to the system only through the heartbeats/metrics it emits — no `Agents[]` config or CRUD row independent of a heartbeat having arrived. |
| **Device** | Config-only (`DeviceOptions`) + heartbeat/event rows, no registry table | One physical sensor/actuator an agent talks to. Identity (`DeviceId`) is asserted in agent-local config, not issued by a cloud registry. |
| **Capability** | Real: `Vivnest.Agent/Capabilities/{Type}/` folders + thin `ICapability` lifecycle interface; no Capability Host/registry yet | A distinct **ability** ("capture," "toggle power," "detect motion") plus whatever it takes to exercise that ability for one kind of device — connection, worker loop, state, events. See §3. |
| **Bridge** | Real, implemented (ADR-016/019/026): `Capabilities/Bridges/HomeAssistant/` | An alternate transport that can carry potentially many device types through one shared connection — orthogonal to Capability, not a kind of it. |
| **Native vs. Bridged** | Real, implemented | Two coexisting connection paths to the *same* `DeviceId`, chosen by a symmetric `Enabled` flag. Exactly one path active at a time; both can exist in config simultaneously. |
| **Health** (liveness) | Real, nuanced ([ADR-005](decision-log.md)) | `AgentHeartbeat` (periodic, unconditional) vs. `DeviceHeartbeat` (event-driven, on-change) vs. cloud-side `IDeviceStatusResolver` combining both into Online/Offline/Warning/Unknown. |
| **Metrics** | Real, deliberately separate from Health ([ADR-020](decision-log.md)) | Operational telemetry (CPU/memory/bandwidth, power/battery/voltage/current) — different table, different worker, different failure domain from Health so a metrics bug can never make something look offline. |
| **Schedule** | Not yet built as a general concept. `SnapshotScheduler.cs` is a real file but an empty stub — a reserved name, not a design. | See §6 for the design worked out below. |
| **Trigger** | Real, but narrow ([ADR-021](decision-log.md)) | `DeviceOptions.TriggersDeviceIds` — a one-hop device-to-device link ("device A's event makes device B act"), resolved by `MotionTriggerResolverHandler` into a `DeviceTriggeredEvent`, consumed by `CaptureOnTriggerHandler`. Not a general rules engine — deliberately declined for now. |
| **Brand** | Does not exist as an entity. Free-text `Brand` field only inside `SmartPlugState` (a *reading*, not a catalog). | No decision has been made about a Brand catalog. Free text stays sufficient — nothing today needs brand-specific behavior. |
| **DeviceType** | Enum (`Camera, HumiditySensor, SmokeAlarm, WaterLeak, HeatPump, MotionSensor, DoorSensor, SmartPlug`) | Stays an enum — cheap to extend, already gates handler/UI dispatch correctly. Promote to a catalog table only if tenant-editable device-type metadata is ever needed. |
| **"Setup"** | No mapping — open question | Doesn't correspond to anything in the code. Either "an agent's device config" (no new noun needed) or a future dashboard onboarding wizard (a UI flow, not a data entity). Not resolved yet — brainstorm live when it comes up. |
| **IMonitorable** (Agent+Device unified shape) | Real, partial: `Vivnest.Cloud/Api/Dtos/IMonitorable.cs`, implemented by `AgentSummaryDto`/`DeviceSummaryDto` | `Id`, `Status`, `StatusSinceUtc`, `LastHeartbeatUtc` only — Metrics deliberately left out (CPU/memory vs. power/battery don't share a shape). No aggregating query/endpoint/UI yet — see §7. |
| **Scenario** | Not built — named concept, not designed | A user-facing, named outcome ("Front Door Monitoring," "Kitchen Sink scenario") composed from one or more `(Device, Capability)` pairs, activated by one or more Triggers. Sits *above* Capability, doesn't redefine it. See §8. |

## 2. The real persisted hierarchy today

```
TenantId | SiteId          → string prefix, no row
  └─ AgentId                 → AgentHeartbeatEntity / AgentEventEntity rows only, no registry
       └─ DeviceId           → DeviceOptions (config) + DeviceHeartbeatEntity / DeviceEventEntity rows, no registry
            ├─ DeviceType    → enum
            ├─ Connection path: Native (Devices[] entry) XOR HomeAssistant-bridged (Enabled flag picks active)
            ├─ Health: DeviceHeartbeatEntity.Status, resolved by IDeviceStatusResolver
            ├─ Metrics: DeviceEvent rows (PowerReading, BatteryStatus)
            ├─ Schedule: per-capability cadence config (see §5)
            └─ Trigger: TriggersDeviceIds + MotionBurstInterval/Duration
```

## 3. What is a Capability — concretely

A capability is **a distinct ability** — the verb: "capture an image," "toggle power," "detect motion," "report battery level." It's never abstract in Vivnest, though: an ability doesn't exist independent of what it takes to exercise it for one kind of device — the connection, the worker loop that acts on it, the state it tracks, the events it produces. So the module bundles ability + machinery, which is why it shows up in code as a whole folder (`Capabilities/{Camera,SmartPlug,MotionSensor,DeviceHealth}/`) rather than just a method name. No two capability folders share code — [ADR-015](decision-log.md) confirmed this by trying to reuse `ICamera` for `ISmartPlug` and finding it didn't fit, because the abilities themselves don't overlap.

Three things the informal vocabulary tends to conflate:

- **Capability = an ability + the machinery to exercise it.** Maps to `Capabilities/{Type}/`.
- **Bridge = no ability of its own.** Not a capability — a transport/protocol adapter that carries some *other* capability's state through an alternate path. Home Assistant doesn't capture, toggle, or detect anything itself; it's a pipe. Nested one level deeper: `Capabilities/Bridges/{Name}/`.
- **Trigger/Schedule = timing, not a new ability.** They decide *when* an existing ability fires; they don't add a new one. They only become their own capability when the scheduled/triggered thing genuinely is a different ability (battery reporting vs. capturing — §6).

**The "is it a Capability?" test:** is this a distinct ability the system doesn't already have, requiring its own connection/worker/state/events to exercise? If yes → new capability. If it's a second transport for an ability that already exists → Bridge. If it's just timing for an ability a capability already has → Schedule/Trigger config, not a new capability.

This directly answers "Camera Capture, How?": Camera is the capability, RTSP-via-ffmpeg is the *how* inside it — there's no separate "capture" concept above it.

**Optional abilities within one capability ("sub-capabilities").** Not every device of a type supports the same abilities — a PTZ-capable camera vs. a static one, an energy-monitoring smart plug vs. a basic one. Not modeled as a capability hierarchy (the runtime already rules out capabilities nesting or referencing each other directly) — two shapes instead, chosen by whether the extra ability needs its own connection:

- **Same connection → optional interface, not a new capability.** E.g. `IPtzControl` alongside `ICamera` on the same concrete class, implemented only if the hardware supports it, declared per-device via a config flag (mirroring the `Enabled` pattern already used for Native/Bridged), checked via feature-detection inside the existing worker.
- **Different connection/failure domain → a sibling capability, not a child.** If the optional ability can fail independently of the main one, it's a peer capability targeting the same `DeviceId` — the same way `DeviceHealth` already applies across every device type without nesting under any of them.

No real precedent for this yet — `ICamera`/`ISmartPlug`/`IMotionSensor` are all single-ability today. Vocabulary for when it comes up, not something to build now.

**Worked example: a camera that both captures and detects motion.** Applying the test above:
- Software motion detection on the same RTSP stream Camera already pulls → same connection → an optional ability on the existing Camera capability (a second event type, `MotionDetected`, alongside whatever it already produces), not a new capability.
- Onboard hardware motion detection pushed via a separate channel (e.g. a distinct ONVIF event subscription) → different connection → a genuine sibling `MotionSensor` capability instance targeting the *same* `DeviceId` as the `Camera` instance.

Either way, `DeviceId` stays singular. The likely config shape (not built) mirrors Native/Bridged (§1) — one `DeviceId`, two coexisting `DeviceOptions` entries (one under `Cameras[]`, one under `MotionSensors[]`), except both are simultaneously active rather than mutually exclusive, since they're two different abilities rather than two paths to one ability.

## 4. Example capabilities

Naming the verb first is the test: if you can't say the ability in one verb ("capture," "toggle," "detect leak," "notify"), it's probably a Bridge, a platform service, or still too vague to be a capability.

**Real, implemented today (device-level ability):**

| Capability | Ability |
|---|---|
| Camera | capture an image |
| SmartPlug | read/toggle power state |
| MotionSensor | detect motion / read sensor state |
| DeviceHealth | assess whether a device is reachable/alive (cross-cutting — applies to every device type, still its own ability, own worker, own events) |

**Real, implemented today (agent-level ability, not tied to a device):**

| Capability | Ability |
|---|---|
| AgentHeartbeat | report the agent process's own liveness |
| AgentMetrics | report the agent's own resource usage (CPU/memory/bandwidth) |

**Named but unimplemented (device-level, from the `DeviceType` enum — placeholders, no connection/worker/events yet):**

| Capability | Ability |
|---|---|
| HumiditySensor | read humidity level (agriculture) |
| SmokeAlarm | detect smoke/fire (home) |
| WaterLeak | detect a leak (home, agriculture) |
| HeatPump | read/control heating state (industrial) |
| DoorSensor | detect open/closed (home, commercial) |

**Real abilities that aren't device capabilities, but pass the same test:**

| Capability | Ability |
|---|---|
| Telegram Notifications | deliver a notification to a human |
| Email | deliver a notification to a human via a different channel |

**From [vivnest-runtime-overview.md](vivnest-runtime-overview.md)'s target "named capabilities" list — reclassified, not capabilities under this definition** (that list predates the "capability = a distinct ability" framing sharpened here):

| Named in the target list | Actually is |
|---|---|
| MQTT, Home Assistant, BLE, Zigbee, Modbus, BACnet, ONVIF | **Bridges** — no ability of their own, carry some other capability's state through an alternate transport |
| Cloud Sync, Licensing, Statistics, OTA, Mesh Networking, Dashboard API | **Platform/runtime services** — not an ability exercised on or for a device, general plumbing |
| Offline Detection | Same ability as **DeviceHealth** above — an older name for it, not a separate one |
| Snapshot Scheduler | **Schedule/Trigger machinery** — *when*, not an ability (§6) |
| Rules Engine | **Scenario** — a composition layer above capabilities, not an ability itself. See §8 |
| AI | Too vague to classify yet — "AI" isn't an ability, "detect a person in a capture" would be. Needs to be scoped to a real verb before it's a capability |

## 5. Health vs. Metrics — deliberately separate subsystems

Not one concern, two, by design ([ADR-005](decision-log.md), [ADR-020](decision-log.md)):

- **Health/liveness**: is the agent/device alive and reachable *right now*? `AgentHeartbeat` (periodic, unconditional) + `DeviceHeartbeat` (event-driven, on-change), reconciled cloud-side into Online/Offline/Warning/Unknown.
- **Metrics**: operational telemetry values — CPU/memory/bandwidth (agent), power/voltage/battery (device). Separate table, separate worker, so a metrics-sampling bug can never make something look offline.

## 6. Schedule design

Two orthogonal primitives cover every case worked through (every second, every minute, every Sunday, every 1st of month, every day at 5pm, every day between 5–10pm):

- **Cadence** — how often, when active: a plain `TimeSpan` interval (what already exists — `SnapshotInterval`, `LivenessInterval`) for sub-minute/second-level ticks, or a cron-style expression for calendar recurrence (`0 0 17 * * *` = every day at 5pm, `0 0 0 1 * *` = 1st of month, `0 0 0 * * SUN` = every Sunday). A well-tested library (Cronos/NCrontab) should own "what's the next occurrence," including DST — not worth hand-rolling.
- **Window** (optional) — a start/end time-of-day, optionally day-filtered, that gates a cadence. "Every day between 5–10pm" is a window, not a single cron fire; it can wrap any cadence, including a plain interval ("capture every 10s, but only 5–10pm").

```
Schedule:
  Interval: 00:00:10          # plain TimeSpan — unchanged from today
  Cron: "0 0 17 * * *"        # OR calendar recurrence — new
  Window: { Start: 17:00, End: 22:00, Days: [Sun-Sat] }   # optional gate on either
```

**Where it lives — no new architectural layer:**
- No standing "Scheduler capability" with its own `BackgroundService`. That's more machinery than needed and the kind of speculative abstraction already declined elsewhere (ADR-021 declined a rules engine for Trigger on the same grounds).
- A small shared parsing/evaluation utility (a `ScheduleExpression` type + an `IsDue(now, lastFired)` check) is config parsing plus a pure function — not a runtime concept, doesn't need a Capability Host to exist.
- Each capability's own worker keeps owning the tick. It already loops and checks "has `SnapshotInterval` elapsed?" — swapping that for `schedule.IsDue(now)` is a small, local change.

**Convergence with Trigger:** a device's own Schedule becomes a second producer of the same `DeviceTriggeredEvent` the Trigger pipeline already consumes — `MotionTriggerResolverHandler` (event-sourced) and a per-device schedule check (clock-sourced) both feed the same downstream handler (`CaptureOnTriggerHandler`), which doesn't need to know which source fired it. A `Source` field (mirroring the existing `DeviceHeartbeatSource` `Native`/`HomeAssistant` pattern) can record *why* something fired — `DeviceTrigger` vs. `Schedule` — for visibility.

**One Schedule per capability, not a list.** Settled: `Schedule` is a single field on a capability's own options (e.g. `CameraOptions.CaptureSchedule`), not a list of (cadence, activity) pairs on one device. When a device needs a second, structurally distinct scheduled activity, it gets a **second capability** with its own `Schedule` — not a second entry on an existing one.

**The dividing line**, reapplying §3's capability test one level down: *does this activity produce its own event type, get consumed by its own handler, and mean something structurally different from the capability's main job?*
- **Genuinely different work → new capability, own schedule.** "Capture a snapshot" vs. "report battery level" are different in event type, consumer, and failure mode — exactly why `BatteryReportInterval` already exists as a separate field from `SnapshotInterval` today, effectively DeviceHealth's own cadence, distinct from Camera's. This precedent already validates the pattern.
- **Same job, different destination → NOT a new capability.** "Capture to disk" and "capture to cloud" are still one activity (capture) with two side effects — one `Schedule`, one trigger, fanning out — not two capabilities each polling independently.

## 7. Agent vs. Device — the IMonitorable shape

Structurally, `AgentHeartbeat`/`AgentMetrics` pass the exact same capability test as `DeviceHealth`/device metrics: a distinct ability (assess liveness / report telemetry) + its own machinery. The only difference is *what* they're pointed at — a device the agent is connected to, vs. the agent process itself. So Agent and Device are both instances of one underlying shape: **identity + a Health ability + a Metrics ability.**

That doesn't make Agent "a Device," for two concrete reasons:

- **Ownership only flows one way.** Every Device row carries an `AgentId` (`DeviceHeartbeatEntity`/`DeviceEventEntity` both extend the base `AgentEntity` for exactly this reason) — a device is always scoped *under* an agent. Agent can't also be a device without being both container and contained.
- **Agent health and device health aren't symmetric — one gates the other.** `IDeviceStatusResolver` uses `AgentHeartbeat` recency to *interpret* a stale `DeviceHeartbeat` ([ADR-005](decision-log.md)) — a quiet device reads differently depending on whether its agent is even online. Flattening them into one `Device` concept would erase that relationship.

**The shared shape doesn't need a name yet** (no `IMonitorable` interface, no polymorphic treatment) — same "no second consumer" rule as everywhere else in this doc. The real trigger would be a dashboard feature that needs agents and devices treated uniformly, e.g. a single unified health & metrics view. Sketched out (not built) to make the trigger concrete:

- Agent rows and device rows share one row shape — name, health badge, last-seen, metrics — but devices stay nested under their owning agent rather than flattened into one peer list, preserving the ownership direction above.
- The metrics column is genuinely type-specific: CPU/memory for an agent row, power/battery for a device row, `—` where nothing applies — not forced into one fixed schema.
- When a parent agent is offline, its child devices should read **unknown**, not a confident online/offline, even if the device's own last report is stale — the ADR-005 asymmetry made visible in the UI, not just enforced in `IDeviceStatusResolver`.

Until a view like this is a real, scoped feature, Agent and Device stay separate types with separate list pages — this section is vocabulary for when that changes, not a plan to build it now.

**Status: interface built, aggregation still parked.** `IMonitorable` (`Id`, `Status`, `StatusSinceUtc`, `LastHeartbeatUtc`) now exists in `Vivnest.Cloud/Api/Dtos/IMonitorable.cs` and is implemented by both `AgentSummaryDto` and `DeviceSummaryDto` — cheap and additive, no changes to entities, tables, or existing endpoints (`/agents`, `/devices` return the exact same shape as before, just now also satisfying the interface). Metrics is deliberately not part of it, for the reason above.

What's still parked — the same trigger as before: a new aggregating query service, a new endpoint merging Agent+Device reads (preserving the ownership nesting and the ADR-005 "child reads unknown if parent's offline" rule), and the dashboard UI to consume it. `IMonitorable` existing doesn't change that calculus — nothing queries across both types yet, so there's still no second consumer for the aggregation itself.

## 8. Scenarios — composing capabilities across devices

A capability stays single-device by definition (§3) — but a user-facing outcome often isn't. "Front Door Monitoring" needs a camera *and* a motion sensor working together; "Kitchen Sink scenario" might need a leak sensor and a smart valve. That composition is a real, separate concept, named **Scenario** (not "Capability" — reusing that word for a multi-device concept would collide with the single-device meaning §3 already establishes).

```
Scenario ("Front Door Monitoring")
  ├─ references: (Front Camera, Camera capability)
  ├─ references: (Driveway Sensor, MotionSensor capability)
  └─ activated by: Trigger — motion detected, window 22:00–06:00 (§6)
       └─ produces: Camera.Capture + Telegram Notification
```

Each capability underneath is still single-device, single-ability (§3 is unchanged) — Scenario only adds the named bundle that references several `(Device, Capability)` pairs and wires them to shared Triggers. It's the concrete shape the target architecture's "Rules Engine" slot was always pointing at, just named and scoped now instead of left vague.

**Why participants are `(Device, Capability)` pairs, not raw devices:** a device can have more than one capability (§3's worked example — a camera that both captures and detects motion), and a Scenario needs to reference *which* ability, not just which device. This resolves the self-referential case for free, with no special-casing:

```
Scenario ("Front Door Monitoring") — one physical camera, two capabilities
  ├─ (Front Camera, MotionSensor)  — role: source, trigger: motion detected
  └─ (Front Camera, Camera)        — role: target, action: capture
```

Same `DeviceId` appears twice — once per capability it's contributing, each with its own role. The camera triggers itself. Nothing about the model changes to support this; it falls out because a participant was never "a device," it was always "a device's ability."

**Where a capability can run — the full spectrum.** A participant's capability doesn't have to run on the same Agent as the rest of the scenario (e.g. an AI capability processing a capture from a different camera's agent). Five topologies, ordered by cost:

| # | Topology | Is it a Capability (§3)? | Transport | Identity/discovery | Status |
|---|---|---|---|---|---|
| 1 | Same Agent, in-process | Yes — an ordinary capability | None — in-process `EventDispatcher` | None | Works today, zero gaps |
| 2 | Cloud-hosted | No — a Cloud handler/service, same category as `ICameraCapturedHandler`/`HealthMonitorService` | Already exists — the upload queue every capture already goes through | Already exists — tenant-scoped by design | Works today, zero new architecture |
| 3 | Same-host sidecar (not a full Agent) | No — a local endpoint, neither a Capability nor an Agent | New but cheap — HTTP/gRPC over the Docker network, addressed by container DNS | None needed — fixed local address | Cheap addition, reuses #2's upload/event shape retargeted locally |
| 4 | Same-host full peer Agent (own `AgentId`, own heartbeat) | Yes — a capability on a second Agent | Cheap — Docker Compose's internal DNS solves addressing | Gap — cross-agent event identity (whose `AgentId`?) and no participant field for "which agent hosts this" | Real gap, cheaper transport than #5 |
| 5 | Cross-host peer Agent (true site mesh) | Yes — a capability on a second, physically separate Agent | Gap — no discovery, auth, or wire protocol across a site's network today | Same gap as #4, plus real discovery | Real gap — JOURNEY.md Stage 5b, "biggest single lift, furthest from today's code" |

(#2 also covers a Cloud service shared across several tenants, not just one — same topology either way.)

This exposes a gap in the participant shape itself: `(Device, Capability)` (or `(—, Capability)` for non-device-bound ones like Telegram) silently assumed "runs on the same Agent as everything else in the scenario." That's only true for #1. The honest shape is closer to `(Device?, Capability, Host)`, where `Host` defaults to "my agent" (covers #1 and Telegram) but can be "cloud" (#2/#3) or a specific other `AgentId` (#4/#5). Not built — recorded here so a future design doesn't have to re-derive the taxonomy, only pick a `Host` value once a real case needs one of #2–#5.

**Not built. Same rule as everywhere else in this doc** ([ADR-021](decision-log.md) already declined a general rules engine for the one real case that exists today — one motion sensor triggering one camera, handled fine by the existing narrow `TriggersDeviceIds` link). Scenario becomes worth building when:
- A single outcome genuinely needs to reference *multiple* devices at once (today's `TriggersDeviceIds` already covers "one device triggers one other device" — that's not this yet), or
- The dashboard needs a UI concept a user names and manages directly ("create a scenario"), not just per-device config a human edits.

Until then, this section is vocabulary — the reserved shape for what "Rules Engine" becomes once it's real, so a future design doesn't reinvent the name or the boundary with Capability.

## 9. Entity promotion rule

Applied consistently across every ADR reviewed: **don't promote a concept to a first-class persisted entity (with registry/CRUD) until a second real consumer needs to look it up independently of the stream that already carries it.** ADR-018's Site reasoning is the reusable template — "one agent per site today, a Site page would just be the unfiltered Agents list" — apply the same test to any new candidate (Schedule-as-shared-object, Capability Host, Brand catalog) before building it.

| Concept | First-class entity now? | Reasoning |
|---|---|---|
| Tenant | No | No consumer needs a Tenant row independent of the API-key-resolved context. |
| Site | No (confirmed) | ADR-018's call still holds; revisit at multi-agent-per-site (JOURNEY.md Stage 5b). |
| Agent | No | No feature needs an Agent to exist before its first heartbeat yet. Revisit if "add an agent" (pre-provisioning before the box is plugged in) becomes a dashboard action. |
| Device | No | Revisit if a remote "Add Device" write path is built (ADR-025 tried and walked back a remote-config-write path once already — reasoning is in git history if revisited). |
| Capability (registry) | No | `ICapability` stays a lifecycle marker; capabilities are wired directly, not through a polymorphic host, until real strain shows up (JOURNEY.md Stage 4 — "built when it strains, not scheduled"). |
| Schedule (as shared/reusable object) | No, stays per-device/per-capability config | Revisit only if a schedule needs to be shared across many devices (e.g. a tenant-wide "business hours" window) rather than tuned per device — that would be the second-consumer trigger. |
| Trigger | No | Stays `TriggersDeviceIds` config; ADR-021 already declined a rules engine for one motion sensor + one camera. |
| Brand | No catalog | Free text has no behavioral consumer yet. |
| DeviceType | Stays enum | No tenant-editable metadata need yet. |
| IMonitorable interface | Done — cheap, additive, no second-consumer test needed since it changed nothing observable | `Vivnest.Cloud/Api/Dtos/IMonitorable.cs`, implemented by `AgentSummaryDto`/`DeviceSummaryDto`. |
| IMonitorable aggregation (query service, endpoint, UI) | No | Revisit if a real feature needs agents and devices queried/displayed together (e.g. the unified health & metrics view) — §7. |
| Scenario | No | Named, not designed. Revisit when a real outcome needs multiple devices wired together, or the dashboard needs a user-managed "create a scenario" UI — §8. |
| Scenario participant `Host` field (cloud / sidecar / other agent) | No | Cheapest three topologies (#1–#3) need no schema change at all; revisit only when a real case picks topology #4 or #5 — §8. |

## 10. Open questions

- **"Setup"** — still unresolved: "an agent's device config" vs. a dashboard onboarding wizard. Revisit when it comes up concretely.
- **Schedule-due semantics per capability** — for a window like "armed 5–10pm," does that mean "run the cadence only inside the window" or "flip a boolean the capability's own logic reacts to" (e.g. suppress notifications outside hours vs. suppress capture entirely)? Per-capability decision, not something the schedule utility itself needs to resolve.
- **Optional/"sub" abilities** — no real case yet (PTZ, energy-monitoring, etc.) to validate the optional-interface-vs-sibling-capability split against. Revisit when a device with variable abilities within one type actually shows up.
- **Scenario internals** — how a multi-device Trigger condition is expressed (AND/OR across devices, ordering, failure handling if one referenced device is offline) is undesigned. Not worth resolving until a real Scenario use case forces the question.
- **Cross-agent event identity** — for Scenario topologies #4/#5 (§8), whether a jointly-produced event (e.g. `PersonDetected`, produced by Agent B about a capture from Agent A) carries the originating agent's `AgentId`, the processing agent's, or both is undecided. Only matters once topology #4 or #5 is real.
