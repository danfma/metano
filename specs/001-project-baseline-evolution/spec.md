# Feature Specification: Project Baseline & Multi-Target Evolution

**Feature Branch**: `001-project-baseline-evolution`

**Created**: 2026-05-27

**Status**: Draft

**Input**: User description: "Import this existing project to understand all its current funcionalities, and let's prepare the next evolution steps. A quick note, this project is a transpiler initially focused on C# to TypeScript, but with others backend (like Dart), and even frontends in mind, for the future."

## Context

Metano is an established source-to-source transpiler. In compiler terms it has a
**frontend** (source-language ingestion — today C#/Roslyn), a canonical
**intermediate representation** (IR), and one or more **backends** (target-language
code generators — TypeScript today, Dart partially). The product already ships
substantial functionality and a formal requirements set under `spec/`
(`FR-001`–`FR-048`, `NFR-001`–`NFR-020`), a feature support matrix, an attribute
catalog, a diagnostic catalog, and 24 ADRs.

This feature does **not** add transpiler capability. It (1) establishes a single,
validated **baseline** of what Metano does today, (2) defines a prioritized
**evolution roadmap** for growing into a multi-backend (and eventually multi-frontend)
transpiler, and (3) reconciles the legacy `spec/` material so the project keeps a
single source of truth. Each concrete evolution increment (e.g. Dart parity, a new
backend) is delivered later through its own specification.

## Clarifications

### Session 2026-05-27

- Q: Where should the canonical baseline + roadmap live, and what happens to the legacy `spec/`? → A: The speckit feature directory becomes canonical; migrate legacy descriptions/specs into the speckit format, and rename `spec/` → `old-spec/` as a comparison reference kept only until the new spec reaches parity, then retire it.
- Q: How should baseline traceability (capability → code + test) be captured? → A: Extend the migrated feature-support matrix with `Code area` and `Test` columns — one artifact carries both status and traceability.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authoritative capability baseline (Priority: P1)

A maintainer, adopter, or AI agent needs to answer "what does Metano support **today**?"
from one authoritative source that provably matches the code, instead of inferring from
scattered docs, samples, and ADRs.

**Why this priority**: A roadmap is only trustworthy if the starting point is accurate.
The baseline is the prerequisite for every later decision and is independently valuable
on its own (onboarding, support, AI context).

**Independent Test**: Pick any capability the baseline marks "Implemented" and confirm a
corresponding code path and test exist; pick any "Partial"/"Planned" item and confirm the
baseline's stated gaps match reality. The baseline passes if there is no drift in either
direction.

**Acceptance Scenarios**:

1. **Given** the baseline inventory, **When** a reader looks up any current capability,
   **Then** its status (Implemented / Partial / Planned) and constraints are stated and
   match the codebase and tests.
2. **Given** the frontend/IR/backend framing, **When** a reader inspects the baseline,
   **Then** C#/Roslyn is identified as the current frontend, the IR as the canonical
   contract, and TypeScript (implemented) and Dart (partial) as the current backends.
3. **Given** a capability with constraints, **When** the reader checks it, **Then** the
   specific unsupported sub-cases are enumerated rather than left implicit.

---

### User Story 2 - Prioritized multi-target evolution roadmap (Priority: P2)

A maintainer needs a prioritized, traceable list of evolution thrusts so future work can
be picked up as discrete specifications in the right order, with the multi-target SPI
hardening recognized as the lead thrust.

**Why this priority**: Once the baseline exists, the team needs an agreed direction.
The roadmap turns intent ("more backends, eventually more frontends") into ordered,
testable commitments without committing implementation in this feature.

**Independent Test**: Each roadmap thrust can be read as a standalone future feature with
a clear outcome, a priority rank, and a link back to the baseline gap or vision it
addresses; the SPI-hardening thrust is ranked first.

**Acceptance Scenarios**:

1. **Given** the roadmap, **When** a maintainer reads the top-ranked thrust, **Then** it is
   "harden the multi-target SPI" — stabilize the IR contract and backend port so a new
   backend is additive (no edits to the core or existing backends).
2. **Given** the roadmap, **When** a maintainer reads subsequent thrusts, **Then** Dart
   backend parity and a frontend-extensibility posture are present, each ranked and scoped.
3. **Given** any roadmap thrust, **When** it is selected for delivery, **Then** it carries
   enough definition (outcome + priority + baseline traceability) to seed its own
   `/speckit-specify` without re-deriving context.

---

### User Story 3 - Single source of truth (legacy spec reconciliation) (Priority: P3)

A maintainer must not be left with two competing normative bodies (the legacy `spec/`
directory and this baseline). The legacy material is reorganized, migrated, or retired so
exactly one authoritative source remains.

**Why this priority**: Duplicate specs cause drift — the exact problem the product itself
fights. Valuable, but only after the baseline and roadmap exist to migrate toward.

**Independent Test**: After reconciliation there is no requirement that exists in two
places with conflicting wording; every retained legacy requirement is either preserved
verbatim under the canonical structure or explicitly superseded with a recorded rationale.

**Acceptance Scenarios**:

1. **Given** the legacy `spec/` documents, **When** reconciliation completes, **Then** each
   normative item is migrated, kept, or retired with a stated reason — none silently dropped.
2. **Given** the reconciled state, **When** a reader looks for the current requirements,
   **Then** a single entry point identifies where the authoritative requirements live.
3. **Given** stable identifiers (`FR-###`, `NFR-###`, `MS####`), **When** reconciliation
   completes, **Then** those identifiers remain resolvable so existing references in
   issues, tests, and commits do not break.

---

### Edge Cases

- A capability is present in code but absent from the baseline → baseline is incomplete and
  MUST be corrected (drift in the "missing" direction).
- A capability is documented as Implemented but no longer works / was removed → baseline
  MUST downgrade it and note the change.
- A legacy requirement conflicts with an ADR or current behavior → reconciliation MUST flag
  the conflict and resolve it explicitly, not silently pick one.
- A roadmap thrust depends on another (e.g. Dart parity benefits from SPI hardening) →
  dependency MUST be stated so ordering is not accidental.
- A future frontend (non-C# source language) is hypothesized → the baseline/roadmap MUST
  keep the IR description frontend-agnostic rather than assuming C# is the only input.

## Requirements *(mandatory)*

### Functional Requirements

Baseline (capability inventory):

- **BR-001**: The baseline MUST present Metano's architecture using compiler terminology:
  frontend (source ingestion), IR (canonical semantic representation), and backends
  (target code generators).
- **BR-002**: The baseline MUST identify C#/Roslyn as the sole current frontend and MUST
  NOT assert that C# is the only possible frontend forever.
- **BR-003**: The baseline MUST enumerate current backends with status — TypeScript
  (Implemented, reference backend) and Dart/Flutter (Partial) — including Dart's known gaps.
- **BR-004**: The baseline MUST inventory current capabilities (type selection, type/enum/
  async/exception/pattern/operator/overload lowering, collections, LINQ, modules, output &
  packaging, serialization, type guards, diagnostics, assets) with a status of Implemented,
  Partial, or Planned, and MUST enumerate constraints for Partial items.
- **BR-005**: Every capability marked Implemented in the baseline MUST be traceable to code
  and at least one test; every Partial/Planned item MUST state what is missing. Traceability
  MUST be captured by extending the migrated feature-support matrix with `Code area` and
  `Test` columns, so a single artifact carries both status and traceability.
- **BR-006**: The baseline MUST preserve the existing stable identifier spaces (`FR-###`,
  `NFR-###`, attribute catalog, `MS####` diagnostics) so prior references remain valid.

Roadmap (evolution direction):

- **RR-001**: The roadmap MUST list evolution thrusts as discrete, prioritized items, each
  with an intended outcome and traceability to a baseline gap or product-vision statement.
- **RR-002**: The roadmap MUST rank "harden the multi-target SPI" as the highest-priority
  thrust: stabilize the IR contract and the backend port so adding a backend requires
  changes only in the new backend, with zero edits to the core or existing backends.
- **RR-003**: The roadmap MUST include "Dart backend to parity" as a thrust, scoped to the
  baseline's enumerated Dart gaps (classic extension methods, `[ModuleEntryPoint]` body,
  JSON serializer context, and any others surfaced during baseline capture).
- **RR-004**: The roadmap MUST include a "frontend-extensibility posture" thrust that keeps
  the IR frontend-agnostic and records additional source languages as a deferred future
  direction (no new frontend implemented or committed by this feature).
- **RR-005**: Each roadmap thrust MUST carry enough definition to seed its own future
  specification without re-deriving baseline context, and MUST state dependencies on other
  thrusts where they exist.
- **RR-006**: The roadmap MUST NOT include implementation of any thrust; this feature
  delivers documentation and prioritization only.

Reconciliation (single source of truth):

- **CR-001**: The reconciliation MUST classify every legacy `spec/` normative item as
  migrated, retained, or retired, with a recorded reason for retirement or supersession.
- **CR-002**: The speckit feature directory MUST become the canonical home for the baseline
  and roadmap; legacy `spec/` content MUST be migrated into the speckit specification format
  rather than left as a parallel normative source.
- **CR-003**: The legacy directory MUST be renamed `spec/` → `old-spec/` and retained only
  as a comparison reference; it MUST NOT be treated as authoritative once renamed.
- **CR-004**: `old-spec/` MUST only be retired (deleted) after the migrated speckit baseline
  reaches parity with it — i.e., every retained normative item from `old-spec/` is present
  (migrated or explicitly superseded) in the canonical speckit source. Until that parity
  gate passes, `old-spec/` stays in the tree.
- **CR-005**: After reconciliation a single authoritative entry point MUST identify where
  the current requirements live; no normative item may exist in two places with conflicting
  wording.
- **CR-006**: Reconciliation MUST keep stable identifiers resolvable (no broken `FR-###`/
  `NFR-###`/`MS####` references) even when documents are moved or merged into the speckit
  format.
- **CR-007**: Because the canonical home moves, any document that points to `spec/` as the
  single source of truth (notably the project constitution and `CLAUDE.md`) MUST be updated
  to point at the new canonical location; the conflict MUST be surfaced, not left stale.
- **CR-008**: Reconciliation decisions affecting architecture MUST stay consistent with the
  existing ADRs (notably the target-agnostic core and shared-IR ADRs); any divergence MUST
  be surfaced rather than applied silently.

### Key Entities

- **Frontend**: The source-language ingestion stage (C#/Roslyn today) that produces the IR.
- **Intermediate Representation (IR)**: The canonical, language-neutral semantic model that
  decouples frontend from backends — the stable contract the evolution centers on.
- **Backend (Target)**: A target-language code generator consuming the IR (TypeScript;
  Dart, partial). Each backend implements the backend port against the IR.
- **Capability Baseline**: The validated inventory of current functionality with statuses,
  constraints, and code/test traceability.
- **Roadmap Thrust**: A prioritized, future-facing evolution item with outcome, priority,
  dependencies, and baseline/vision traceability.
- **Legacy Spec Item**: A normative statement in the existing `spec/` directory subject to
  migration, retention, or retirement during reconciliation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of capabilities marked "Implemented" in the baseline are traceable to a
  code path and at least one test; reviewers find zero drift in either direction during
  validation.
- **SC-002**: A reader unfamiliar with the project can answer "what does Metano support
  today, and what is the next priority?" using only the baseline and roadmap, in under 15
  minutes, without reading source code.
- **SC-003**: The roadmap ranks the multi-target SPI hardening as the #1 thrust, and a
  maintainer can describe, from the roadmap alone, the additive-only target: adding a new
  backend touches only the new backend's own artifacts.
- **SC-004**: After reconciliation there are zero requirements duplicated across the legacy
  `spec/` and the canonical source with conflicting wording, and 100% of legacy normative
  items have a recorded disposition (migrated / retained / retired).
- **SC-005**: Every stable identifier referenced before this feature (`FR-###`, `NFR-###`,
  `MS####`) still resolves to a definition after reconciliation (no dangling references).
- **SC-006**: Every roadmap thrust is self-contained enough that a reviewer can start its
  own specification from the thrust entry without requesting additional context.

## Assumptions

- This feature is **documentation and planning only** — baseline capture, roadmap
  definition, and legacy reconciliation. No transpiler code is changed here.
- "Frontend" and "backend" follow compiler terminology: frontend = source-language
  ingestion (C#/Roslyn), backend = target-language generation (TypeScript, Dart). The
  earlier informal use of those words is corrected accordingly.
- Additional source-language frontends and framework-specific frontend code generation are
  out of scope for this feature; they are recorded only as deferred future directions, and
  the architecture is kept from foreclosing them.
- The existing `spec/` content is broadly accurate and serves as primary raw material; it is
  migrated into the speckit specification format (the new canonical home), renamed to
  `old-spec/` as a comparison reference, and retired only after the canonical baseline
  reaches parity with it — preserving normative intent and stable identifiers throughout.
- Concrete delivery of any roadmap thrust (Dart parity, a new backend, SPI changes) happens
  in separate, later specifications and is governed by the project constitution at delivery
  time.
- The 24 existing ADRs remain authoritative for architectural rationale; the baseline and
  roadmap reference them rather than restating or overriding them.
