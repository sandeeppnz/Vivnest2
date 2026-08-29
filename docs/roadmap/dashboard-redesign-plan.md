# Dashboard redesign plan

*Written 2026-08-29, from the full-file review of `Vivnest.Dashboard`
(2026-08-25) and the 18-screen Home/Installer/Developer mockup shared
2026-08-07. Companion to EVOLUTION-PLAN.md's gradual-evolution rule:
each phase ships value on its own, and nothing here needs a backend
change until explicitly noted.*

**Status 2026-08-29: all six phases are BUILT** - shipped as commits
26876e7 (D0), 81b47c5 (D1), e112a8a/9907636 (D2), 1cc4d1b (D3),
648a4c9 (D4), 0fac91b (D5), each verified live against the local
stack. The text below is kept as the design rationale; the
out-of-scope list at the bottom is still the deferred list.

Five phases, deliberately ordered: D1 and D2 are invisible foundations
that shrink the codebase, D3 is the visible redesign, D4 and D5 are the
growth path toward the mockup. Each phase is independently shippable
via `deploy.ps1` and none blocks the others' rollback.

What this plan deliberately KEEPS: the status vocabulary (dots, badges,
the shared severity palette), ErrorBanner + troubleshooting tips, the
capture gallery's timezone handling, confirm-before-destructive, and
the hand-rolled CSS. No component-library rewrite — that would be
churn, not improvement.

---

## D0 — Test scaffolding (enabler, small)

Add vitest + a first test file covering the pure logic that already
bit us once: `formatInterval` (the TimeSpan day case), `formatDateTime`
clock skew, `requestVoid`'s status mapping, `eventDescriptions`. The
.NET side's lesson applies here: tests seeded from real defects, not
coverage. This exists so D1/D2's mechanical rewrites have a net.

New dev deps: `vitest` only. No jsdom/component testing yet — pure
functions first, the same "extract when a second consumer needs it"
restraint.

## D1 — URLs (routing)

**Why first**: refresh loses your place, back does nothing, nothing is
linkable. Also the prerequisite for D3's pages and D4's modes (modes
are route prefixes).

- Router: `wouter` (~2 KB, hooks-only). The repo's dependency diet
  (react + react-dom and nothing else) is deliberate; wouter respects
  it. react-router would also work but brings far more than needed.
- Routes: `/` (overview), `/devices`, `/devices/:id`,
  `/devices/:id/:tab`, `/agents`, `/agents/:id/:tab?`, `/events`,
  `/admin`, `/admin/:screen`. Status filters move to query strings
  (`/devices?status=Error`) so Overview's drill-down links are real
  links.
- `App.tsx`'s ternary chain becomes a route table; the `View`/
  `AdminView` state types disappear. `devicesOnly` becomes a route
  guard, not a render branch.
- **Deployment prerequisite**: Azure Static Web Apps returns 404 for
  deep links without a `staticwebapp.config.json` carrying
  `navigationFallback` to `index.html`. That file does not exist today
  and must ship in the same change as the router, copied into `dist`
  by the build.

## D2 — Data layer (query cache + background refresh)

**Why**: every screen refetches the same lists on mount and then the
data freezes — a monitoring dashboard that is stale while you look at
it. Also the single biggest code-deletion opportunity.

- `@tanstack/react-query` v5. Query keys: `['devices']`, `['agents']`,
  `['events']`, `['device', id]`, `['commands', agentId]`, one per
  admin list.
- Monitoring data (`devices`/`agents`/`events`) gets
  `refetchInterval: 30_000` + refetch-on-focus; admin lists refetch on
  focus only. No sockets — 30s polling is the right cost at this
  scale, and the interval lives in one place when that changes.
- The `apiKey`/`onAuthError` prop-drilling (threaded through every one
  of the 48 components today) collapses into a session context + a
  global QueryCache error handler: any 401 anywhere resets the session
  once, centrally. Operator-tier screens keep their separate key via
  their own context.
- Hooks live in a new `src/queries.ts` (`useDevices()`,
  `useDevice(id)`, ...); `api.ts` stays as the pure fetch layer it is.
- Actions (restart, deploy, assign, publish, revoke, ...) become
  mutations that invalidate the affected keys — replacing every manual
  `load()` re-fetch.
- **What this deletes**: the fourteen hand-rolled
  `useEffect`+`cancelled` blocks, the `reloadNonce` retry machinery,
  `DeviceCapabilitiesModal`'s `currentLoadRef`, and the per-screen
  `saveError` wiring's plumbing. `ErrorState` survives with
  `onRetry={refetch}`. Conversion is screen-by-screen and mechanical;
  no UX change in this phase.

## D3 — Task-first admin (the visible redesign)

**Why**: the seven admin screens mirror the storage tables, so "add a
camera" spans five screens and three modals. The projected-config /
publish / sync-status flow — architecturally the backend's best work
(ADR-063/064/068/070) — is hidden behind a link icon in a modal.

- **Add-device wizard** at `/admin/devices/new` — a page, not a modal
  (D1 makes that possible). Steps, all against existing endpoints:
  1. Register: name, type (create type inline if missing), owning
     agent, location — `POST /devices-registry-admin`.
  2. Capabilities: compatible ones (via `device-type-capabilities`)
     with executing-agent pick (via `agent-capabilities-admin`) and
     schema-driven settings — the `ConfigFields` renderer already
     exists.
  3. Review: `GET .../projected-config`, warnings shown inline;
     Publish enabled only when clean — same gate the modal has.
  4. Done: link to the new device page + "Refresh agent
     configuration" action.
  No new backend routes needed; the wizard sequences what exists.
- **Configuration page** per device (`/devices/:id/configuration`) and
  agent: sync status (Desired vs Published vs Applied), version
  numbers, publish, rollback, and the projected JSON — everything now
  in `ProjectedConfigModal`/`AgentProjectedConfigModal`, promoted to a
  first-class tab. The modals are then deleted.
- The CRUD screens stay as-is under `/admin` as the "registry browser"
  — demoted, not rewritten.

## D4 — Home mode, grown from the devicesOnly seam

The mockup's Installer Mode ≈ today's app. Its Home Mode is grown from
the `devicesOnly` key path, which already hides agents and trims
navigation: rename that rendering path's vocabulary toward the mockup
(Home / Devices / History / Settings tabs; History = the events feed).
Explicitly deferred, same as the 2026-08-07 note: the mode selector,
the PIN gate, the Alerts screen, and Home Mode as a choice for
full-access keys. This phase only aligns what is genuinely cheap.

## D5 — The bell

Flavor 1 from the parked notification-center note (the cheap one):
header bell, unread count = Warning/Critical events newer than a
`localStorage` last-seen timestamp, panel reuses the Events feed
filtered to those severities. D2's `['events']` query with its 30s
refresh makes the count live for free. Real push stays parked — it is
a second delivery channel beside Telegram, not a dashboard feature.

---

## What is deliberately out of scope

- Real-time transport (SignalR/WebSockets) — polling via D2 is right
  for this scale.
- A component library / visual rewrite.
- Per-user accounts, server-side read state, audit identity. The bell's
  read state is localStorage precisely because users don't exist; the
  moment they do (a backend decision), revisit D5 and `requestedBy`.
- ~~The mode selector and PIN gate (D4 note)~~ - built 2026-08-29 on
  request as a follow-up (D6): full-access keys pick Home or Installer
  at login, Home Mode's Settings carries the PIN-gated installer
  unlock, and the sidebar's "Switch mode" reopens the selector. The
  PIN is child-proofing on one browser, NOT a security boundary - the
  key has full API access regardless; the real boundary remains the
  devicesOnly key. Developer Mode and the Alerts screen stay deferred.

## Verification per phase

`tsc -b`, `oxlint`, `vite build`, plus a dev-server smoke pass with
the backend running (D1: deep-link + refresh + back; D2: watch a
heartbeat tick update without navigation; D3: full wizard run against
a real registration). D0's vitest runs from D1 onward. Deploy at each
phase boundary, not mid-phase.
