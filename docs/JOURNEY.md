# The Vivnest Journey

This doc exists for one reason: it's easy to lose sight of how big the
destination is while heads-down on the next bug fix or sprint. Read this
when you need the whole shape of the path, not the next step on it — for
that, go to [roadmap/EVOLUTION-PLAN.md](roadmap/EVOLUTION-PLAN.md) instead.

## Where this started

A single-purpose home camera agent: one process, one device type, one
household. Working, but with real correctness/security bugs (fixed this
session) and no product features beyond "capture and upload."

## Where this is actually headed

A commercial IoT device management platform — any device or sensor, across
multiple verticals (home, commercial CCTV, agriculture, industrial), for
many independent customer sites, each site potentially running a resilient
mesh of specialized agents rather than one box. That is a categorically
bigger thing than "a camera app," and it's worth saying so plainly rather
than letting it stay implicit.

## The shape of the path

```text
Stage 0   Foundation stabilized             ← done
Stage 1   First real product (cameras)      ← done, deployed
Stage 2   Second device type                ← done, proved the abstraction
Stage 3   Integrations + AI                     protocol reach, detection
Stage 4   Formal runtime kernel                 built when strain demands it
Stage 5a  Multi-tenant cloud                    many customer sites
Stage 5b  Intra-site agent mesh                 many agents, one site
```

**Stage 0 — Foundation stabilized.** Fixed the correctness, security, and
reliability bugs in the existing single-agent architecture. Consolidated
scattered planning documents into one coherent set. Clarified that the
target is multi-device, multi-vertical, not camera-only. This stage is
about the ground being solid enough to build on, not about new features.

**Stage 1 — First real product, still camera-only.** Phase 3 of
[roadmap.md](roadmap/roadmap.md): device health monitoring, notifications
across channels, a read-only REST API, a dashboard. Shipped on the
existing architecture, no rewrite required, and actually deployed —
`Vivnest.Cloud.Functions` to a real Azure Function App,
`Vivnest.Dashboard` to Azure Static Web Apps (see EVOLUTION-PLAN.md step
7). This alone is a usable, sellable home / commercial-CCTV monitoring
product — the journey doesn't require Stage 5 to produce something
valuable.

**Stage 2 — The second device type. Done — a TP-Link Kasa smart plug.**
[ADR-007](architecture/decision-log.md#adr-007--camera-is-the-first-device-capability-not-the-only-one)'s
open question got tested for real, per
[ADR-015](architecture/decision-log.md#adr-015--the-second-device-type-smartplug-does-not-reuse-icamera-confirming-adr-007s-prediction):
`ICamera`/`CameraCaptureService` did **not** generalize onto the new
device — the second device type needed its own `ISmartPlug` shape
entirely, while the heartbeat/offline-detection/persistence layers
absorbed it with zero changes, exactly as predicted. The single most
important proof point for whether "any device" is achievable with this
architecture has now actually been tested, not just designed for.

**Stage 3 — Integrations and intelligence.** Phase 4 (MQTT, ONVIF, Modbus,
BACnet, Zigbee — the protocols the agriculture/industrial verticals
actually use) and Phase 5 (AI detection, riding on the existing
`DeviceEvent` pipeline per the integration decision already documented).
Reach and intelligence, not new structural risk.

**Stage 4 — The formal Vivnest Runtime kernel.** The 12-sprint build in
[phase-1-runtime-foundation.md](architecture/phase-1-runtime-foundation.md):
command/event dispatch, a priority-aware work queue, a real capability
host. Built when the informal `IEventHandler<T>` pattern is genuinely
straining under the number of capabilities in play — not scheduled by
calendar, scheduled by pain.

**Stage 5a — Multi-tenant cloud.** Many independent customer sites, managed
centrally, each properly isolated
([ADR-008](architecture/decision-log.md#adr-008--multi-tenancy-is-a-day-one-constraint-not-a-later-migration)).
The data model (`TenantId`/`SiteId`) already anticipates this; the
remaining work is enforcing it once the API exists.

**Stage 5b — The intra-site agent mesh.** The biggest single lift in this
entire journey: containerized Camera / Storage / AI / Heartbeat agents,
potentially one per Raspberry Pi, discovering each other, sharing load, and
failing over to one another within a site. Requires network-transparent
command/event dispatch that doesn't exist even in the Stage 4 kernel design
yet. This is the part of the vision that's furthest from today's code —
worth being honest that it's a distinct, later, harder undertaking, not a
natural extension of Stage 4.

## Reading this journey honestly

- **Every stage before 5b ships something real on its own.** This isn't
  "nothing works until the whole vision is built" — Stage 1 alone is a
  product. Treat each stage as independently valuable, not as a checkpoint
  on the way to the "real" goal.
- **Stages aren't strictly sequential.** Stage 3 (integrations/AI) doesn't
  block on Stage 2 (second device type) in every case — some integrations
  are camera-specific (ONVIF). Use judgment; this map shows shape, not a
  rigid gate order.
- **5a and 5b are genuinely different problems that happen to share a
  phase number.** See EVOLUTION-PLAN.md's "three distinct distribution
  axes" for why they shouldn't be designed as one thing.
- **This doc changes rarely.** Unlike EVOLUTION-PLAN.md (updated as work
  lands) or roadmap.md (updated as phases get detail), this is meant to
  stay a stable north star. If it needs to change, that's a real strategy
  shift worth noticing explicitly, not a routine edit.

## Where to go next

- **What do I work on right now?** → [roadmap/EVOLUTION-PLAN.md](roadmap/EVOLUTION-PLAN.md)
- **What does each phase actually deliver?** → [roadmap/roadmap.md](roadmap/roadmap.md)
- **What does the target architecture look like?** → [architecture/vivnest-runtime-overview.md](architecture/vivnest-runtime-overview.md)
- **What's actually built today?** → [architecture/current-architecture.md](architecture/current-architecture.md)
- **What rules must not be broken along the way?** → [architecture/decision-log.md](architecture/decision-log.md)
