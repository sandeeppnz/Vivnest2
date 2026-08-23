# Vivnest

**Target:** an edge-first IoT device monitoring platform — any device or
sensor (cameras, water meters, heat pumps, soil sensors, etc.) across
multiple verticals (home, commercial CCTV, agriculture, industrial IoT).
**Today:** only camera monitoring is implemented. An edge agent captures
camera snapshots and heartbeats, an Azure-hosted cloud side persists and
processes them, and Telegram delivers notifications. See [README.md](README.md) for how to build, run and configure it.

## What each project is for

Restructured 2026-08-24 (ADR-106 to ADR-108). Two planes, one shared
layer between them:

| Project | Purpose |
|---|---|
| **Vivnest.Domain** | What the business *is*. Agent, Device, Capability, Site, Tenant, Machine, and the enums that belong to each. Foldered by area, references nothing at all. |
| **Vivnest.Core** | The contracts both planes share. `ICapability`, `IEventDispatcher`, `ICommandHandler`, `IBlobStorageClient`, options and queue-message shapes, and the few entities both sides read. References Domain only. Knows no implementations. |
| **Vivnest.Runtime** | The execution engine. `CapabilityHost`, `CapabilityRegistry`, `CapabilityContext`, `CapabilityWorkerSupervisor`, `EventDispatcher`, runtime state. Starts, stops and supervises capabilities and dispatches events in-process. **Does not know that Camera exists.** |
| **Vivnest.Capabilities** | What an Agent can actually do. Camera, MotionSensor, SmartPlug, Triggers, the Home Assistant / Tapo Hub bridges, and the AiClassification pipeline. Three of these implement `ICapability`; AiClassification does not yet - see ADR-111. |
| **Vivnest.Infrastructure** | External technology, nothing else. Azure blob/queue/table clients, Tapo and Kasa device protocols, RTSP capture, and the DI registration for them. Implements contracts defined in Core. |
| **Vivnest.Cloud** | The control plane: what exists and what *should* be. Registries, capability assignment, configuration projection and publishing, command dispatch, health rules, notifications, persistence. |
| **Vivnest.Cloud.Functions** | Hosting for Cloud - HTTP routes, queue triggers, timers. Deliberately thin; the logic lives in Vivnest.Cloud. |
| **Vivnest.Agent** | The executable host. Bootstrap, DI, configuration loading, and the platform shell (heartbeats, command polling, metrics, log shipping, error reporting). **It starts the runtime; it does not know how a camera works.** |
| **Vivnest.Agent.Updater** | A separate process on the host that pulls and redeploys the Agent container. Never inside that container - see ADR-028. |
| **Vivnest.Tests** | One test project for everything (191 tests). |

**The dependency rule.** `Domain <- Core <- everything`. Domain and Core
never reference Infrastructure, Runtime, Cloud or Agent, and nothing
references Vivnest.Agent - it is the host, and the arrows point at it.

```
              Vivnest.Domain
                    ^
              Vivnest.Core
             /      |       \
     Runtime   Infrastructure   Cloud
        ^            ^             ^
  Capabilities       |        Cloud.Functions
        \___________ | ___________/
                Vivnest.Agent
```

**Cloud and Runtime are two systems, not two layers.** Cloud is the
control plane - it decides. Runtime, Capabilities and the Agent are the
execution plane - they do. Core is shared by both, which is exactly why it
cannot be merged into Cloud: 93 of its 134 types are used by the Agent
side, and merging would put the whole control plane inside the Raspberry
Pi container.

Do **not** add `Vivnest.Cloud.Domain` or `Vivnest.Runtime.Domain`. The
same entities exist in both worlds; what differs is how they are used, not
what they mean.

Don't let "camera" in type/method names read as a hard architectural
boundary — it's the first of several planned device capabilities, not the
shape everything else must fit into. See
[docs/architecture/decision-log.md](docs/architecture/decision-log.md)
(ADR-007) before assuming camera-specific code generalizes for free.

## Start here

- **[docs/JOURNEY.md](docs/JOURNEY.md)** — the whole arc, from where this
  started to where it's actually headed, in one page. Read this first if
  you need the big picture; it changes rarely, unlike everything below it.
- **[docs/roadmap/EVOLUTION-PLAN.md](docs/roadmap/EVOLUTION-PLAN.md)** — the
  plan of record for what's next and why. Read this for the immediate next
  step; it explains how the feature roadmap and the target architecture
  reconcile, and what's actually true about the current code vs. what's
  aspirational.
- **[docs/architecture/current-architecture.md](docs/architecture/current-architecture.md)**
  — what's actually built today, verified against the code. Read this
  before assuming anything about how the system works.
- **[docs/architecture/decision-log.md](docs/architecture/decision-log.md)**
  — binding architectural rules (workers never persist directly, queue
  messages carry only PartitionKey/RowKey, cloud determines device health,
  etc.). Treat these as constraints, not style preferences.
- **[docs/architecture/vivnest-runtime-overview.md](docs/architecture/vivnest-runtime-overview.md)**
  — the target architecture (the "Vivnest Runtime"): capability-based,
  commands/events, runtime state separate from persistence. Not yet built —
  this is where the codebase is gradually heading, not what it looks like today.
- **[docs/roadmap/roadmap.md](docs/roadmap/roadmap.md)** — the full phase
  roadmap (Phase 1-6, runtime-first) with the near-term sprint plan
  (Phase 3, feature-first) nested inside it. EVOLUTION-PLAN.md is the
  reconciliation layer on top of this.

  **Two phase numbering schemes exist.** roadmap.md runs Phase 1-6. The
  decision log and current-architecture.md also refer to **Phase 7, 8 and
  9** (Phase 9 = Command & Control, closed 2026-08-23), which no roadmap
  defines, and EVOLUTION-PLAN.md now defines **Phase 10** (Distributed /
  Multi-Agent Execution, parked). The schemes are not aligned, and Phase
  10 overlaps roadmap.md's Phase 6 in substance while proposing a
  different architecture for it. Say which scheme you mean.

- **[docs/operations/](docs/operations/)** — running the thing:
  [configuration.md](docs/operations/configuration.md) (where every setting
  comes from, and the layering that has broken twice),
  [troubleshooting.md](docs/operations/troubleshooting.md) (real failures and
  their diagnoses — read the first paragraph even if nothing is broken), and
  [deployment.md](docs/operations/deployment.md). Written 2026-08-21.

## Guiding principle: gradual evolution

The codebase is evolving toward the Vivnest Runtime architecture, but not by
a big-bang rewrite. Every change should:

1. Ship real product value on the *current* architecture, and
2. Where genuinely cheap, be shaped toward the target vocabulary — e.g. the
   event dispatch mechanism is already named `IEventHandler`/
   `IEventDispatcher`, not `ICapabilityHandler`/`ICapabilityDispatcher`
   (renamed for exactly this reason).

Do not extract generalized abstractions (a formal command dispatcher, a
capability host, separate `Vivnest.Runtime`/`Vivnest.Abstractions`
projects, mesh/distributed features) speculatively. Extract them when a
second real consumer needs them — see the "rule of thumb" in
EVOLUTION-PLAN.md.

## Current state, briefly

- `Vivnest.Tests` (xunit, in the solution) covers three areas, still
  seeded from real defects rather than written for coverage: the shared
  primitives in `Vivnest.Core`; the configuration publish pipeline in
  `Vivnest.Cloud` (versioning, immutable version blobs, the no-op guard,
  ETag retry, rollback, and the tenant/site-scoped blob layout with its
  dual-write and backfill); and API auth in `Vivnest.Cloud.Functions`,
  driven through the real Function class. Storage is faked behind
  interfaces, so no Azure is needed. Still untested: the Agent host
  process, the queue/timer-triggered functions, and the dashboard — so
  treat a green `dotnet test` as "the tested paths did not regress", not
  "the system works". Several of the defects this suite exists because of
  were only findable by running against real storage.
- `IEventHandler<T>` + `EventDispatcher` in `Vivnest.Runtime/Events` is the current (informal) event dispatcher — a real capability-module concept (a Capability Host) doesn't exist yet. An empty `ICapability` stub used to sit in `Vivnest.Agent/Interfaces` with zero implementations and zero references; it was removed, so write that contract fresh against the capabilities that exist when a Host is actually built rather than resurrecting it.
- Queues mostly flow Agent → Cloud, plus two Cloud → Agent command queues (`agent-restart-commands`, `agent-deploy-commands` — see [decision-log.md](docs/architecture/decision-log.md) ADR-024, ADR-028). Deploy is consumed by `Vivnest.Agent.Updater`, a separate process on the host — never by `Vivnest.Agent` itself, which deliberately has no Docker access.
- `Vivnest.Cloud.Functions` now has several queue-triggered functions, two Timer-triggered functions (health monitoring, retention), and a full tenant-scoped HTTP REST API (`/devices`, `/agents`, `/apikeys`, `/whoami`) — see roadmap.md Phase 3 Sprint 4 and [current-architecture.md](docs/architecture/current-architecture.md)'s "REST API & Auth" section.
- Configuration blobs are named `{tenantId}/{siteId}/{runtimeId}` (ADR-091). The old unscoped names are still written on every publish and still read as a fallback, because that is what lets an Agent on an older build keep working — do not remove that second write until every deployed Agent reads the scoped layout. `RuntimeConfigurationWriter<TEntity>` owns the whole publish cycle for both the Agent and Device sides; the two publishers supply only what genuinely differs.
- Agent images are semver-tagged in ACR (`1.0.0` onward, ADR-073), which is what makes `AgentInstallation.ImageVersion` meaningful. `AgentAuth:RequireApiKey` is **off** in deployed config, so the Agent command callbacks accept unauthenticated calls in grace mode — the mechanism to close that is built and tested, it is the flag that is not flipped.

For anything more specific than this — open questions, what's fixed vs.
outstanding, the next concrete step — read EVOLUTION-PLAN.md rather than
relying on this file being current; it's the one meant to be updated as
work lands.

## Keeping docs honest

`current-architecture.md` and `decision-log.md` describe the code as it is
*right now* — they will drift the moment the code they describe changes.
There is no automated check for this, so it's a manual discipline:

- If a change touches something either doc describes (event dispatch,
  worker/handler responsibilities, runtime state fields, persistence
  rules), update the doc **in the same change**, not as a follow-up.
- Don't do a periodic "review the docs" pass for its own sake — that tends
  to get skipped. Tie doc updates to the code change that invalidates them.
- If you notice a doc is already stale, fix it on the spot rather than
  filing it away for later.
