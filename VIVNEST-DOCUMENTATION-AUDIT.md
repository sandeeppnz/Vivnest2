# Vivnest — Documentation Audit

**Date:** 2026-08-21
**Actioned:** same day — F2, F3 and F4 were fixed; F1 was accepted as a known
infrastructure difference. Per-file entries below are as-audited; the
resolutions are recorded against each finding.
**Scope:** every `.md` file in the repository (17), excluding `node_modules`,
`bin`, `obj` and `.git`.
**The audit itself deleted and modified nothing** — it was a read-only pass,
and this file was its only output. The fixes recorded against F2/F3/F4 below
were made afterwards, as a separate act on the user's instruction, and are
listed in the commit that carries them. Nothing was deleted at any point.

Claims were checked against the code and, where a document asserts something
about deployed infrastructure, against the live Azure environment. Where a
document conflicts with reality, the evidence is stated rather than asserted.

---

## Summary

| # | Path | Class | Current or historical | Conflicts? | Recommendation |
|---|---|---|---|---|---|
| 1 | `CLAUDE.md` | AUTHORITATIVE | Current | No | KEEP |
| 2 | `README.md` | AUTHORITATIVE | Current | No | KEEP |
| 3 | `docs/architecture/current-architecture.md` | AUTHORITATIVE | Current | No | KEEP |
| 4 | `docs/architecture/decision-log.md` | AUTHORITATIVE | Current + historical by design | Two stale env references | KEEP |
| 5 | `docs/roadmap/EVOLUTION-PLAN.md` | AUTHORITATIVE | Current | **Yes — stale deploy target** | KEEP + correct |
| 6 | `docs/roadmap/roadmap.md` | AUTHORITATIVE | Current | No | KEEP |
| 7 | `docs/JOURNEY.md` | AUTHORITATIVE | Current (narrative) | No | KEEP |
| 8 | `docs/architecture/vivnest-runtime-overview.md` | AUTHORITATIVE (target state) | Neither — aspirational | No | KEEP |
| 9 | `docs/architecture/phase-1-runtime-foundation.md` | AUTHORITATIVE (target state) | Neither — aspirational | No | KEEP |
| 10 | `docs/architecture/dashboard-domain-model.md` | **OUTDATED** | Historical | **Yes — materially** | INVESTIGATE |
| 11 | `devops/blob-lifecycle/README.md` | **OUTDATED** | Historical | **Yes — wrong environment** | INVESTIGATE |
| 12 | `devops/device-event-retention/README.md` | AUTHORITATIVE | Current | No | KEEP |
| 13 | `Vivnest.Agent/Models/README.md` | AUTHORITATIVE | Current | No | KEEP |
| 14 | `scripts/ml/sink-cleanliness/README.md` | AUTHORITATIVE | Current | No | KEEP |
| 15 | `tools/object-detection-tester/README.md` | AUTHORITATIVE | Current | No | KEEP |
| 16 | `tools/sink-cleanliness-tester/README.md` | AUTHORITATIVE | Current | No | KEEP |
| 17 | `Vivnest.Dashboard/README.md` | **GENERATED** | Neither — vendor boilerplate | No | DELETE CANDIDATE |

**No TEMPORARY files found.** No Claude-generated planning notes, TODO
documents, task specifications or migration documents survive in the tree.
The one audit artefact that did exist, `VIVNEST-DEAD-LEGACY-CODE.md`, was
retired on 2026-08-20 (commit `5dfa41c`) with its content redistributed —
that is why this audit finds a clean tree rather than a pile of notes.

**No true DUPLICATE pairs.** Two candidate overlaps were checked and both
turned out to be deliberate layering, not duplication — see §Findings.

---

## Findings that need a decision

### F1 — Capture images are not being aged out in this environment

`devops/blob-lifecycle/README.md` says the retention policy was *"Applied to
the current production storage account"* and shows the command run against
**`stvivnestagentdev`** in **`rg-vivnest-dev`**.

That is the **V1** environment. Vivnest2 runs in `rg-vivnest-2` against
`stvivnestagent2`. Verified against Azure today:

```
az storage account management-policy show \
  --account-name stvivnestagent2 --resource-group rg-vivnest-2
  → no policy
```

So this is not merely a stale document. **No lifecycle policy exists on the
storage account this system actually writes captures to**, and the `photos`
container has been growing unbounded since the environment moved. The
document describes a control that is not in force.

The `blob-lifecycle-policy.json` alongside it is still correct — it scopes to
the `photos/` prefix, which matches `StorageOptions.BlobContainer`. Only the
account it was applied to has changed.

**Recommendation: INVESTIGATE**, and treat as an operational task rather than
a documentation one. Applying the existing policy file to `stvivnestagent2`
would close it.

> **Resolved 2026-08-21: accepted, not fixed.** The user has accepted this as
> a known infrastructure difference between the V1 and V2 environments rather
> than a defect. Recorded in ADR-094 so it reads as a decision. The practical
> consequence stands: the `photos` container in `stvivnestagent2` grows
> without bound until a policy is applied.

### F2 — The environment migration is only half-recorded

ADR-090 records the container registry moving from `vivnestagentacr`
(`rg-vivnest-dev`) to `vivnestagent2acr` (`rg-vivnest-2`), and calls it a
permanent switch. Nothing records the **Function App, storage account or
dashboard** making the same move — yet they clearly did:

| Component | Documented as | Actually in use |
|---|---|---|
| Container registry | `vivnestagent2acr` (ADR-090) | ✅ matches |
| Function App | `vivnestcloudprod` / `rg-vivnest-dev` (ADR at line 716, EVOLUTION-PLAN item 7) | `vivnestcloud2` / `rg-vivnest-2` |
| Storage account | `stvivnestagentdev` (blob-lifecycle README) | `stvivnestagent2` |

The ADR at line 716 is a **point-in-time record of the first deployment** and
is fine as history. `EVOLUTION-PLAN.md` item 7 is not — it is the plan of
record, describing present state, and it names the wrong Function App.

**Recommendation: INVESTIGATE**, then either extend ADR-090 or add a new ADR
covering the whole environment move.

> **Resolved 2026-08-21.** Added **ADR-094**, which maps all five components
> old-to-new, states that `rg-vivnest-dev` is the V1 generation and out of
> scope, and folds in F1 as an accepted difference. `EVOLUTION-PLAN.md` item
> 7 now names the current target while keeping the original deploy as
> history. Older ADRs still naming `vivnestcloudprod` were deliberately left
> alone — ADR-094 is the lens to read them through.

### F3 — `dashboard-domain-model.md` describes a system that no longer exists

Last substantive edit 2026-08-05. Its central table states, for the
vocabulary it exists to define:

| It says | Reality |
|---|---|
| **Agent** — "Heartbeat/event rows only, **no registry table**" | `tblAgentRegistry` exists |
| **Device** — "Config-only … **no registry table**" | `tblDeviceRegistry` exists |
| **Device** — "Identity asserted in agent-local config, not issued by a cloud registry" | Issued by the registry; `RuntimeDeviceId` is the mapping |
| **Capability** — "thin `ICapability` lifecycle interface" | `ICapability` was **deleted** (D1, unused stub) |
| **Device** — "One flat `DeviceOptions` shape … no subtyping" | Still true |

Verified: `tblAgentRegistry`, `tblDeviceRegistry`, `tblCapabilities`,
`tblDeviceCapabilities`, `tblAgentCapabilities`, `tblCapabilityDependencies`
and `tblDeviceTypeCapabilities` all exist in `stvivnestagent2`.

This matters more than an ordinary stale doc because the file's stated
purpose is *"reconciling the dashboard's mental model against what's actually
decided and actually built today."* A reconciliation document that is wrong
about what is built inverts its own value — a reader consulting it to check
their assumptions would be corrected in the wrong direction.

Its §9 "conditional triggers" framing may still hold; that was not audited in
depth here.

**Recommendation: INVESTIGATE.** Either refresh the status column against
today's schema, or mark it HISTORICAL.

> **Resolved 2026-08-21: refreshed, not archived.** Six rows corrected against
> the live schema — Tenant, Site, Agent, Device, Capability, Schedule — plus
> the `DeviceType` enum, which had eight members listed and now has nine
> (`Hub`). A dated note at the top says what changed and why. **The
> vocabulary was deliberately not touched**: those distinctions were correct
> when written, remain correct, and exist nowhere else. Only the "Status
> today" column moved.

### F4 — `Vivnest.Dashboard/README.md` is unmodified vendor boilerplate

It is the stock `npm create vite` React+TS template readme, describing Oxlint
configuration and the React Compiler. It says nothing about Vivnest, its
dashboard, how to run it, or how it authenticates.

**Recommendation: DELETE CANDIDATE** — but only in favour of a real one. The
dashboard is a 48-file application with its own auth model and deployment
target, and it currently has no documentation at all.

> **Resolved 2026-08-21: replaced.** Written from the code rather than
> generically — how to run it, the `x-api-key`/`localStorage` auth model and
> its two honest limitations, the `src/` map, the deployment target, and a
> "things that will bite you" section covering the traps actually hit while
> auditing (a 401 renders as an empty list, and the `VITE_API_BASE_URL`
> fallback port is not the one this repo's Functions host runs on).

---

## Per-file detail

### 1. `CLAUDE.md`
- **Title:** Vivnest
- **Purpose:** Agent-facing orientation; loaded into every session.
- **Phase:** All — it is the standing brief.
- **Related code:** Whole solution; names `IEventHandler`/`EventDispatcher`,
  the queue topology, ADR-091's dual-write, `AgentAuth:RequireApiKey`.
- **Status:** AUTHORITATIVE. Corrected 2026-08-20 (`d7e3fb9`) — it had
  claimed no test project existed.
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Duplicates:** Overlaps `README.md` by design (audience differs).
- **Recommendation: KEEP.**

### 2. `README.md`
- **Title:** Vivnest
- **Purpose:** Human entry point — what each project is, build/run, config.
- **Phase:** All.
- **Related code:** All six projects + dashboard.
- **Status:** AUTHORITATIVE. Testing section corrected 2026-08-20; it had
  said no automated test project existed, when 114 tests exist.
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Duplicates:** Deliberate overlap with `CLAUDE.md`.
- **Recommendation: KEEP.**

### 3. `docs/architecture/current-architecture.md` (2,529 lines)
- **Purpose:** What is actually built, verified against code.
- **Phase:** All, present tense.
- **Related code:** Everything.
- **Status:** AUTHORITATIVE — the single most load-bearing document here.
  Updated repeatedly through 2026-08-21, including a new section on
  deliberate retentions and dismissed findings inherited from the retired
  dead-code audit.
- **Current or historical:** Current, by definition.
- **Conflicts:** None found.
- **Duplicates:** No.
- **Recommendation: KEEP.** Its size is a fair concern, but it is the only
  place several things are written down.

### 4. `docs/architecture/decision-log.md` (9,548 lines, 93 ADRs)
- **Purpose:** Why, not what. Binding architectural rules plus a
  point-in-time record of each decision.
- **Phase:** All.
- **Related code:** Everything.
- **Status:** AUTHORITATIVE.
- **Current or historical:** Both, deliberately. Older ADRs are *supposed* to
  read as of their date — ADR-092 makes this explicit, and older entries
  still say `ICaptureStatusStore` after the rename on purpose.
- **Conflicts:** Two environment references (lines 716, 9139) name
  `rg-vivnest-dev` resources. Both are historical records and correct as
  such; see F2 for the gap they expose.
- **Duplicates:** No.
- **Recommendation: KEEP.** Do not retrofit old ADRs to match today.

### 5. `docs/roadmap/EVOLUTION-PLAN.md` (687 lines)
- **Purpose:** Plan of record — what is next, and what is true now versus
  aspirational.
- **Phase:** Bridges near-term sprints to the target runtime.
- **Related code:** All.
- **Status:** AUTHORITATIVE.
- **Current or historical:** Current.
- **Conflicts:** **Yes.** Item 7 names `vivnestcloudprod` / `rg-vivnest-dev`
  as the deployed Function App; the live target is `vivnestcloud2` /
  `rg-vivnest-2`. See F2. Two other statuses (items 10 and 16) were stale and
  were corrected on 2026-08-21 (`fcf7dec`), which suggests this file drifts
  faster than the others and is worth checking whenever a phase completes.
- **Duplicates:** Overlaps `roadmap.md` by design — this is the
  reconciliation layer on top of it.
- **Recommendation: KEEP + correct item 7.**

### 6. `docs/roadmap/roadmap.md` (947 lines)
- **Purpose:** Full phase roadmap, Phase 1–6, with sprint detail.
- **Phase:** All.
- **Status:** AUTHORITATIVE. Sprint 8 status updated 2026-08-20.
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Duplicates:** See item 5 — layered, not duplicated.
- **Recommendation: KEEP.**

### 7. `docs/JOURNEY.md` (114 lines)
- **Purpose:** The arc in one page — where this started, where it is headed.
- **Phase:** All.
- **Status:** AUTHORITATIVE as narrative. Last touched 2026-08-02, which is
  appropriate: CLAUDE.md describes it as changing rarely by design.
- **Current or historical:** Current, at the level of intent.
- **Conflicts:** None found — it deliberately avoids specifics that would
  date it.
- **Duplicates:** No.
- **Recommendation: KEEP.** The best short orientation in the repository.

### 8. `docs/architecture/vivnest-runtime-overview.md` (244 lines)
- **Purpose:** Target architecture, the "north star".
- **Phase:** Target state (Phase 6-ish).
- **Related code:** None yet — that is the point.
- **Status:** AUTHORITATIVE *as a statement of intent*. Self-labelled
  "Target architecture, not yet implemented."
- **Current or historical:** Neither. Aspirational.
- **Conflicts:** Cannot conflict — it does not claim to describe today.
- **Duplicates:** Records that it already consolidated two earlier drafts
  (`VERA_v1_Architecture.md`, `Vivnest_Edge_Runtime_Architecture_VERA.md`),
  neither of which is in the tree. That consolidation was done properly.
- **Recommendation: KEEP.**

### 9. `docs/architecture/phase-1-runtime-foundation.md` (221 lines)
- **Purpose:** Sprint-level detail for Milestone 1 of the target runtime.
- **Phase:** Target state.
- **Status:** AUTHORITATIVE as a plan. Self-labelled "Not started" — still
  accurate; no `Vivnest.Runtime` or `Vivnest.Abstractions` project exists.
- **Current or historical:** Neither. Aspirational.
- **Conflicts:** None.
- **Duplicates:** Subordinate to item 8, not duplicative.
- **Recommendation: KEEP.** Worth re-reading before starting that work, since
  CLAUDE.md's standing rule is not to extract those abstractions
  speculatively.

### 10. `docs/architecture/dashboard-domain-model.md` (265 lines)
- **Purpose:** Reconcile the dashboard's mental model against what is decided
  and built.
- **Phase:** Phase 3 era (2026-08-05).
- **Related code:** `TenantContext`, heartbeat entities, capability folders,
  the HA bridge.
- **Status:** **OUTDATED.** See F3.
- **Current or historical:** Historical.
- **Conflicts:** **Yes, materially** — wrong about registry tables and about
  `ICapability` existing.
- **Duplicates:** Partially overlaps `current-architecture.md`, but carries
  vocabulary distinctions found nowhere else.
- **Recommendation: INVESTIGATE** — refresh or re-label as historical. Not a
  delete candidate.

### 11. `devops/blob-lifecycle/README.md` (71 lines)
- **Purpose:** Document the Azure-native blob lifecycle policy for capture
  retention.
- **Phase:** Phase 2/3 operational.
- **Related code:** `StorageOptions.BlobContainer`;
  `blob-lifecycle-policy.json` beside it.
- **Status:** **OUTDATED** as to environment. See F1.
- **Current or historical:** Historical.
- **Conflicts:** **Yes** — claims the policy is applied to the production
  account; no policy exists on `stvivnestagent2`.
- **Duplicates:** Companion to item 12, explicitly cross-referenced. Not a
  duplicate.
- **Recommendation: INVESTIGATE.** The document is a symptom; the missing
  policy is the problem.

### 12. `devops/device-event-retention/README.md` (64 lines)
- **Purpose:** Document Table Storage cleanup for `tblDeviceEvents`.
- **Phase:** Phase 3 operational.
- **Related code:** `DeviceEventRetentionTimerFunction`,
  `DeviceEventRetentionService`, `AzureTableDeviceEventReader.DeleteOlderThanAsync`.
- **Status:** AUTHORITATIVE. Settings match the deployed app
  (`DeviceEventRetention__Enabled=true`, `RetentionDays=30`,
  `DeviceEventRetentionCronSchedule=0 0 3 * * *`), including the deliberate
  single-underscore cron name.
- **Current or historical:** Current.
- **Conflicts:** None. Notably it names **no** environment, which is why it
  survived the migration that broke item 11.
- **Recommendation: KEEP.**

### 13. `Vivnest.Agent/Models/README.md` (16 lines)
- **Purpose:** What belongs in the ONNX models folder and where it comes from.
- **Phase:** Phase 5 (AI).
- **Related code:** `DeviceOptions.SinkCleanliness.ModelPath`,
  `ObjectDetectionOptions.ModelPath`, `ObjectDetector`.
- **Status:** AUTHORITATIVE. Includes the AGPL-3.0 note on the Ultralytics
  YOLOv8n export — a licensing constraint worth keeping visible.
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Recommendation: KEEP.**

### 14. `scripts/ml/sink-cleanliness/README.md` (17 lines)
- **Purpose:** Fetch/label/train workflow for the sink-cleanliness model.
- **Phase:** Phase 5.
- **Related code:** ADR-032; produces `Vivnest.Agent/Models/*.onnx`.
- **Status:** AUTHORITATIVE. Explicitly "not part of the shipped product."
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Recommendation: KEEP.**

### 15. `tools/object-detection-tester/README.md` (32 lines)
- **Purpose:** Run the real `ObjectDetector` against local photos.
- **Phase:** Phase 5.
- **Related code:** `Vivnest.Agent/Capabilities/Camera/ObjectDetector.cs`.
- **Status:** AUTHORITATIVE. Confirmed ACTIVE by the retired dead-code audit
  (its U4).
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Recommendation: KEEP.**

### 16. `tools/sink-cleanliness-tester/README.md` (46 lines)
- **Purpose:** Run the real `SinkCleanlinessClassifier` against local photos.
- **Phase:** Phase 5.
- **Related code:** `SinkCleanlinessClassifier`, `SinkCleanlinessWorker`.
- **Status:** AUTHORITATIVE. Documents a genuine subtlety — the classifier
  crops to ROI internally while the detector does not, and the threshold is
  applied downstream rather than inside `Classify`.
- **Current or historical:** Current.
- **Conflicts:** None found.
- **Duplicates:** Sibling of item 15, different subject.
- **Recommendation: KEEP.**

### 17. `Vivnest.Dashboard/README.md` (32 lines)
- **Purpose:** None specific to Vivnest — stock Vite template text.
- **Phase:** N/A.
- **Related code:** Nominally the dashboard; mentions none of it.
- **Status:** **GENERATED** (vendor scaffold, never edited).
- **Current or historical:** Neither.
- **Conflicts:** No — it makes no claims about Vivnest to conflict with.
- **Recommendation: DELETE CANDIDATE**, contingent on writing a real one. See
  F4.

---

## Two candidate duplicates, both dismissed

**`roadmap.md` vs `EVOLUTION-PLAN.md`.** Genuinely layered: roadmap is the
full phase plan, EVOLUTION-PLAN reconciles it against what the code actually
supports and sequences the near term. CLAUDE.md states this relationship
explicitly. Not duplication.

**`current-architecture.md` vs `dashboard-domain-model.md`.** These *do*
overlap, and the second is outdated — but the overlap is not why. The domain
model carries vocabulary (Capability vs Bridge, Native vs Bridged, Health vs
Metrics) that `current-architecture.md` does not define. Consolidating would
mean moving that vocabulary across, not deleting the file. Recorded as
CONSOLIDATE-eligible under F3 rather than DUPLICATE.

---

## What this audit did not do

- Did not verify every claim in the two large documents
  (`current-architecture.md`, `decision-log.md`) line by line. Both were
  spot-checked against today's code and the live environment; a full
  line-by-line reconciliation of 12,000 lines was out of scope.
- Did not audit `dashboard-domain-model.md`'s §9 conditional-trigger
  framing, only its status table.
- Did not examine `.md` files inside `node_modules` (vendor) or any
  generated build output.
