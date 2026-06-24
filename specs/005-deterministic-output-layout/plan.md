# Implementation Plan: Deterministic and Self-Cleaning TypeScript Output Layout

**Branch**: `005-deterministic-output-layout` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-deterministic-output-layout/spec.md`

## Summary

Two confirmed defects make generated TypeScript paths unstable and stale-prone: (1) the output-tree root is the *longest common prefix* of the currently-present types, so adding/removing a sibling type silently relocates other types' files; and (2) the build never deletes files a prior run produced but the current run does not, so orphans accumulate on the default (incremental) path. The reported symptom — `import { UserProfile } from "../contracts"` resolving only through a stale orphan barrel — is a direct consequence.

The plan resolves both by adopting the decisions captured in the spec:

- **D1 — Full-namespace layout (no stripping).** A type's path is its full kebab-cased C# namespace under the package/output root (`Vigiata.Contracts.Profiles.UserProfileDto` → `vigiata/contracts/profiles/user-profile.ts`). The path becomes a pure function of the type's own namespace; the common-prefix computation is removed. Local and cross-package mapping converge on the same rule (package = assembly = namespace container).
- **D2 — Barrel policy.** Per-namespace leaf barrels stay always-on (the external import unit); the root namespace-aggregation barrel stays opt-in (it defeats tree-shaking).
- **D3 — Internal direct-file imports.** References between generated types resolve to the defining type's file, never through a barrel — decoupling internal correctness from barrels and removing ESM-cycle risk.
- **Self-cleaning builds.** On a real (non-cache-hit) emit, reconcile the previous run's recorded output set against the current one and delete the difference (Metano-owned files only), plus now-empty generated directories. This lives in the target-agnostic host so it applies to both the CLI and the MSBuild integration.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (preview features); generated output is TypeScript consumed via Bun.

**Primary Dependencies**: Roslyn (Microsoft.CodeAnalysis 5.3), ConsoleAppFramework (CLI), MSBuild integration (`Metano.Build` package), TUnit (.NET tests), bun:test (TS tests), CSharpier (format).

**Storage**: Filesystem — generated `.ts`/`.tsx` + barrels; incremental state in `<outputDir>/.metano-cache.json` (`OutputHashes` = the run's generated-file set) and `<outputDir>/.metano-cache-groups-typescript.json`; MSBuild `<outputDir>/.metano-stamp`.

**Testing**: TUnit via `dotnet run --project tests/Metano.Tests/`; bun:test in `targets/js/*`; golden `.ts` fixtures under `tests/Metano.Tests/Expected/`.

**Target Platform**: Developer toolchain (CLI + MSBuild) on macOS/Linux/Windows.

**Project Type**: Compiler/transpiler — target-agnostic core (`Metano.Compiler`) behind the `ITranspilerTarget` port, with the TypeScript adapter (`Metano.Compiler.TypeScript`) owning emission.

**Performance Goals**: Incremental builds stay fast; orphan reconciliation is O(|previous output set|) using the persisted manifest — no full output-tree scan; identical-output runs delete nothing.

**Constraints**: Core MUST NOT depend on the TS target (Principle IV). Output MUST be deterministic and idempotent. `TreatWarningsAsErrors`; all C# passes `dotnet csharpier .`. Pruning MUST only delete Metano-generated files and MUST be failure-safe.

**Scale/Scope**: ~13 sample packages under `targets/js/`, 36 golden fixtures, 337+ TUnit tests. Full-namespace churn is bounded to sources/samples that *declare* a namespace (global-namespace unit tests are unaffected: `ns=""` → root, unchanged).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| I. Clean Code | PASS — net simplification: the common-prefix path machinery (`FindCommonPrefix` usage for layout, `StripRootNamespace`) is removed/neutralized rather than extended. Edits are localized and named. |
| II. Expressive code | PASS — full-namespace paths are more intention-revealing (on-disk path == .NET fully-qualified name). |
| III. Screaming organization | PASS — changes land in capability folders: `Transformation/PathNaming`, core `NamespaceUtilities`/`TranspilerHost`, TS `BarrelFileGenerator`, `PackageJsonWriter`. No new layer/utility bucket. |
| IV. Ports & Adapters | PASS — the root-namespace *policy* and *orphan reconciliation* are target-agnostic and live in the core; kebab segmenting, barrels, and `package.json` shaping stay in the TS adapter. Core gains no TS dependency. |
| V. Developer Experience | PASS — self-cleaning builds remove a surprising failure mode; pruned files are reported on the console; new TUnit + bun:test coverage; one documented command per toolchain unchanged. |
| VI. Pragmatism | PASS — no speculative abstraction. Removing the prefix computation and adding pruning both answer present, demonstrated needs. |

**Required by gates**: (a) an **ADR** under `docs/adr/` for the layout-policy change (full namespace, superseding the common-prefix behavior and relating to ADR-0006 namespace-first imports and the import-alias ADR) and the orphan-pruning addition; (b) **dual-agent review** (`compiler-man` + `bob`) before commit; (c) **spec-as-source-of-truth** satisfied by this spec (005), consistent with features 002–004 — reconcile the baseline capability matrix in `specs/001-project-baseline-evolution/` as documentation debt; (d) **build & tests green** including regenerated goldens/samples.

**No violations** → Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/005-deterministic-output-layout/
├── plan.md              # This file
├── spec.md              # Feature spec (D1–D3, FR-001..FR-017)
├── research.md          # Phase 0 — resolved design/mechanics decisions
├── data-model.md        # Phase 1 — entities (path mapping, manifest, barrels, imports)
├── quickstart.md        # Phase 1 — how to validate the fix end-to-end
├── contracts/
│   ├── path-mapping.md      # FQN → on-disk path mapping contract
│   ├── import-contract.md   # how generated types are importable (+ package.json exports)
│   └── pruning-contract.md  # orphan reconciliation behavior + safety guarantees
└── checklists/requirements.md
```

### Source Code (repository root)

```text
src/
├── Metano.Compiler/                      # target-agnostic core
│   ├── NamespaceUtilities.cs                # FindCommonPrefix — no longer drives layout
│   ├── CSharpSourceFrontend.cs              # ComputeLocalRootNamespace → full-namespace policy
│   ├── IR/IrCompilation.cs                  # LocalRootNamespace semantics (now empty/no-strip)
│   ├── TranspilerHost.cs                    # emit + cache; ADD orphan reconciliation
│   ├── CacheKeyBuilder.cs / TranspilationCache.cs  # OutputHashes = prior generated set (manifest)
│   └── Diagnostics/                         # (optional) report pruned files
├── Metano.Compiler.TypeScript/
│   ├── Transformation/PathNaming.cs         # full-namespace path; direct-file internal imports
│   ├── Transformation/TypeTransformer.cs    # PathNaming construction (root → "")
│   ├── Transformation/BarrelFileGenerator.cs# leaf barrels under nested full-ns dirs; root opt-in
│   ├── PackageJsonWriter.cs                 # exports/imports reflect full-namespace barrels
│   └── TypeScriptTarget.cs                  # ConfigurationFingerprint += layout-version token
└── Metano.Build/build/Metano.Build.targets  # no pruning change (host-driven); MetanoClean retained

tests/
└── Metano.Tests/
    ├── *.cs                                 # update namespace-declaring assertions; add new tests
    └── Expected/                            # regenerate goldens whose source declares a namespace

targets/js/                                  # regenerate namespace-declaring samples + fix consumers/tsconfig/package.json
docs/adr/0025-full-namespace-output-layout.md (+ pruning)   # new ADR
```

**Structure Decision**: Single multi-project .NET solution with a target-agnostic core and language adapters (existing). The layout *policy* and *orphan reconciliation* are added to the core (`Metano.Compiler`) so every target inherits them; TypeScript-specific path/barrel/package shaping stays in `Metano.Compiler.TypeScript`. No new projects or layers are introduced.

## Phases

- **Phase 0 — Research** (`research.md`): lock the mechanics — root-namespace policy, cross-package consistency, internal import style, barrel policy, pruning manifest + safety, cache invalidation token, package.json shaping, MSBuild applicability.
- **Phase 1 — Design & Contracts** (`data-model.md`, `contracts/`, `quickstart.md`): entities and the three behavior contracts (path mapping, import contract, pruning), plus an end-to-end validation walkthrough; update the agent context pointer in `CLAUDE.md`.
- **Phase 2 — Tasks** (`/speckit-tasks`, NOT created here): dependency-ordered tasks per user story (US1 layout, US2 pruning, US3 import contract) including golden/sample regeneration and the ADR.

## Complexity Tracking

> No constitution violations — no entries.
