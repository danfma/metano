# Phase 0 Research: Project Baseline & Multi-Target Evolution

No `NEEDS CLARIFICATION` markers remained after `/speckit-clarify`. This document records the
decisions that shape Phase 1, with rationale and rejected alternatives.

## D1 — Canonical home for baseline + roadmap

- **Decision**: The speckit feature directory (`specs/001-project-baseline-evolution/`) is the new
  canonical home. Legacy `spec/` is migrated into speckit format, renamed `spec/` → `old-spec/`,
  and retired only after the parity gate.
- **Rationale**: User decision (clarify Q1=B). One workflow, one home; eliminates the dual-normative-
  source drift the product itself fights.
- **Alternatives considered**: (A) keep `spec/` canonical and reorganize in place — matched current
  constitution/CLAUDE.md SSOT pointer but leaves the speckit flow secondary; (C) a third dedicated
  location — adds a home without removing one. Both rejected in favor of consolidation.

## D2 — Baseline traceability artifact

- **Decision**: Extend the migrated feature-support matrix with `Code area` and `Test` columns. One
  artifact carries status + traceability.
- **Rationale**: User decision (clarify Q2=C). Reuses a known structure (`08-feature-support-matrix.md`),
  makes SC-001 mechanically checkable, avoids a parallel matrix (Principle VI — pragmatism).
- **Alternatives considered**: standalone matrix (A) — duplicate structure; narrative prose (B) — not
  mechanically verifiable. Rejected.

## D3 — Compiler terminology (frontend / IR / backend)

- **Decision**: Adopt strict compiler vocabulary across all canonical docs: **frontend** = source-language
  ingestion (C#/Roslyn); **IR** = canonical semantic representation (ADR-0013); **backend** = target-language
  code generator (TypeScript, Dart).
- **Rationale**: Corrects the informal usage in the original request; aligns with Principle II and with the
  evolution thesis (multiple backends now, multiple frontends conceivable later). The IR is the hourglass waist.
- **Alternatives considered**: keep the legacy "target" wording only — rejected because the multi-frontend
  future is unclear without the frontend/backend split being explicit.

## D4 — Baseline organization (semantic area vs. legacy numbering)

- **Decision**: Organize the migrated baseline by **semantic capability area** (overview, feature-support
  matrix, attribute catalog, diagnostic catalog, …), not by the legacy `01..11` file numbering.
- **Rationale**: Principle III (screaming/feature-semantic organization). The tree should reveal capabilities.
- **Alternatives considered**: 1:1 file-number migration — preserves order but encodes no meaning; rejected.

## D5 — Parity gate before deleting `old-spec/`

- **Decision**: `old-spec/` stays in-tree until every retained normative item is present (migrated or
  explicitly superseded) in the canonical source; only then is it deleted (CR-004).
- **Rationale**: Prevents silent loss of requirements during migration. Bounded, governed duplication.
- **Alternatives considered**: delete on migration — risk of dropping content; rejected.

## D6 — Identifier preservation strategy

- **Decision**: Carry the existing identifier spaces verbatim (`FR-001`–`FR-048`, `NFR-001`–`NFR-020`,
  `MS0001`–`MS0025`, attribute names). New baseline-feature requirements use the spec's own `BR-`/`RR-`/`CR-`
  prefixes and do not collide.
- **Rationale**: CR-006 — existing references in issues/tests/commits must not break.
- **Alternatives considered**: renumber into a unified scheme — breaks external references; rejected.

## D7 — Roadmap thrust recording format

- **Decision**: Record thrusts as ranked entries in `roadmap/00-roadmap.md`, each self-contained
  (outcome, priority, dependencies, baseline/vision traceability). Conversion to GitHub issues is deferred to
  `/speckit-taskstoissues` or per-thrust `/speckit-specify` later.
- **Rationale**: RR-005/RR-006 — documentation + prioritization only, no implementation. A markdown roadmap is
  the lightest artifact that satisfies "seed a future spec without re-deriving context."
- **Alternatives considered**: open GitHub issues now — premature commitment before per-thrust specs; rejected.

## D8 — Validation tooling

- **Decision**: A lightweight shell/grep pass verifies (a) no dangling stable identifiers and (b) no
  duplicated-with-conflict normative text between `old-spec/` and the canonical source. Matrix code/test
  traceability (SC-001) is confirmed by content review.
- **Rationale**: Matches a docs feature; avoids building test infrastructure for prose.
- **Alternatives considered**: a custom linter — over-engineered for a one-time migration (Principle VI).
