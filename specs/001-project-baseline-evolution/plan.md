# Implementation Plan: Project Baseline & Multi-Target Evolution

**Branch**: `001-project-baseline-evolution` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-project-baseline-evolution/spec.md`

## Summary

Establish a single, validated **capability baseline** of what Metano does today, define a
prioritized **multi-target evolution roadmap** (lead thrust: harden the IR + backend SPI so new
backends are additive), and **reconcile** the legacy `spec/` into the speckit format as the new
canonical home — renaming `spec/` → `old-spec/` and retiring it only after a parity gate passes.
This is a documentation-and-planning feature: no transpiler code changes. The technical approach
is a content-migration + traceability exercise grounded in compiler terminology (frontend = source
ingestion / C#+Roslyn; IR = canonical contract; backends = TypeScript, Dart).

## Technical Context

**Language/Version**: Markdown specification artifacts. The referenced system is .NET 10 / C# 14
(Roslyn 5.3) — read only, for traceability; no code is modified by this feature.

**Primary Dependencies**: Existing `spec/` corpus (12 docs), `docs/adr/` (24 ADRs), the
`Metano.Compiler*` source tree, and the TUnit/bun test suites (used as traceability evidence).

**Storage**: Filesystem markdown under `specs/001-project-baseline-evolution/` (new canonical home)
and the renamed `old-spec/` reference directory.

**Testing**: Validation, not unit tests. Three mechanical checks: (1) every "Implemented" matrix row
resolves to a real `Code area` + `Test`; (2) every legacy stable identifier (`FR-###`, `NFR-###`,
`MS####`) still resolves after migration; (3) no normative item exists in two places with conflicting
wording. A lightweight grep/link script backs checks (2)–(3); check (1) is review-confirmed.

**Target Platform**: Repository documentation consumed by maintainers, adopters, and AI agents.

**Project Type**: Documentation / specification reconciliation for a multi-target compiler.

**Performance Goals**: Human comprehension metric only — SC-002: a newcomer answers "what does Metano
do today + what's next?" in under 15 minutes from the canonical docs alone.

**Constraints**: Preserve all stable identifiers (CR-006); `old-spec/` deleted only after parity gate
(CR-004); update every pointer that names `spec/` as SSOT (CR-007); Mermaid for any diagram; English
throughout; stay consistent with existing ADRs (CR-008).

**Scale/Scope**: ~12 legacy docs to migrate; FR-001–FR-048, NFR-001–NFR-020, MS0001–MS0025, ~21
attributes, 24 ADRs, 2 backends (TS implemented, Dart partial), 1 frontend (C#/Roslyn).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against Constitution v1.0.0 (6 principles):

| Principle | Applicability to this (docs-only) feature | Status |
|-----------|-------------------------------------------|--------|
| I. Clean Code as Baseline | No production code touched. Applies to markdown clarity/structure only. | PASS |
| II. Expressive, Intention-Revealing | Docs MUST use compiler vocabulary (frontend/IR/backend) consistently — enforced by BR-001/BR-002. | PASS |
| III. Screaming, Feature-Semantic Organization | Canonical docs MUST be organized by capability/semantic area, not arbitrary buckets — drives the baseline layout. | PASS |
| IV. Clean Architecture via Ports & Adapters | This feature documents the seam (IR + backend port); the roadmap's #1 thrust (RR-002) is hardening exactly this. No code, so structurally compliant. | PASS |
| V. Developer Experience First | Core intent: one discoverable source of truth (SC-002, CR-005). | PASS |
| VI. Pragmatism Over Dogma | Reuse the existing feature-support matrix instead of inventing parallel structures (Q2); avoid over-structuring docs. | PASS |

**Workflow gates from the constitution:**

- *Spec as source of truth*: This feature **moves** the canonical SSOT from `spec/` into the speckit
  format (Q1). The Governance section and `CLAUDE.md` both name `spec/` as SSOT. Per CR-007 this feature
  updates those **location references** — a pointer edit, not a change to any principle, so no separate
  constitution amendment is required. Tracked as a Complexity entry for visibility.
- *Dual-agent review (compiler-man + bob)*: The rule targets code review. This feature ships no code; the
  equivalent gate is a content review for terminology consistency and identifier integrity (polish-phase task).
- *Mermaid diagrams / English / conventional commits*: honored.

No unjustified violations. Gate: **PASS**.

## Project Structure

### Documentation (this feature)

```text
specs/001-project-baseline-evolution/
├── plan.md              # This file (/speckit-plan output)
├── spec.md              # Feature spec (/speckit-specify + /speckit-clarify)
├── research.md          # Phase 0 output — migration & traceability decisions
├── data-model.md        # Phase 1 output — document/artifact schemas
├── quickstart.md        # Phase 1 output — how to navigate & validate the baseline
├── contracts/           # Phase 1 output — canonical document-format contracts
│   ├── baseline-matrix.contract.md
│   ├── roadmap-thrust.contract.md
│   └── reconciliation-ledger.contract.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (already created)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Canonical product spec (target of the migration)

```text
specs/001-project-baseline-evolution/   # NEW canonical home (speckit format, per Q1)
  baseline/                              # Migrated capability baseline (by semantic area)
    00-overview.md                       #   frontend / IR / backend framing
    feature-support-matrix.md            #   status + Code area + Test columns (Q2)
    attribute-catalog.md
    diagnostic-catalog.md
  roadmap/                               # Prioritized evolution thrusts
    00-roadmap.md                        #   ranked: SPI-hardening (P1) → Dart parity → frontend posture
  reconciliation-ledger.md               # Disposition of every legacy item (migrated/retained/retired)

old-spec/                                # RENAMED from spec/ — comparison reference only,
                                         # retired (deleted) once parity gate (CR-004) passes
```

**Structure Decision**: Single-repo documentation feature. The speckit feature directory is the new
canonical home (Q1=B). Baseline content is organized by **semantic capability area** (Principle III)
rather than by legacy file numbering. `old-spec/` is the renamed legacy tree, kept only as a parity
reference. No `src/`/`tests/` layout applies — this feature emits documentation, and the referenced
code/tests are read-only traceability evidence.

## Complexity Tracking

> Only deviations needing justification are listed.

| Deviation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Move canonical SSOT out of `spec/` into the speckit feature dir, requiring edits to the constitution Governance pointer + `CLAUDE.md` (CR-007) | Explicit user decision (Q1=B): consolidate everything into the speckit format so there is one workflow and one home | Keeping `spec/` canonical (Option A) was simpler and matched current docs, but the user wants a single speckit-driven source; two homes reproduce the drift the product fights |
| Retain `old-spec/` in-tree temporarily instead of deleting on migration | Parity gate (CR-004) protects against silently losing normative content during migration | Immediate deletion risks dropping requirements before parity is proven; the temporary duplicate is bounded and governed |
