# Feature Specification: Deterministic and Self-Cleaning TypeScript Output Layout

**Feature Branch**: `005-deterministic-output-layout`

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "Deterministic and self-cleaning TypeScript output layout — a generated file ends up referenced from a path/namespace where the symbol no longer exists, because the generated folder layout is non-deterministic and stale orphan files are never removed."

## Context & Problem Statement

Observed in a real consumer (`Vigiata.Contracts` transpiled into `frontend/vigiata-app/src/apis/web-server/contracts/`):

- A single transpilable type `UserProfileDto` in C# namespace `Vigiata.Contracts.Profiles` (renamed to `UserProfile`) was first emitted **flat at the output root** (`contracts/user-profile.ts` plus a root `contracts/index.ts` barrel).
- After a second type (`ContractsSerializerContext` in `Vigiata.Contracts.Serialization`) was added, the **same** `UserProfileDto` started being emitted **nested** at `contracts/profiles/user-profile.ts`.
- The original root files were left behind as orphans. A hand-written consumer doing `import { UserProfile } from "../contracts"` now resolves **only** because of the stale orphan barrel. A clean rebuild deletes the orphan and the import breaks: without the opt-in root-barrel feature, no root barrel is generated and the type lives under `profiles/`.

Two independent defects were confirmed by reproduction with the local compiler:

1. **Non-deterministic layout.** The "root namespace" that maps to the output-directory root is computed as the *longest common prefix* across **all** transpilable types currently present. With a single type it collapses to that type's entire namespace (every segment is stripped → flat at root); with two or more types in sibling namespaces the common prefix shrinks and previously-stripped segments reappear as subfolders. As a result, **adding or removing an unrelated type in a sibling namespace silently relocates other types' files on disk.**
   - Reproduced: 1 type (`Vigiata.Contracts.Profiles.UserProfileDto`) → `user-profile.ts` + `index.ts` at the root. 2 types (adding `Vigiata.Contracts.Serialization.*`) → `profiles/user-profile.ts` + `serialization/...`.
2. **No orphan pruning.** Each run only writes the current output set; it never reconciles against the previously generated set, so files that are no longer produced are never deleted. The incremental cache records the prior output set but uses it solely to validate cache hits. The default build path does not wipe the output directory, so orphans accumulate indefinitely. A full clean of the output directory is currently the only remediation.

The combination means a layout change (or rename/move/delete of a type) leaves dangling generated files that mask broken imports until the next clean build.

## Design Decisions (resolved)

- **D1 — Full-namespace, nested folders.** The output path is the type's **full** C# namespace, mapped one folder per kebab-cased segment, with no root/common-prefix stripping. The package/output root is purely the assembly-equivalent container. Example: `Vigiata.Contracts.Profiles.UserProfileDto` → `vigiata/contracts/profiles/user-profile.ts`, imported as `<package-or-alias>/vigiata/contracts/profiles`. This makes the path a pure function of the type's own namespace, eliminating the unstable common-prefix computation and the "which root" question entirely. (Dotted-dir-per-namespace was considered and rejected in favor of the more conventional nested layout.)
- **D2 — Barrels.** A per-namespace leaf barrel is always generated (the namespace's external export surface / import unit). The root namespace-aggregation barrel (`export namespace` tree at the package root) stays **opt-in** because it defeats tree-shaking; it is offered only as an ergonomic single-entrypoint convenience.
- **D3 — Internal references use direct file imports.** References between generated types resolve to the defining type's file directly, never through a barrel, decoupling internal correctness from barrel generation and avoiding ESM import cycles.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stable output paths regardless of the type set (Priority: P1)

As an author of a transpiled C# project, when I add, remove, or move a type in one namespace, the on-disk output path of every **other** type must stay the same, so that hand-written and generated imports never break because an unrelated edit relocated a file.

**Why this priority**: This is the direct cause of the reported defect. Without path stability, every incremental edit risks silently relocating files and invalidating imports across the consuming codebase. Fixing it removes the entire class of "the file moved" failures.

**Independent Test**: Transpile a project containing only `Vigiata.Contracts.Profiles.UserProfileDto`; record its output path. Add a second unrelated type in `Vigiata.Contracts.Serialization`; transpile again. The `UserProfileDto` output path is byte-for-byte identical across both runs (it does not migrate between the output root and a `profiles/` subfolder).

**Acceptance Scenarios**:

1. **Given** a project with a single type in namespace `Root.Sub.Leaf`, **When** it is transpiled, **Then** the type is emitted at `root/sub/leaf/<type>.ts` (its full kebab-cased namespace), never collapsed to the output root just because it is the only type.
2. **Given** that same project, **When** a second type is added in a sibling namespace, **Then** the first type's output path is unchanged.
3. **Given** a project where all types already share a common namespace, **When** transpiled before and after the feature, **Then** the resulting layout for those types is unchanged from today's behavior (no regression for already-stable projects).

### User Story 2 - Incremental rebuilds clean up after themselves (Priority: P2)

As an author relying on the default (incremental) build, when a type is renamed, moved to another namespace, or deleted — or when configuration changes such that a previously generated file (including a barrel) is no longer produced — the now-orphaned generated file must be removed automatically, without my having to run a full clean.

**Why this priority**: Even after layout determinism (US1) lands, existing orphans and future renames/moves/deletes will keep producing stale files unless the build prunes them. Pruning is what makes the fix durable on the normal incremental build path, not just on a manual clean.

**Independent Test**: Transpile a project; confirm the output set. Rename a type (changing its output file name); transpile again **without** a clean. The old file is gone, the new file is present, and no hand-written or unrelated file in the output tree was touched.

**Acceptance Scenarios**:

1. **Given** a prior run produced file `A.ts`, **When** a subsequent incremental run no longer produces `A.ts` (type moved/renamed/deleted), **Then** `A.ts` is deleted automatically and the run reports it as removed.
2. **Given** an output directory that also contains hand-written files Metano never generated, **When** an incremental run prunes orphans, **Then** only files Metano itself previously generated are deleted; hand-written files are never removed.
3. **Given** the default build integration (not a manual full-clean invocation), **When** a type's output path changes, **Then** the orphan from the previous path is pruned on that same build.
4. **Given** a run that produces an output set identical to the previous run, **When** it completes, **Then** no file is deleted and no spurious churn occurs.

### User Story 3 - A coherent, non-accidental consumer import contract (Priority: P3)

As a consumer of generated TypeScript emitted into a subfolder under an import alias, I must be able to import a generated type through a documented, stable path that exists in clean generation — never relying on an accidental orphan file.

**Why this priority**: The reported symptom is a consumer importing `UserProfile` from the output root (`../contracts`) where, in clean generation, the symbol does not exist. The import contract (which paths exist, and how the alias / `package.json` exports map to the now-stable layout) must be explicit so consumers can depend on it.

**Independent Test**: From a clean generation (no orphans), follow the documented import path for a generated type and confirm it resolves both at type-check time and via the package's declared subpath entries; confirm the previously-broken root-level reference is either valid by design or clearly not part of the contract.

**Acceptance Scenarios**:

1. **Given** a clean generation of a type in a sub-namespace, **When** a consumer imports it via `<alias>/<full-namespace>` (the namespace leaf barrel) or the direct type-file path, **Then** the import resolves without depending on any orphan file.
2. **Given** the reported case (`import { UserProfile } from "../contracts"`), **When** generation is clean, **Then** either that path is part of the documented contract and resolves, or it is explicitly not part of the contract and the documented path is used instead — in no case does resolution depend on a stale orphan.
3. **Given** the package's subpath entries (import alias and `package.json` exports), **When** the layout is generated, **Then** those entries reflect the actual stable layout of the emitted barrels.

### Edge Cases

- A type whose namespace has no segments beyond the root identity (i.e., it legitimately belongs at the output root) — it must still emit deterministically at the root.
- Types in namespaces that do **not** descend from the project's root identity (e.g., a type in an unrelated namespace) — the layout must be deterministic and documented (kept verbatim as subfolders, never silently collapsed).
- A project with exactly one transpilable type — must not collapse that type to the output root solely because it is the only one.
- Orphan pruning when the output directory does not exist yet, is empty, or contains only the prior cache metadata.
- An interrupted/failed run — pruning must not delete files for an output set that was never fully written (no partial-state corruption).
- A user-defined type named such that it would collide with a generated barrel file name (the existing collision behavior must be preserved).
- Cross-package output (types discovered from a referenced assembly) — the stable-root rule must apply per originating assembly and not regress cross-package import resolution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The output path of a generated type MUST be a pure function of that type's own fully-qualified C# namespace, and MUST NOT depend on which other types are present in the same run, on any computed common prefix, or on any stripped "root".
- **FR-002**: The system MUST map types using the FULL C# namespace with NO root/common-prefix stripping. The output directory (the package / import-alias root) acts purely as the assembly-equivalent container; the complete namespace is represented beneath it.
- **FR-003**: Each C# namespace segment MUST map to one nested, kebab-cased folder, with the type emitted as a kebab-cased file beneath its namespace folder — e.g., `Vigiata.Contracts.Profiles.UserProfileDto` → `vigiata/contracts/profiles/user-profile.ts`.
- **FR-004**: A project containing a single transpilable type MUST emit that type at the same full-namespace path it would occupy if sibling types existed (no single-type collapse to the output root).
- **FR-005**: Adopting full-namespace mapping intentionally CHANGES the on-disk layout of existing samples and golden outputs (they gain the full-namespace prefix). This churn is expected and MUST be handled by regenerating and reviewing goldens — it is NOT a regression. The mapping function MUST then remain stable/idempotent across subsequent runs.
- **FR-006**: On every run (including the default incremental build path, not only an explicit full clean), the system MUST detect generated files that a prior run produced but the current run does not produce, and remove them.
- **FR-007**: Orphan removal MUST be restricted to files the transpiler itself generated; it MUST NOT delete hand-written files, unrelated files, or files outside the tracked generated set, even when they live inside the output directory.
- **FR-008**: The system MUST persist enough information about each run's generated output set to reconcile it against the next run for orphan detection.
- **FR-009**: When orphans are removed, the system MUST report what was removed (visible in build output) so the change is observable and auditable.
- **FR-010**: A run that produces an output set identical to the previous run MUST delete nothing and MUST NOT introduce spurious file churn.
- **FR-011**: Orphan pruning MUST also remove now-unneeded barrel/index files and any now-empty generated directories created by previous runs.
- **FR-012**: A consumer MUST be able to import a generated type via `<package-or-alias>/<full-namespace>`, which resolves to that namespace's leaf barrel (the type is also reachable by its direct file path). This path MUST exist in clean generation and MUST NOT depend on orphan files. A root-level import (the package/alias root, e.g. `../contracts`) resolves ONLY when the opt-in root aggregation barrel is enabled; absent that opt-in, root-level imports are explicitly NOT part of the contract.
- **FR-013**: The package's declared subpath entries (import-alias `#…/*` entries and `package.json` exports) MUST reflect the actual full-namespace layout of the generated leaf barrels.
- **FR-014**: Pruning MUST be safe under failure: if a run does not complete successfully, the previously generated, still-valid files MUST NOT be left in a corrupted or half-deleted state.
- **FR-015**: A per-namespace leaf barrel MUST be generated for each namespace that has emitted types, exporting that namespace's own types; this is always generated and is the external import unit. The root namespace-aggregation barrel (a single root index re-creating the C# namespace tree via `export namespace` blocks) MUST remain OPT-IN, because it defeats tree-shaking; it is provided only as an ergonomic single-entrypoint convenience.
- **FR-016**: References between generated types MUST be emitted as direct file imports (the path to the defining type's file), never routed through barrels, so that internal correctness is independent of barrel generation and ESM import cycles are avoided.
- **FR-017**: The behavior MUST be covered by tests for: single-vs-multi-type full-namespace layout stability (FR-001/FR-002/FR-004), namespace move/rename/delete pruning (FR-006/FR-007/FR-011), the identical-output no-churn case (FR-010), failure-safe pruning under an interrupted/failed run (FR-014), internal direct-import references (FR-016), per-namespace leaf barrels with opt-in root aggregation (FR-015), and the consumer import contract under an import alias (FR-012/FR-013).

### Key Entities *(include if feature involves data)*

- **Generated output file**: A `.ts`/`.tsx` file (type file or barrel) produced by the transpiler, identified by its path relative to the output directory; the unit that is written, reconciled, and possibly pruned.
- **Package/container root**: The output directory (reached via the npm package name or the import alias). It is the assembly-equivalent container only; NO namespace prefix is stripped — the full C# namespace is represented beneath it.
- **Generated-output manifest**: The recorded set of files a run produced, persisted so the next run can compute orphans (files in the prior set but absent from the current set).
- **Barrel**: An aggregating `index.ts` for a folder/namespace; part of the import contract and itself subject to pruning when no longer needed.
- **Import contract**: The documented set of paths (namespace subpaths via the import alias, generated barrels, and `package.json` export entries) through which consumers may import generated types.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For any project, adding or removing a type in one namespace changes the output path of zero other types (0% unintended relocations across an incremental edit).
- **SC-002**: A type's output path is identical whether the project contains 1 type or N types (100% path stability across the single-vs-multi-type boundary), because the path is its full namespace.
- **SC-003**: After any rename, move, or delete of a type, an incremental build (no manual clean) leaves zero orphaned generated files on disk.
- **SC-004**: After regenerating goldens to the full-namespace layout, re-running the suite twice yields byte-identical output both times (the mapping is stable/idempotent); the regeneration diff is reviewed and contains only the expected full-namespace path changes.
- **SC-005**: Orphan pruning never deletes a non-generated file — across all test scenarios, 0 hand-written or unrelated files are removed.
- **SC-006**: From a clean generation, a consumer can import every generated type via a documented path with zero dependence on orphan files, verified by a type-check pass with the output directory freshly cleaned and regenerated.
- **SC-007**: The originally reported failure (a root-barrel reference to `UserProfile` that does not exist in clean generation) no longer occurs after a clean regeneration of the Vigiata-style scenario.

## Assumptions

- The transpiler can reliably distinguish files it generated from files it did not (via the persisted generated-output manifest and/or the existing incremental-cache records), and pruning operates only on the former.
- The output layout uses the FULL C# namespace with no stripping (decided, D1); this eliminates any "root namespace" computation and makes the path a pure function of the type's own namespace.
- Existing samples/goldens will be regenerated to the full-namespace layout; that diff is intended (D1), not a regression.
- Cross-package and local output converge on the same full-namespace model (package = assembly = namespace container); cross-package import resolution must continue to work, now via full-namespace subpaths.
- Per-namespace leaf barrels are always generated; the root aggregation barrel stays opt-in due to its tree-shaking cost (D2).
- References between generated types use direct file imports, decoupling correctness from barrels and avoiding ESM cycles (D3).
- The change targets the TypeScript target plus the target-agnostic core (full-namespace path mapping and orphan reconciliation), and should generalize to future targets (e.g., Dart) without target-specific layout instability.
- The fix is delivered behind the normal build (incremental) path so that consumers benefit without opting into a full clean; an explicit full clean remains available and unchanged.

## Dependencies

- The incremental cache / output-manifest mechanism (records the per-run generated output set used for reconciliation).
- The MSBuild build integration (so the self-cleaning behavior applies on the default incremental build, not only the CLI).
- The package-metadata writer (so declared subpath/export entries track the stable layout).
- The configurable import-alias feature (the reported scenario emits into a subfolder under an alias).
