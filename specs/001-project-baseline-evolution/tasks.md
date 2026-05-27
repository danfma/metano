---
description: "Task list for Project Baseline & Multi-Target Evolution"
---

# Tasks: Project Baseline & Multi-Target Evolution

**Input**: Design documents from `/specs/001-project-baseline-evolution/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Not requested (documentation feature). No TDD/unit-test tasks are generated; instead each
story carries explicit **validation** tasks tied to the spec's Success Criteria (SC-001..SC-006).

**Organization**: Tasks grouped by user story (US1 baseline → US2 roadmap → US3 reconciliation).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (user-story phases only)
- All paths are repo-relative. Canonical home = `specs/001-project-baseline-evolution/`.

## Path Conventions

- Canonical baseline → `specs/001-project-baseline-evolution/baseline/`
- Roadmap → `specs/001-project-baseline-evolution/roadmap/`
- Ledger → `specs/001-project-baseline-evolution/reconciliation-ledger.md`
- Legacy reference → `old-spec/` (renamed from `spec/`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding for the canonical documentation home.

- [X] T001 Create canonical scaffolding dirs `specs/001-project-baseline-evolution/baseline/` and `specs/001-project-baseline-evolution/roadmap/`
- [X] T002 [P] Build a working raw-material index mapping each `spec/` doc to its target capability area, saved as `specs/001-project-baseline-evolution/baseline/.migration-index.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Structural prerequisites every story depends on.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 Rename the legacy spec directory with `git mv spec old-spec` (blocks every migration that references `old-spec/`; CR-003)
- [X] T004 Create `specs/001-project-baseline-evolution/reconciliation-ledger.md` skeleton (table headers + SSOT-pointer section) per `contracts/reconciliation-ledger.contract.md`
- [X] T005 [P] Extract the stable-identifier inventory (`FR-###`, `NFR-###`, `MS####`, attribute names) from `old-spec/` into `specs/001-project-baseline-evolution/baseline/.identifier-inventory.md` (drives CR-006 / SC-005)

**Checkpoint**: Canonical home scaffolded, legacy renamed, identifier inventory ready — stories can begin.

---

## Phase 3: User Story 1 - Authoritative capability baseline (Priority: P1) 🎯 MVP

**Goal**: One validated, drift-free inventory of what Metano does today, in compiler terms.

**Independent Test**: Pick any `Implemented` row → confirm its `Code area` + `Test` exist; pick any
`Partial`/`Planned` row → confirm stated gaps match reality. Pass = no drift either direction (SC-001).

- [X] T006 [US1] Write `specs/001-project-baseline-evolution/baseline/00-overview.md` with the frontend (C#/Roslyn) → IR → backends (TypeScript, Dart) framing and a Mermaid hourglass diagram (BR-001, BR-002, BR-003)
- [X] T007 [US1] Migrate the feature-support matrix from `old-spec/08-feature-support-matrix.md` into `specs/001-project-baseline-evolution/baseline/feature-support-matrix.md`, including the Target Support table (TS Implemented, Dart Partial, C#/Roslyn frontend) (BR-003, BR-004)
- [X] T008 [US1] Add `Code area` and `Test` columns and fill traceability for every `Implemented` row by locating the source path + at least one test in `src/Metano.Compiler*` and `tests/Metano.Tests` (BR-005, SC-001) — per `contracts/baseline-matrix.contract.md`
- [X] T009 [P] [US1] Enumerate explicit `Constraints` for every `Partial` row (Dart gaps, classes/inheritance, interfaces/generics, pattern matching, operator lowerings) in `specs/001-project-baseline-evolution/baseline/feature-support-matrix.md` (BR-004)
- [X] T010 [P] [US1] Migrate the attribute catalog from `old-spec/09-attribute-catalog.md` into `specs/001-project-baseline-evolution/baseline/attribute-catalog.md`, preserving attribute names verbatim (BR-004, CR-006)
- [X] T011 [P] [US1] Migrate the diagnostic catalog from `old-spec/10-diagnostic-catalog.md` into `specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md`, preserving `MS0001`–`MS0025` (BR-006, CR-006)
- [X] T012 [US1] Record dispositions for all US1-migrated items (matrix, attributes, diagnostics, vision/scope/architecture docs) in `reconciliation-ledger.md` (CR-001)
- [X] T013 [US1] Validate SC-001: confirm every `Implemented` row resolves to a real `Code area` + `Test`; fix any drift found

**Checkpoint**: Baseline is complete, validated, and independently usable (MVP).

---

## Phase 4: User Story 2 - Prioritized multi-target evolution roadmap (Priority: P2)

**Goal**: Ranked, traceable evolution thrusts; lead thrust = harden the multi-target SPI.

**Independent Test**: Each thrust reads as a standalone future feature with outcome + rank + traceability;
the rank-1 thrust is SPI hardening with an additive-only outcome (SC-003, SC-006).

**Dependency**: Uses the Dart gaps enumerated in US1 (T009) for the parity thrust.

- [X] T014 [US2] Write `specs/001-project-baseline-evolution/roadmap/00-roadmap.md` with `RT-01 — Harden multi-target SPI` at rank 1 and the additive-only outcome (stabilize IR + backend port; new backend touches only its own artifacts) per `contracts/roadmap-thrust.contract.md` (RR-002, SC-003)
- [X] T015 [US2] Add `RT-02 — Dart backend to parity`, scoped to the enumerated Dart gaps from the matrix (extension methods, `[ModuleEntryPoint]` body, JSON serializer context, + any others surfaced) (RR-003)
- [X] T016 [US2] Add `RT-03 — Frontend-extensibility posture` (keep IR frontend-agnostic; additional source languages deferred, not implemented) (RR-004)
- [X] T017 [P] [US2] For each thrust, fill Outcome / Traceability / Dependencies / Scope note so it can seed its own `/speckit-specify` without extra context (RR-001, RR-005, SC-006)
- [X] T018 [US2] Verify no implementation steps leaked into any thrust; roadmap is documentation + prioritization only (RR-006)

**Checkpoint**: Roadmap stands alone; #1 priority and its additive-only target are stated clearly.

---

## Phase 5: User Story 3 - Single source of truth (legacy reconciliation) (Priority: P3)

**Goal**: Exactly one authoritative source; legacy content migrated/retained/retired with reasons.

**Independent Test**: Every legacy normative item has a recorded disposition; every stable identifier
resolves once; no conflicting duplicate wording remains (SC-004, SC-005).

**Dependency**: Builds on US1/US2 migrations recorded in the ledger.

- [X] T019 [US3] Complete `reconciliation-ledger.md`: one row per legacy normative item (`FR/NFR/MS`, attributes, free-standing statements) with `Disposition` ∈ {Migrated, Retained, Retired} and reasons for retirement (CR-001, SC-004)
- [X] T020 [US3] Establish the single authoritative entry point and remove any normative item existing in two places with conflicting wording (CR-005, INV-2)
- [X] T021 [US3] Verify identifier preservation — run the `grep` pass from `quickstart.md` so every `FR-###`/`NFR-###`/`MS####` resolves to exactly one canonical definition (CR-006, SC-005, INV-3)
- [X] T022 [US3] Update SSOT pointers: repoint the constitution Governance "runtime guidance / spec" reference in `.specify/memory/constitution.md` and the `spec/`-as-source-of-truth text in `CLAUDE.md` to the canonical speckit location (CR-007)
- [X] T023 [US3] Confirm the parity gate: assert zero `Unprocessed` ledger rows; mark `old-spec/` eligible for deletion but DO NOT delete it yet (CR-004)
- [X] T024 [P] [US3] Cross-check every architecture-affecting disposition against the relevant ADR in `docs/adr/` for consistency (CR-008)

**Checkpoint**: One source of truth; identifiers intact; `old-spec/` retained pending parity sign-off.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Discoverability, style, and final validation across all stories.

- [X] T025 [P] Add a canonical entry-point `specs/001-project-baseline-evolution/baseline/README.md` linking overview, matrix, catalogs, roadmap, and ledger (DX, SC-002)
- [X] T026 [P] Ensure all diagrams are Mermaid and all canonical docs are in English (constitution conventions)
- [X] T027 Content review for terminology consistency (frontend / IR / backend) and identifier integrity — the docs-feature equivalent of the constitution's dual-agent review gate
- [X] T028 Run all `quickstart.md` validation commands and confirm SC-001, SC-003, SC-004, SC-005 pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all stories (rename + ledger skeleton + id inventory).
- **US1 (Phase 3)**: Depends on Foundational. The MVP.
- **US2 (Phase 4)**: Depends on US1 (consumes Dart gaps from the matrix).
- **US3 (Phase 5)**: Depends on US1 + US2 (reconciles their migrations).
- **Polish (Phase 6)**: Depends on US1–US3.

> Note: unlike a greenfield build, these stories are **sequential** (P1 → P2 → P3) because each migrates
> content the next one references. US1 alone is still a shippable, independently valuable increment.

### Within Each User Story

- US1: T006 and T007 before T008 (columns need the migrated matrix); T009/T010/T011 parallel; T012 after migrations; T013 last.
- US2: T014 before T015/T016 (file created first); T017 parallel after thrusts exist; T018 last.
- US3: T019 before T020/T021; T022 independent; T023 after T019; T024 parallel.

### Parallel Opportunities

- Setup: T002 ∥ (after T001).
- US1: T009 ∥ T010 ∥ T011 (different files).
- US2: T017 (single-file edits per thrust — treat as parallelizable notes).
- US3: T024 ∥ others.
- Polish: T025 ∥ T026.

---

## Parallel Example: User Story 1

```text
# After T006 + T007 + T008, launch the independent migrations together:
Task: "T009 Enumerate Partial-row constraints in baseline/feature-support-matrix.md"
Task: "T010 Migrate attribute catalog to baseline/attribute-catalog.md"
Task: "T011 Migrate diagnostic catalog to baseline/diagnostic-catalog.md"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1) — the validated capability baseline.
3. **STOP and VALIDATE**: SC-001 passes (every Implemented row traceable). This alone is shippable value
   (onboarding, support, AI context).

### Incremental Delivery

1. Setup + Foundational → ready.
2. US1 → validated baseline (MVP).
3. US2 → roadmap with SPI-hardening as #1.
4. US3 → single source of truth + SSOT pointer updates; `old-spec/` retained pending parity sign-off.
5. Polish → discoverability + final validation.

### Notes

- `old-spec/` is **not** deleted by this feature — only marked eligible after the parity gate (CR-004).
- No transpiler code is modified; `src/` and `tests/` are read-only traceability evidence.
- Commit after each task or logical group; conventional commits; reference the feature where relevant.
