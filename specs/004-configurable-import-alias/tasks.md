---
description: "Task list for: Configurable Isolated Subpath-Import Alias for Generated Packages"
---

# Tasks: Configurable Isolated Subpath-Import Alias for Generated Packages

**Input**: Design documents from `/specs/004-configurable-import-alias/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED. The Metano constitution (Principle V) mandates golden/
expected-output tests for transpiler behavior, and the plan enumerates them.

**Organization**: Tasks are grouped by user story. The implementation is a single
cohesive change to the TypeScript target adapter; the Foundational phase wires the
config value through without changing behavior, then each story layers behavior +
tests on top.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (Setup, Foundational, Polish carry no story label)

## Path Conventions

Single-project compiler with target adapters. Production code under
`src/Metano.Compiler.TypeScript/` and `src/Metano.Build/`; tests under
`tests/Metano.Tests/`. The core `src/Metano.Compiler/` is intentionally NOT touched.

---

## Phase 1: Setup

**Purpose**: Establish a clean, green baseline to compare backward compatibility against.

- [x] T001 Capture a green baseline: run `dotnet build` and `dotnet run --project tests/Metano.Tests/` and record that all tests pass before any change (used as the no-regression reference for US2).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Thread the optional `importAlias` value end-to-end through the TypeScript
adapter WITHOUT changing emitted output yet (default stays `#`). Everything below
this line builds on these seams.

**⚠️ CRITICAL**: No user story behavior work begins until this phase is complete.

- [x] T002 [P] Add `ImportAlias` init property to `TypeScriptTarget`, pass it into `new TypeTransformer { ... }`, and append it to `BuildConfigurationFingerprint` so a changed alias invalidates the cache (FR-010) in `src/Metano.Compiler.TypeScript/TypeScriptTarget.cs`
- [x] T003 [P] Add `ImportAlias` init property to `TypeTransformer` in `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`
- [x] T004 [P] Add a defaulted `string? importAlias = null` parameter to `PackageJsonWriter.UpdateOrCreate` and `BuildImports`, threaded through unused for now (keeps existing test call sites compiling) in `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs`
- [x] T005 [P] Add the `MetanoImportAlias` MSBuild property mapped to a conditional `--import-alias "$(MetanoImportAlias)"` arg, plus its doc-block entry, in `src/Metano.Build/build/Metano.Build.targets`
- [x] T006 Add the `--import-alias <name>` parameter (with XML doc) to `Commands.Transpile`, set `ImportAlias` on the `TypeScriptTarget`, and pass `importAlias:` into the `PackageJsonWriter.UpdateOrCreate(...)` call in `src/Metano.Compiler.TypeScript/Commands.cs` (depends on T002 and T004)

**Checkpoint**: Solution builds; alias value flows to the target, the cache key, and
the writer; emitted output is still byte-identical to today.

---

## Phase 3: User Story 1 - Emit into a subfolder of an existing project (Priority: P1) 🎯 MVP

**Goal**: With an alias configured, internal imports use `#<alias>/...` and the
`package.json` gets only the alias-scoped keys, leaving the host project's `#` untouched.

**Independent Test**: Transpile a 2-namespace sample with `--import-alias contracts`
into a nested output dir under a host package that already defines `#/*`; confirm
generated imports are `#contracts/...`, the host `#`/`#/*` is unchanged, and the
output resolves.

### Tests for User Story 1 ⚠️ (write first, ensure they fail)

- [x] T007 [P] [US1] `PathNaming` unit tests for the aliased specifier: alias `contracts` → `#contracts/<ns>`; root-namespace → bare `#contracts`; same-namespace → still `./<kebab-type>`; normalization (`#contracts` ≡ `contracts`, blank → `#`) in `tests/Metano.Tests/ImportAliasTests.cs`
- [x] T008 [P] [US1] End-to-end golden test: a 2-namespace inline sample transpiled with the alias emits internal imports as `from "#contracts/..."` and zero `from "#/..."` in `tests/Metano.Tests/ImportAliasTests.cs`
- [x] T009 [P] [US1] `PackageJsonWriter` test: `importAlias: "contracts"` produces `imports` with `#contracts` + `#contracts/*` (and NOT `#`/`#/*`), with targets scoped to the output subfolder, in `tests/Metano.Tests/EmitPackageTests.cs`
- [x] T010 [P] [US1] Isolation-merge test: a `package.json` with pre-existing user `#`/`#/*` plus a configured alias keeps the user keys AND adds `#contracts`/`#contracts/*` (no collision, no clobber) in `tests/Metano.Tests/EmitPackageTests.cs`
- [x] T011 [P] [US1] Cyclic-reference test under a custom alias: a two-file import cycle with `--import-alias contracts` still reports MS0005 in `tests/Metano.Tests/CyclicReferenceTests.cs`

### Implementation for User Story 1

- [x] T012 [US1] Parameterize `PathNaming` (ctor `(string rootNamespace, string? importAlias = null)`, derive `AliasPrefix` with the normalization helper) and use `AliasPrefix` in the two `#` branches of `ComputeRelativeImportPath` (keep the `./` same-namespace fallback); construct it as `new PathNaming(ir.LocalRootNamespace, ImportAlias)` in `TypeTransformer` — `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs` (+ the construction line in `TypeTransformer.cs`)
- [x] T013 [US1] In `PackageJsonWriter.BuildImports`, derive `starKey`/`rootKey` from the alias and emit only those keys (when an alias is set, `#`/`#/*` are never written) in `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs`
- [x] T014 [US1] Teach `CyclicReferenceDetector` the alias: add an `importAlias` param to `DetectAndReport`, thread `aliasPrefix` into `TryNormalizeLocalImport` and `ToDisplayImportPath`, and pass `ImportAlias` from the `DetectAndReport` call site in `TypeTransformer` — `src/Metano.Compiler.TypeScript/Transformation/CyclicReferenceDetector.cs` (+ the call site in `TypeTransformer.cs`)

**Checkpoint**: US1 fully functional — alias behavior verified by T007–T011.

---

## Phase 4: User Story 2 - Existing projects are unaffected (Priority: P1)

**Goal**: With no alias configured, generated TypeScript and `package.json` are
byte-identical to the pre-feature output.

**Independent Test**: Transpile any existing sample with no alias and diff against the
baseline (T001) — identical.

### Tests for User Story 2 ⚠️

- [x] T015 [P] [US2] `PathNaming` no-regression unit test: with no alias, specifiers are `#/<ns>` and bare `#` exactly as before, in `tests/Metano.Tests/ImportAliasTests.cs`
- [x] T016 [P] [US2] End-to-end no-regression golden test: the same 2-namespace sample with NO alias emits `from "#/..."` (default), in `tests/Metano.Tests/ImportAliasTests.cs`

### Implementation / Verification for User Story 2

- [x] T017 [US2] Run the full existing .NET suite (`dotnet run --project tests/Metano.Tests/`) and regenerate the in-repo samples (e.g. `targets/js/sample-todo-service`); confirm zero golden-fixture diffs and byte-identical sample regeneration vs the T001 baseline (SC-002)

**Checkpoint**: US1 and US2 both hold — opt-in works, default is untouched.

---

## Phase 5: User Story 3 - Correct paths at any output depth (Priority: P2)

**Goal**: `package.json` import/export path values are well-formed (no doubled
separators) at any output-directory depth.

**Independent Test**: Transpile into a deeply nested output dir and inspect the
generated `imports`/`exports` for `//`.

### Tests for User Story 3 ⚠️

- [x] T018 [P] [US3] Double-slash regression test: a nested output (non-empty `outputPrefix`) yields no `//` in any `imports` or `exports` path value, in `tests/Metano.Tests/EmitPackageTests.cs`

### Implementation for User Story 3

- [x] T019 [US3] Trailing-trim `outputPrefix` before building `distBase` in `PackageJsonWriter.BuildImports`, and apply the identical fix in `BuildExports`, in `src/Metano.Compiler.TypeScript/PackageJsonWriter.cs` (same file as T013 — sequence after it)

**Checkpoint**: All three stories independently verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T020 [P] Document `--import-alias` / `MetanoImportAlias` (purpose, normalization, multi-project known limitation) in the CLI flag list and MSBuild property/conventions tables of `CLAUDE.md`
- [ ] T021 [P] Record the new knob in the baseline flag/attribute catalog under `specs/001-project-baseline-evolution/baseline/` — DEFERRED to ship-time (the feature is recorded in its own spec 004; baseline backfill happens when 004 merges, per the project's spec-reconciliation convention)
- [x] T022 Run `dotnet csharpier .` and `bunx biome check` (in the relevant `targets/js/*`); fix any formatting/lint findings
- [x] T023 Dual-agent review per constitution: run `compiler-man` (semantics/IR/pipeline) and `bob` (Clean Code) in parallel on the diff; fix findings before commit
- [x] T024 Run the `quickstart.md` walkthrough end-to-end: transpile a sample into a subfolder with `--import-alias`, then `bun run build` the target to confirm the alias resolves

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2)**: after Setup — BLOCKS all stories. T002–T005 are parallel; T006 depends on T002 + T004.
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 is the MVP; US2 and US3 can follow.
- **Polish (Phase 6)**: after the desired stories are complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. Delivers the core alias behavior (MVP).
- **US2 (P1)**: after Foundational. Independently testable; its no-regression tests pass as long as the default path is preserved (which T012/T013 must maintain).
- **US3 (P2)**: after Foundational. T019 shares `PackageJsonWriter.cs` with T013 — sequence T019 after T013 to avoid edit conflicts in `BuildImports`.

### Shared-file sequencing (not parallel)

- `PackageJsonWriter.cs`: T004 → T013 → T019 (same file, ordered).
- `TypeTransformer.cs`: T003 → T012 (construction) → T014 (detector call) (same file, ordered).

### Parallel Opportunities

- Foundational: T002, T003, T004, T005 in parallel (distinct files); then T006.
- US1 tests: T007, T008, T009, T010, T011 in parallel (T007/T008 share `ImportAliasTests.cs`, so co-author or sequence those two).
- US2 tests: T015, T016 in parallel with each other and with US1 tests.
- Polish: T020, T021 in parallel.

---

## Parallel Example: Foundational phase

```bash
# Distinct files, no interdependencies — run together:
Task: "Add ImportAlias prop + cache fingerprint in TypeScriptTarget.cs"        # T002
Task: "Add ImportAlias prop in TypeTransformer.cs"                              # T003
Task: "Add importAlias param to PackageJsonWriter.UpdateOrCreate/BuildImports" # T004
Task: "Add MetanoImportAlias MSBuild property in Metano.Build.targets"         # T005
# Then wire them together:
Task: "Add --import-alias flag in Commands.cs"                                 # T006
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → green baseline.
2. Phase 2 Foundational → value plumbing (no behavior change).
3. Phase 3 US1 → alias behavior + tests.
4. **STOP and VALIDATE**: alias works end-to-end into a nested subfolder; host `#` untouched.

### Incremental Delivery

- US1 (MVP) → US2 (prove default unchanged) → US3 (double-slash fix) → Polish.
- Each story is an independently testable increment; none breaks the previous.

---

## Notes

- The core `src/Metano.Compiler/` and `TranspileOptions` are NOT modified (Constitution Principle IV).
- Backward compatibility (US2) is a release gate — keep the default path byte-identical.
- Commit after each logical group; run the dual-agent review (T023) before committing the feature.
- Avoid same-file parallelism on `PackageJsonWriter.cs` and `TypeTransformer.cs` (see sequencing).
