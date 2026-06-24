# Phase 0 Research: Deterministic and Self-Cleaning Output Layout

All items below are resolved; no NEEDS CLARIFICATION remain. Decisions D1–D3 were agreed with the user during specification; the entries here pin the mechanics.

## R1 — Root-namespace policy (the layout anchor)

**Decision**: Map every type by its **full** C# namespace; do not strip any root. Operationally, the per-assembly "root namespace" used for layout becomes the empty string, so `PathNaming.StripRootNamespace(ns)` returns `ns` unchanged and `GetRelativePath` emits one kebab-cased folder per namespace segment.

**Implementation shape**:
- `CSharpSourceFrontend.ComputeLocalRootNamespace` stops deriving the layout root from `NamespaceUtilities.FindCommonPrefix`. `IrCompilation.LocalRootNamespace` becomes `""` (or the host stops feeding a non-empty root to the TS target's `PathNaming`).
- `NamespaceUtilities.FindCommonPrefix` is retained only if still used for a non-layout purpose; otherwise it is removed to avoid dead code (Principle I). A grep across `src/` confirms its layout usages are the ones being neutralized.
- Global-namespace types (`ns == ""`) keep emitting at the output root — unchanged.

**Rationale**: Makes the path a pure function of the type's own namespace (FR-001/FR-002), eliminating the data-dependent instability and the "which root" question entirely. Aligns on-disk paths with .NET fully-qualified names (Principle II) and is a net simplification (Principle VI).

**Alternatives considered**:
- *Anchor to the assembly's declared root namespace (RootNamespace/assembly name), strip it.* Shorter, non-redundant paths, but reintroduces a (smaller) variant of the same bug class (mis-computed/ambiguous root, types outside the declared root) and needs config. Rejected by the user in favor of determinism and simplicity; the redundancy cost is accepted and mitigated by the opt-in root barrel (R4).
- *Keep common-prefix.* Rejected — it is the root cause.

## R2 — Cross-package consistency

**Decision**: Apply the same full-namespace rule to cross-package (referenced-assembly) output: call `PathNaming.ComputeSubPath` with `assemblyRootNamespace = ""` so referenced types are addressed by their full namespace too.

**Rationale**: Unifies local and cross-package layout (package = assembly = namespace container). The npm package name already provides the outer container, so the full namespace under it is unambiguous and collision-resistant even when multiple assemblies share an output root. Keeps a single mental model and a single code path.

**Alternatives considered**: Keep cross-package stripping its per-assembly root while local uses full namespace — rejected as inconsistent (the same type would be addressed differently locally vs. across packages).

## R3 — Internal cross-type import style (D3)

**Decision**: References between generated types resolve to the **defining type's file**, never to a barrel.
- Same namespace: relative file import (`./user-profile`) — current behavior, kept.
- Different namespace: the isolated-alias path to the **file** (not the namespace barrel), i.e. `#<alias>/<full-namespace>/<file>` (e.g. `#web-server-contracts/vigiata/contracts/profiles/user-profile`).

`PathNaming.ComputeRelativeImportPath` changes so the different-namespace branch appends the kebab type file name instead of stopping at the namespace barrel.

**Rationale**: Eliminates ESM import cycles structurally and decouples internal correctness from barrel generation/pruning (FR-016). This intentionally reverses, for *internal* imports, the namespace-first-via-barrel behavior of ADR-0006; the new ADR records that barrels remain the **external** contract while internal edges are direct.

**Alternatives considered**:
- *Relative `../../` paths across namespaces.* Works but produces fragile `../../..` chains that move when nesting depth changes; the alias path is depth-independent. Rejected.
- *Keep routing internal imports through namespace barrels.* Re-introduces cycle risk and couples internal correctness to barrels. Rejected (contradicts D3).

## R4 — Barrel policy (D2)

**Decision**: Keep `BarrelFileGenerator` behavior:
- **Leaf barrel per namespace** (`<full-ns-dir>/index.ts`) — always generated; the external import unit (`<pkg-or-alias>/<full-namespace>`).
- **Root aggregation barrel** (`export namespace` tree at the output root) — remains opt-in via `NamespaceBarrels`.

Under full namespace each leaf directory holds exactly one namespace's own files, so the existing per-directory leaf-barrel logic already yields a clean namespace↔barrel mapping; verify it still fires for nested full-namespace directories.

**Rationale**: The leaf barrel is the consumer entry point; the root aggregation barrel defeats tree-shaking and is therefore an opt-in convenience only (FR-015). No new mechanism required.

## R5 — Orphan reconciliation (self-cleaning) + safety

**Decision**: On a real emit (not a cache hit), reconcile the **previous** run's recorded output set against the **current** set and delete the difference, then remove now-empty generated directories.

- **Manifest source**: the prior `TranspilationCache.OutputHashes` keys (relative paths) read from `<outputDir>/.metano-cache.json`. This is the authoritative set of files Metano produced last run (type files + barrels).
- **Algorithm**: `orphans = priorOutputPaths − currentOutputPaths`; delete each orphan under `outputDir`; then prune directories that became empty (excluding the output root itself and Metano metadata files `.metano-cache.json`, `.metano-cache-groups-typescript.json`, `.metano-stamp`).
- **Ownership guarantee**: only paths present in the prior manifest are eligible for deletion → hand-written/unrelated files are never touched (FR-007), even inside the output dir.
- **When it runs**: only on the full emit path. A **cache hit** changes nothing → no pruning. `--clean` already wipes the whole dir → pruning is a no-op. First run (no prior cache) → nothing to reconcile.
- **`--no-cache`**: no manifest is read/written, so reconciliation has no prior set; pruning is skipped and this is documented (pair with `--clean` for a guaranteed clean tree). Acceptable because `--no-cache` explicitly opts out of incremental state.
- **Failure-safety** (FR-014): reconcile/delete only **after** the current output set has been computed and written successfully, paired with writing the fresh manifest; a failed run leaves the prior valid files intact and the prior manifest unchanged.
- **Reporting** (FR-009): print removed paths to the console (e.g. `Pruned: <relative path>`), consistent with the existing `Cleaned:`/`Updated:` lines.

**Rationale**: Reuses an existing, trustworthy record (the cache manifest) instead of scanning/guessing, keeping pruning O(|prior set|) and provably scoped to Metano-owned files. Lives in `TranspilerHost` (core) so both CLI and MSBuild inherit it.

**Alternatives considered**:
- *Enumerate the output directory and delete anything not in the current set.* Rejected — would delete hand-written files (violates FR-007).
- *Track ownership in a separate sidecar manifest.* Rejected — the cache `OutputHashes` already is that manifest; a second file is redundant (Principle VI).

## R6 — Cache invalidation for the layout change

**Decision**: Add a layout-version token to the TypeScript target's `ConfigurationFingerprint` (e.g. `layout=full-namespace-v1`). The fingerprint already participates in the cache-hit gate, so bumping it forces a full re-emit on the first build after upgrade.

**Rationale**: A compiler upgrade that changes the *layout* is not reflected in source/reference hashes; without a token, a cache hit would happily re-validate and keep the **old** layout (old files still hash-match). The token guarantees the new layout is produced (and the old files become orphans the new pruning step removes in the same run).

**Alternatives considered**: Rely on users running `--clean` after upgrade — rejected (silent wrong behavior by default).

## R7 — package.json exports / import-alias subpaths

**Decision**: `PackageJsonWriter` derives `imports`/`exports` subpaths from the **emitted leaf-barrel paths**, which are now full-namespace (`./apis/web-server/contracts/vigiata/contracts/profiles` etc.). Confirm it reads from the target's `LastSourceFiles` (the actual emitted set) rather than any stripped/common-prefix value, and that the import-alias `#<alias>/*` mapping continues to point at the output root.

**Rationale**: FR-013 — declared subpaths must mirror the real layout so consumers resolve types without depending on orphans.

## R8 — MSBuild applicability

**Decision**: No change to `Metano.Build.targets` for pruning — it runs inside the transpiler core, which the MSBuild target already invokes. `MetanoClean` (full wipe) remains available and unchanged. The target's `Inputs`/`Outputs` stamp still re-runs the transpiler whenever a `.cs`/asset/project input changes (which is exactly when layout/orphans can change); when nothing changes, the target is skipped and no orphan can have been introduced.

**Rationale**: Keeps the fix in one place (the core host) and automatically covers both the CLI and MSBuild incremental paths (FR-006).

## Churn assessment (informational)

- **Unit tests**: inline sources in the global namespace (`ns==""`) are unaffected (still flat at root). Only tests whose inline C# *declares* a namespace (notably cross-package tests using `[EmitPackage]`) change paths and need assertion/golden updates.
- **Golden fixtures** (`tests/Metano.Tests/Expected/`, 36 files): regenerate those whose source declares a namespace.
- **Samples** (`targets/js/*`): namespace-declaring samples (e.g. SampleTodo, SampleIssueTracker) gain the full-namespace prefix; their hand-written consumers / `tsconfig` / `package.json` subpaths are updated and bun:test re-run to green.
- This churn is intentional (FR-005), not a regression; it is captured as explicit tasks in Phase 2.
