# Vivnest Runtime — Phase 1: Runtime Foundation (Detailed Sprint Plan)

**Status:** Target-state detail for
[vivnest-runtime-overview.md](vivnest-runtime-overview.md)'s Milestone 1.
Not started. See [../roadmap/EVOLUTION-PLAN.md](../roadmap/EVOLUTION-PLAN.md)
for whether and when to begin this build.

## Objective

Build a reusable, production-quality edge runtime (the **Vivnest Runtime**)
that hosts capabilities without containing any business logic. The runtime
provides hosting, dependency injection, lifecycle management, messaging,
scheduling, diagnostics, telemetry, and extensibility. Vivnest is
implemented by adding capabilities on top of this runtime.

---

## Sprint 1 — Solution & Architecture Foundation

**Goal:** Establish the solution structure and architectural boundaries.

New projects, added to the existing `Vivnest.slnx` alongside
`Vivnest.Agent`, `Vivnest.Core`, `Vivnest.Infrastructure`, `Vivnest.Cloud`,
and `Vivnest.Cloud.Functions` — not a separate solution:

```text
Vivnest.slnx

├── Vivnest.Abstractions
├── Vivnest.Runtime
├── Vivnest.Hosting
├── Vivnest.Diagnostics
├── Vivnest.Extensions
├── Vivnest.Agent            (existing)
├── Vivnest.Core             (existing)
├── Vivnest.Infrastructure   (existing)
├── Vivnest.Cloud            (existing)
└── Vivnest.Cloud.Functions  (existing)
```

Deliverables: solution structure, project references, coding standards,
folder structure, README, Architecture Decision Records (ADRs).

---

## Sprint 2 — Core Contracts (Vivnest.Abstractions)

**Goal:** Define all public interfaces and contracts.

Contracts: `ICommand` / `CommandBase`, `IEvent` / `EventBase`, `ICapability`,
`IKernel`, `IRuntimeContext`, `IRuntimeState`, `ICommandDispatcher`,
`IEventDispatcher`, `ICommandHandler<T>`, `IEventHandler<T>`, `IWorkQueue`,
`IWorkItem`, `IScheduler`, `IScheduledJob`, `IHealthCheck`,
`IMetricsCollector`, `ITelemetryPublisher`.

Deliverables: stable runtime contracts, no implementations.

---

## Sprint 3 — Hosting & Kernel

**Goal:** Implement the runtime lifecycle.

Components: Kernel, RuntimeHost, RuntimeContext, RuntimeState, LifecycleManager.

Lifecycle:

```text
Created → Starting → Initializing → Running → Stopping → Stopped
```

Deliverables: runtime startup, runtime shutdown, lifecycle management.

---

## Sprint 4 — Dependency Injection & Bootstrap

**Goal:** Standardize dependency registration.

```csharp
builder.Services
    .AddVivnestRuntime()
    .Build();
```

Deliverables: single runtime registration entry point, runtime service registration.

---

## Sprint 5 — Capability Host

**Goal:** Implement the plugin framework.

Components: CapabilityHost, CapabilityLoader, CapabilityRegistry, CapabilityManager.

Lifecycle:

```text
Discover → Register → Initialize → Start → Running → Stop → Dispose
```

Deliverables: load and execute a dummy capability.

---

## Sprint 6 — Messaging Infrastructure

**Goal:** Implement commands and events.

Command flow:

```text
Command → Dispatcher → Single Handler → Result
```

Event flow:

```text
Event → Dispatcher → 0..N Handlers
```

Deliverables: command dispatcher, event dispatcher, handler registration.

---

## Sprint 7 — Internal Work Queue

**Goal:** Provide asynchronous background execution.

Components: `Channel<T>`, WorkQueue, BackgroundProcessor, WorkerPool.

The queue must support priority, not just FIFO — at minimum an `Urgent` /
`Normal` split, so an instant alert can't get stuck behind routine
compression or retry work on constrained edge hardware. A
`PriorityChannel<T>`-style wrapper (multiple underlying channels drained in
priority order) is the likely shape; a single-channel FIFO `WorkQueue`
would need to be revisited if built first.

Design this alongside Sprint 6's Command Dispatcher, not after it: the
immediate path (`Command Dispatcher → Handler`) and the queued path
(`Work Queue → Worker → Handler`) must implement the *same* handler
interface, so a command's caller doesn't know or need to know which path
it took. Not every command should be queued — only long-running or
deferrable work (AI inference, video processing, retries, uploads).

Deliverables: priority-aware queue abstraction, background workers,
non-blocking execution.

---

## Sprint 8 — Scheduler

**Goal:** Implement runtime scheduling.

Features: interval jobs, cron jobs, job registration, job execution.

Deliverables: generic scheduler independent of business logic.

---

## Sprint 9 — Runtime State

**Goal:** Track runtime health and status.

Runtime: version, uptime, started time, status.
Capabilities: loaded, running, failed, stopped.

Deliverables: runtime state service.

---

## Sprint 10 — Diagnostics & Telemetry

**Goal:** Provide operational visibility.

Logging: runtime started, capability loaded, command executed, event published.
Metrics: commands/sec, events/sec, queue length, memory, CPU, execution time.
Health: runtime, scheduler, queue, capabilities.

Deliverables: structured logging, metrics, health checks.

---

## Sprint 11 — Extension Model

**Goal:** Support runtime extensibility.

```csharp
builder.Services
    .AddVivnestRuntime()
    .AddCapability<CameraCapability>()
    .AddCapability<TelegramCapability>()
    .AddCapability<CloudSyncCapability>();
```

Future: external plugins, NuGet capabilities, dynamic loading, marketplace.

---

## Sprint 12 — Integration & Stabilisation

**Goal:** Freeze Runtime v1.

Integration test:

```text
Start Runtime → Load Dummy Capability → Send Command → Publish Event
  → Execute Queue → Shutdown
```

Deliverables: integration tests, documentation, performance baseline,
Runtime v1 API freeze.

---

## Phase 1 Deliverables

Hosting, Dependency Injection, Runtime Lifecycle, Runtime Context, Runtime
State, Capability Host, Command Dispatcher, Event Dispatcher, Work Queue,
Scheduler, Diagnostics, Telemetry, Extension Model, Integration Tests,
Documentation.
