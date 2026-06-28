---
description: "Task list for feature 006 — complete imports for nested record variants"
---

# Tasks: Complete imports for nested record variants

**Input**: Design documents from `specs/006-fix-nested-variant-imports/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R4), data-model.md, contracts/import-completeness.md (C1–C6), quickstart.md

**Tests**: INCLUDED — the spec explicitly requires golden coverage (US3 / FR-008) and the constitution mandates golden tests accompany transpiler behavior. Tests are written **first** and must FAIL before the production edits land.

**Organization**: Grouped by user story (US1 P1, US2 P2, US3 P3). The two production edits live in two layers (core IR + TS target) and are independent of each other.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (omitted for Setup, Foundational, Polish)

## Path Conventions

Compiler with target-agnostic core + per-language adapters. Core: `src/Metano.Compiler/`. TS target: `src/Metano.Compiler.TypeScript/`. Tests: `tests/Metano.Tests/` (golden fixtures under `tests/Metano.Tests/Expected/`). Downstream consumer (out of scope for edits): `../../Vigiata`.

---

## Phase 1: Setup (Shared Baseline)

**Purpose**: Establish a clean, reproducible baseline and the pre-fix failure oracle before touching code.

- [X] T001 Confirmed clean baseline: build + full TUnit suite green on `main` before changes.
- [X] T002 [P] Captured the pre-fix oracle: the two `TS2304` errors (`UserProfile`, `valueEquals`) on the real Vigiata output (and reproduced by the failing golden T005).
- [X] T003 [P] Confirmed scope safety: `.NestedTypes` read only by `IrRuntimeRequirementScanner.cs:83-84`; no existing `Expected/` fixture contains a `namespace` block.

---

## Phase 2: Foundational (Blocking Prerequisite)

**Purpose**: Shared test scaffolding used by every user story's golden assertions.

**⚠️ CRITICAL**: Complete before the story phases so all three stories assert against one fixture set.

- [X] T004 Created `tests/Metano.Tests/NestedRecordVariantImportTests.cs` with the shared reproduction C# source (abstract record + `Unauthorized` + `UserProfileLoaded(UserProfileDto UserProfile)` + renamed `UserProfileDto` → `UserProfile`).

**Checkpoint**: Harness ready — story phases can begin.

---

## Phase 3: User Story 1 - Nested-variant files type-check out of the box (Priority: P1) 🎯 MVP

**Goal**: A variant referencing another generated type and carrying a value-equality field produces a `.ts` file that type-checks — both the intra-project type import (defect 1) and the `valueEquals` runtime import (defect 2) are emitted.

**Independent Test**: Transpile the reproduction and assert the variant file imports the renamed type and `valueEquals` (next to `HashCode`); regenerated Vigiata output passes `tsc --noEmit` with zero `TS2304`.

### Tests for User Story 1 (write first — MUST FAIL on current code) ⚠️

- [X] T005 [US1] Add failing golden assertions for contract cases **C1 + C2** in `tests/Metano.Tests/NestedRecordVariantImportTests.cs`: the variant file imports the intra-project renamed type (`UserProfile` from its own file) AND `valueEquals` alongside `HashCode`. Confirmed FAILED against `main` before the fix (2/3 failing).

### Implementation for User Story 1

- [X] T006 [P] [US1] Fix defect 1 in `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs` (`case TsNamespaceDeclaration`): recurse `CollectFromTopLevel` over `ns.Members`, and route `ns.Functions` through the same `CollectFromTopLevel(func, sink)` entry point.
- [X] T007 [P] [US1] Fix defect 2 — **Strategy B (revised after review; see research.md R2)**: in `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`, `ScanIrRuntimeRequirements` delegates to a recursive `ScanTypeAndEmittedNested` that scans each emitted nested companion (gated by `IsExportableNestedType`, reusing `GetOrExtractIr`, applying the same `[Import]`/`[Ignore]`/entry-point skips). The core `IrClassExtractor` is left untouched. *(The original plan to populate `NestedTypes` in the core was implemented, reviewed, and reverted — it regressed Dart and over-approximated `[Import]`.)*
- [X] T008 [US1] Filter alignment: the nested scan reuses `IsExportableNestedType` (the exact emission gate) verbatim — confirmed by compiler-man that scan enumeration cannot drift from `TransformNestedTypes`.
- [X] T009 [US1] Full TUnit suite green (1191 pass / 0 fail / 7 pre-existing skips); `dotnet csharpier check .` clean; `dotnet build Metano.slnx` 0 warnings/0 errors.
- [X] T010 [US1] Regenerated the **real** Vigiata.Contracts to a scratch dir (no mutation of their working copy) — `get-user-profile-response.ts` now emits `import { HashCode, valueEquals } from "metano-runtime"` and `import type { UserProfile } from "./user-profile"` (SC-001/SC-004).

**Checkpoint**: MVP — the reported bug is fixed and proven by golden test + external type-check.

---

## Phase 4: User Story 2 - Import completeness for every symbol kind used in a variant (Priority: P2)

**Goal**: The `Members` recursion covers all symbol kinds, not just `UserProfile` — cross-package types, value references, and guards used only in a variant are imported.

**Independent Test**: Transpile variants referencing a cross-package type / a `new`/`instanceof` value ref / a generated guard used nowhere else, then type-check — all references resolve.

### Tests for User Story 2 (write first) ⚠️

- [X] T011 [P] [US2] Added cross-package golden **C6** (`NestedVariant_ImportsCrossPackageTypeReferencedOnlyInVariant`) via `TranspileWithLibrary`: a variant field from another `[EmitPackage]` assembly → cross-package import emitted with the correct specifier.
- [~] T012 [P] [US2] Value-reference / guard generality is **covered by construction** — the `ns.Members` recursion reuses the complete `TsClass`/`TsFunction` collection path (value names, origins, guards), as compiler-man confirmed. No dedicated golden added (the cross-package + type-ref goldens exercise the same path); flagged as optional follow-up if a regression ever surfaces.

### Implementation for User Story 2

- [X] T013 [US2] Confirmed: US2 cross-package golden passes on the US1 `ImportCollector` fix with **no further production change**.

**Checkpoint**: Import completeness is general, not reproduction-specific.

---

## Phase 5: User Story 3 - Regression protection for the companion-namespace pattern (Priority: P3)

**Goal**: Durable golden coverage with a negative control and a dedup guarantee, so the defect cannot silently return.

**Independent Test**: The negative control (strict-only variant) shows no `valueEquals` import; a symbol used at top level and in a variant appears exactly once; reverting either production edit fails a golden.

### Tests for User Story 3 (write first) ⚠️

- [X] T014 [US3] Added negative-control golden **C3** (strict-only variant → no `valueEquals`), strengthened per review with positive assertions (`class Loaded`, `this.count === other.count`). Plus a new `[Import]`-nested negative control guarding the over-approximation hole.
- [X] T015 [P] [US3] Added dedup golden **C4** (`NestedVariant_DeduplicatesRuntimeImportSharedWithTopLevel`): exactly one `from "metano-runtime"` line.

### Implementation for User Story 3

- [X] T016 [US3] Regression-proof: demonstrated by the TDD sequence (C1+C2 failed before the edits). Additionally added a **Dart-target** regression test (`DartBackendTests.NestedRecordInNonRecordParent_DoesNotFoldChildHashCodeIntoParentFile`) that locks in finding #1 — fails if the reverted Strategy A is ever reintroduced.

**Checkpoint**: All contract cases C1–C6 covered; pattern is regression-protected.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T017 Dual-agent review (per CLAUDE.md): ran `compiler-man` + `bob` in parallel; both initially **requested changes** (2 Majors each), which drove the Strategy A→B pivot. Re-reviewed the revised diff — **both APPROVE**. All findings fixed (doc-hygiene nits + the two negative-control tests added).
- [~] T018 [P] .NET regression sweep done (full TUnit suite green). Bun sample sweep **deferred** — the committed sample TS files are unchanged (not regenerated), so they prove nothing about this change; the .NET golden suite is the authoritative coverage. Pair with T019 if running samples.
- [ ] T019 [P] **Deferred (recommended follow-up, not done):** regenerate samples whose C# uses nested record variants (e.g. `SampleSolidUi`) and commit generated `targets/**` changes as a separate `chore`. Not required to ship the fix; kept out to avoid mutating committed output without sign-off.
- [X] T020 Quickstart before/after validated against the real Vigiata project (scratch output): pre-fix two `TS2304`; post-fix both imports present.
- [ ] T021 [P] Optional documentation debt (not done): consider tightening baseline FR-028 wording to "regardless of nesting depth". Tracked in research R4; not required to ship.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps; start immediately.
- **Foundational (P2)** → after Setup; blocks all story phases (shared test class).
- **US1 (P3 phase)** → after Foundational. The MVP.
- **US2 / US3** → after Foundational; both build on the US1 `ImportCollector` fix but add only tests, so they can proceed once US1's T006/T007 land.
- **Polish (P6)** → after the desired stories complete.

### Within US1 (critical path)

T005 (failing test) → {T006 ∥ T007} → T008 (filter alignment) → T009 (golden green + format) → T010 (Vigiata type-check).

### Parallel opportunities

- T002 ∥ T003 (Setup).
- **T006 ∥ T007** — different files, different layers (TS target vs core), no inter-dependency. This is the main parallel win.
- T011 ∥ T012 (US2 tests); T014 ∥ T015 (US3 tests) — same file, so coordinate edits or run sequentially if editing concurrently.
- T018 ∥ T019 ∥ T021 (Polish).

---

## Parallel Example: User Story 1 production edits

```bash
# After the failing golden (T005) is in place, land both fixes in parallel:
Task: "Fix ns.Members recursion in src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs"
Task: "Populate NestedTypes in src/Metano.Compiler/Extraction/IrClassExtractor.cs"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Setup (T001–T003) → Foundational (T004).
2. T005 failing golden → T006 ∥ T007 → T008 → T009 → T010.
3. **STOP and VALIDATE**: golden green + Vigiata `tsc` clean = reported bug fixed. Shippable.

### Incremental delivery

US1 (MVP) → US2 (generality goldens) → US3 (negative control + dedup + regression-proof) → Polish (dual-agent review + full sweep + sample regen).

---

## Notes

- The fix is two small production edits; the bulk of the work is test coverage for a pattern that had **none**.
- `IrEqualityClassifier` is untouched — it stays the single source of truth shared by the emitter and the scanner, which is what keeps "import lands iff `valueEquals` call is emitted" true.
- Authoring on `main` per maintainer choice; the real implementation MUST still pass dual-agent review (T017) before any commit, per CLAUDE.md.
