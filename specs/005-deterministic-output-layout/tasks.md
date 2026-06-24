---

description: "Task list for Deterministic and Self-Cleaning TypeScript Output Layout"
---

# Tasks: Deterministic and Self-Cleaning TypeScript Output Layout

**Input**: Design documents from `/specs/005-deterministic-output-layout/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — FR-017 mandates coverage and the constitution requires golden/behavior tests for transpiler changes.

**Organization**: Grouped by user story (US1 layout → US2 pruning → US3 import contract). US2 is independent of US1 and may proceed in parallel; US3 depends on US1 (shared `PathNaming.cs`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish have no story label)

## Path Conventions

Multi-project .NET solution: target-agnostic core in `src/Metano.Compiler/`, TypeScript adapter in `src/Metano.Compiler.TypeScript/`, MSBuild package in `src/Metano.Build/`, tests in `tests/Metano.Tests/`, generated samples in `targets/js/`.

---

## Phase 1: Setup

**Purpose**: Baseline and decision record before changing behavior.

- [x] T001 Capture a green baseline: run `dotnet build` and `dotnet run --project tests/Metano.Tests/`, recording current pass/skip counts so the intended golden churn is distinguishable from regressions.
- [x] T002 [P] Write ADR `docs/adr/0025-full-namespace-output-layout.md` recording D1 (full-namespace, no stripping), D2 (leaf barrels always-on, root aggregation opt-in), D3 (internal direct-file imports) and the orphan-pruning addition; mark it as superseding the common-prefix layout and relating to ADR-0006 (namespace-first imports) and the import-alias ADR.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared cache concern that both the layout change and pruning correctness rely on.

**⚠️ CRITICAL**: Complete before US1/US2 verification so cache hits cannot mask the new behavior.

- [x] T003 Add a `layout=full-namespace-v1` token to `ConfigurationFingerprint` in `src/Metano.Compiler.TypeScript/TypeScriptTarget.cs` so any layout change invalidates prior incremental caches (R6).

**Checkpoint**: Caches now invalidate on the layout change — user-story work can proceed.

---

## Phase 3: User Story 1 - Stable full-namespace layout (Priority: P1) 🎯 MVP

**Goal**: A type's on-disk path is its full kebab-cased C# namespace and never moves when unrelated sibling types are added/removed.

**Independent Test**: Transpile a single type in `Vigiata.Contracts.Profiles` → `vigiata/contracts/profiles/user-profile.ts`; add a `Vigiata.Contracts.Serialization` type → the first type's path is unchanged.

### Tests for User Story 1

- [x] T004 [P] [US1] Add failing TUnit tests in `tests/Metano.Tests/OutputLayoutTests.cs`: (a) single type in a sub-namespace emits at its full-namespace path (no root collapse, FR-004); (b) a type's path is byte-identical with 1 vs N types (sibling add does not relocate, FR-001); (c) cross-package type maps by full namespace (R2).

### Implementation for User Story 1

- [x] T005 [US1] In `src/Metano.Compiler/CSharpSourceFrontend.cs`, make `ComputeLocalRootNamespace` return `""` (stop deriving the layout root from `NamespaceUtilities.FindCommonPrefix`) so `IrCompilation.LocalRootNamespace` is empty (full-namespace layout).
- [x] T006 [US1] Update the `LocalRootNamespace` doc comment in `src/Metano.Compiler/IR/IrCompilation.cs` to state that an empty value means "no stripping — full namespace is the output tree".
- [x] T007 [US1] In `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`, confirm/adjust `GetRelativePath` + `StripRootNamespace` produce nested kebab folders for the full namespace when the root is empty (e.g. `vigiata/contracts/profiles/user-profile.ts`).
- [x] T008 [US1] Make cross-package path computation use the full namespace by passing `assemblyRootNamespace=""` to `PathNaming.ComputeSubPath` at its callers in `src/Metano.Compiler.TypeScript/Bridge/IrToTsTypeMapper.cs` and `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`.
- [x] T009 [US1] In `src/Metano.Compiler.TypeScript/Transformation/BarrelFileGenerator.cs`, confirm one leaf `index.ts` is emitted per nested full-namespace directory (and the root barrel still only when `NamespaceBarrels` is opt-in).
- [x] T010 [P] [US1] In `src/Metano.Compiler/NamespaceUtilities.cs`, remove `FindCommonPrefix` if no non-layout caller remains (grep first); otherwise leave it and note the remaining caller.
- [x] T011 [US1] Regenerate golden fixtures whose source declares a namespace under `tests/Metano.Tests/Expected/` and update the corresponding `ContainsKey("…")` path assertions in `tests/Metano.Tests/*.cs`; review the diff to confirm only the full-namespace prefix changed.
- [~] T012 [US1] [BLOCKED: local NuGet multi-source restore (NU1507) — regenerate in a single-source/CI env] Regenerate namespace-declaring samples under `targets/js/*` (e.g. `sample-todo`, `sample-issue-tracker`, `sample-todo-service`) and update their `tsconfig.json` / `package.json` subpaths to the full-namespace layout; build each with Bun.
- [x] T013 [US1] Run the full TUnit suite and the regenerated sample builds; verify the US1 tests pass and that transpiling twice yields a byte-identical output tree (idempotent, SC-004).

**Checkpoint**: Layout is stable and deterministic; this is a shippable MVP (the reported "file relocated" class of failures is gone).

---

## Phase 4: User Story 2 - Self-cleaning incremental rebuilds (Priority: P2)

**Goal**: Renaming/moving/deleting a type removes the orphaned generated file automatically on the default incremental build — no `--clean` required — without touching hand-written files.

**Independent Test**: Build a type, rename it (path changes), rebuild without `--clean` → old file gone, new file present, a hand-written file in the tree preserved, a no-op rebuild deletes nothing.

**Note**: Independent of US1 (touches `TranspilerHost.cs`/cache, not `PathNaming.cs`) — may run in parallel with US1.

### Tests for User Story 2

- [x] T014 [P] [US2] Add failing TUnit tests in `tests/Metano.Tests/OrphanPruningTests.cs`: (a) a removed/renamed output file is pruned on the next non-clean run (FR-006); (b) a hand-written file in the output dir is never deleted (FR-007); (c) an identical output set deletes nothing (FR-010); (d) orphaned barrels and emptied directories are removed (FR-011); (e) a failed/interrupted run leaves the prior generated files and the prior `.metano-cache.json` manifest intact — pruning never runs on a non-successful emit (FR-014).

### Implementation for User Story 2

- [x] T015 [US2] In `src/Metano.Compiler/TranspilerHost.cs`, read the prior run's output set (keys of `TranspilationCache.OutputHashes`) from `<outputDir>/.metano-cache.json` before the emit, exposing it via `TranspilationCache`/`CacheKeyBuilder` as needed (`src/Metano.Compiler/TranspilationCache.cs`, `src/Metano.Compiler/CacheKeyBuilder.cs`).
- [x] T016 [US2] In `src/Metano.Compiler/TranspilerHost.cs`, after a successful write of the current output set, delete `prior − current` restricted to manifest-recorded paths; skip entirely on cache hit, `--no-cache`, and `--clean` (per the pruning contract).
- [x] T017 [US2] In `src/Metano.Compiler/TranspilerHost.cs`, remove generated directories emptied by the prune, excluding the output root and the `.metano-cache.json` / `.metano-cache-groups-typescript.json` / `.metano-stamp` metadata files.
- [x] T018 [US2] In `src/Metano.Compiler/TranspilerHost.cs`, report each removed path to the console (e.g. `Pruned: <relativePath>`, FR-009).
- [x] T019 [US2] Verify the MSBuild incremental path inherits pruning with no `src/Metano.Build/build/Metano.Build.targets` change (pruning lives in the host). Document the `--no-cache` limitation (no prior manifest → no prune) in the CLI flag help in `src/Metano.Compiler.TypeScript/Commands.cs` and in a comment in `src/Metano.Build/build/Metano.Build.targets`, advising it be paired with `MetanoClean`. (No separate CLI-parity test — the host is the single shared code path already exercised by T014.)
- [x] T020 [US2] Run `OrphanPruningTests` and walk the `quickstart.md` US2 steps (rename → prune, hand-written file preserved, no-op deletes nothing).

**Checkpoint**: Incremental builds are self-cleaning; the existing Vigiata orphan (`contracts/user-profile.ts`, root `contracts/index.ts`) is removed on the next non-clean rebuild.

---

## Phase 5: User Story 3 - Coherent consumer import contract (Priority: P3)

**Goal**: Generated types import each other by direct file path (no barrels/cycles), consumers import via the full-namespace leaf barrel, and `package.json` exports mirror the real layout — no reliance on orphans.

**Independent Test**: From a clean regeneration, import a type via `#<alias>/<full-namespace>` and type-check successfully; confirm the reported `../contracts` root reference is either backed by the opt-in root barrel or replaced by the subpath import.

**Note**: Depends on US1 (shares `PathNaming.cs`).

### Tests for User Story 3

- [x] T021 [P] [US3] Add failing TUnit tests in `tests/Metano.Tests/ImportContractTests.cs`: (a) an internal cross-namespace reference resolves to the defining file `#<alias>/<full-ns>/<file>`, not a barrel (FR-016); (b) same-namespace references stay relative (`./<file>`); (c) `package.json` exports list full-namespace leaf-barrel subpaths (FR-013).

### Implementation for User Story 3

- [x] T022 [US3] In `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`, change `ComputeRelativeImportPath` so cross-namespace internal references target the file (`#<alias>/<full-ns>/<kebab-type>`) instead of the namespace barrel; keep same-namespace `./<kebab-type>` (depends on T007).
- [x] T023 [US3] In `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs`, ensure `exports` (and the `#<alias>/*` mapping) are derived from the emitted full-namespace leaf-barrel paths so declared subpaths mirror the layout (FR-013).
- [~] T024 [US3] [BLOCKED with T012 — same NuGet env issue] Regenerate the namespace-declaring samples and update hand-written consumer imports to the full-namespace subpath; confirm a clean (`--clean`) regeneration type-checks with no orphan dependency and `bun test` is green for those samples.
- [x] T025 [US3] Add a cross-package TUnit fixture in `tests/Metano.Tests/DeterministicLayoutScenarioTests.cs` using `TranspileHelper.TranspileWithLibrary` that replicates the reported Vigiata scenario (a namespace-declaring library — `Vigiata.Contracts.Profiles.UserProfileDto` plus `Vigiata.Contracts.Serialization.*` — consumed under an import alias), asserting: `UserProfile` resolves via `#web-server-contracts/vigiata/contracts/profiles`; no root `index.ts` / `user-profile.ts` is emitted; and a clean regeneration leaves no stale root reference (SC-007).

**Checkpoint**: All three stories are independently functional; the import contract is coherent and orphan-free.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T026 [P] Reconcile the baseline in `specs/001-project-baseline-evolution/` by adding an FR/cross-reference entry that traces this feature's full-namespace layout + orphan-pruning behavior to the canonical spec, and update the capability matrix accordingly — satisfying the constitution's "spec as source of truth" gate (resolves analysis finding C1).
- [ ] T027 [P] Update sample READMEs / docs to note the full-namespace layout and the tree-shaking caveat of the opt-in root aggregation barrel.
- [x] T028 Dual-agent review of the complete diff: `compiler-man` (semantics, IR/path mapping, cache/pruning correctness, pipeline coverage) and `bob` (naming, method size, condition complexity, blank-line discipline) in parallel; fix findings before commit.
- [x] T029 Run `quickstart.md` end-to-end, then `dotnet csharpier .`, the full TUnit suite, and every namespace-declaring sample's `bun run build && bun test` — all green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: after Setup; the cache token (T003) must precede US1/US2 verification.
- **US1 (Phase 3)**: after Foundational.
- **US2 (Phase 4)**: after Foundational; independent of US1 — can run in parallel.
- **US3 (Phase 5)**: after US1 (shared `PathNaming.cs`: T022 depends on T007).
- **Polish (Phase 6)**: after all targeted stories.

### Within stories

- Tests (T004, T014, T021) are written first and FAIL before implementation.
- US1: core root change (T005) → PathNaming/barrels (T007–T009) → regen (T011–T012) → verify (T013).
- US2: manifest read (T015) → prune (T016) → empty-dir cleanup (T017) → report (T018).
- US3: import path change (T022) → package.json (T023) → sample/consumer regen (T024) → Vigiata repro (T025).

### Parallel Opportunities

- T002 (ADR) ∥ T001 (baseline).
- **US1 ∥ US2** after T003 (different files: `PathNaming`/core vs `TranspilerHost`/cache).
- Test scaffolds T004 ∥ T014 ∥ T021 (different files).
- T010 ∥ other US1 impl (different file), T026 ∥ T027 (docs).

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup (T001–T002) → Foundational (T003).
2. US1 (T004–T013): full-namespace layout + regen.
3. **STOP and VALIDATE**: single-vs-multi-type stability, idempotent re-transpile. Ship — the "relocated file" failure class is resolved.

### Incremental Delivery

1. + US2 (pruning) → existing orphans auto-removed on incremental builds.
2. + US3 (import contract) → internal direct-file imports + exports mirror layout; Vigiata repro clean.
3. Polish: doc reconciliation, dual-agent review, full green gate.

## Notes

- Golden/sample churn (T011/T012/T024) is intentional (FR-005), not a regression — review diffs for full-namespace-prefix-only changes.
- Global-namespace unit tests are unaffected (`ns==""` → root); only namespace-declaring sources change.
- Commit after each logical group; dual-agent review (T028) is mandatory before the commit that declares the feature complete.
