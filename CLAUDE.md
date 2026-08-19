# Vivnest

**Target:** an edge-first IoT device monitoring platform — any device or
sensor (cameras, water meters, heat pumps, soil sensors, etc.) across
multiple verticals (home, commercial CCTV, agriculture, industrial IoT).
**Today:** only camera monitoring is implemented. An edge agent captures
camera snapshots and heartbeats, an Azure-hosted cloud side persists and
processes them, and Telegram delivers notifications. Six projects —
`Vivnest.Agent`, `Vivnest.Core`, `Vivnest.Infrastructure`, `Vivnest.Cloud`,
`Vivnest.Cloud.Functions`, `Vivnest.Agent.Updater` — see [README.md](README.md)
for what each does, how to build/run, and configuration.

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

- `Vivnest.Tests` (xunit, in the solution) covers the shared logic in
  `Vivnest.Core` only: the device-config runtime adapter, the event RowKey
  format, free-text name matching, and command-status terminality. It is a
  deliberately narrow start, seeded from real defects rather than written
  for coverage. Everything else — Cloud services, the Agent host, the
  Functions — still has no automated tests, so treat a green `dotnet test`
  as "the shared primitives did not regress", not "the system works".
- `IEventHandler<T>` + `EventDispatcher` in `Vivnest.Agent/Runtime/Dispatching` is the current (informal) event dispatcher — a real capability-module concept (a Capability Host) doesn't exist yet. An empty `ICapability` stub used to sit in `Vivnest.Agent/Interfaces` with zero implementations and zero references; it was removed, so write that contract fresh against the capabilities that exist when a Host is actually built rather than resurrecting it.
- Queues mostly flow Agent → Cloud, plus two Cloud → Agent command queues (`agent-restart-commands`, `agent-deploy-commands` — see [decision-log.md](docs/architecture/decision-log.md) ADR-024, ADR-028). Deploy is consumed by `Vivnest.Agent.Updater`, a separate process on the host — never by `Vivnest.Agent` itself, which deliberately has no Docker access.
- `Vivnest.Cloud.Functions` now has several queue-triggered functions, two Timer-triggered functions (health monitoring, retention), and a full tenant-scoped HTTP REST API (`/devices`, `/agents`, `/apikeys`, `/whoami`) — see roadmap.md Phase 3 Sprint 4 and [current-architecture.md](docs/architecture/current-architecture.md)'s "REST API & Auth" section.

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
