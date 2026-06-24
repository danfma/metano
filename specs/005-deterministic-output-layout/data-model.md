# Phase 1 Data Model: Deterministic and Self-Cleaning Output Layout

This feature is a compiler behavior change; the "data model" is the set of conceptual entities the pipeline reasons about, their fields, and the rules that bind them.

## Entity: Type → Path mapping

The deterministic function from a transpilable type to its on-disk file.

| Field | Description |
|-------|-------------|
| `fullNamespace` | The type's complete C# namespace (`Vigiata.Contracts.Profiles`), or `""` for the global namespace. |
| `typeName` | The emitted name (after `[Name]`), e.g. `UserProfile`. |
| `isJsx` | Whether the type emits `.tsx` (else `.ts`). |
| `relativePath` (derived) | `kebab(seg₁)/…/kebab(segₙ)/kebab(typeName).ext`, where `seg₁..segₙ` are the full namespace segments. Global namespace → `kebab(typeName).ext` at the root. |

**Rules**
- The mapping depends ONLY on `fullNamespace` + `typeName` + `isJsx` — never on the set of other types present (FR-001).
- No root/common-prefix stripping (FR-002): every namespace segment becomes a folder (FR-003).
- Single-type projects use the same rule (no collapse) (FR-004).
- Cross-package types use the identical rule (R2) — the referenced assembly's root namespace is not stripped.

## Entity: Generated output file

A unit written, reconciled, and possibly pruned.

| Field | Description |
|-------|-------------|
| `relativePath` | Path relative to the output directory (POSIX separators). |
| `kind` | `TypeFile` \| `LeafBarrel` \| `RootAggregationBarrel`. |
| `contentHash` | sha256 of the emitted content (already produced for the cache). |

**Rules**
- A `LeafBarrel` (`<full-ns-dir>/index.ts`) exists for every namespace that has emitted type files (FR-015, always-on).
- A `RootAggregationBarrel` (`index.ts` at the output root, `export namespace` blocks) exists ONLY when `NamespaceBarrels` is opt-in enabled (FR-015).
- Every generated file's `relativePath` is recorded in the run manifest (below).

## Entity: Generated-output manifest

The persisted record of a run's output set, used to reconcile orphans next run.

| Field | Description |
|-------|-------------|
| `outputPaths` | Set of `relativePath` for every file the run generated (type files + barrels). Backed by `TranspilationCache.OutputHashes` keys in `<outputDir>/.metano-cache.json`. |
| `configurationFingerprint` | Includes the new `layout=full-namespace-v…` token (R6) so a layout change invalidates prior caches. |

**Rules**
- On a real emit, `orphans = previous.outputPaths − current.outputPaths` are deleted (FR-006), restricted to this set (Metano-owned) (FR-007).
- Reconciliation is skipped on cache hit and on `--no-cache`; `--clean` makes it moot (R5).
- The manifest is rewritten only after a successful emit (FR-014).

## Entity: Import edge

How one module references a generated type.

| Field | Description |
|-------|-------------|
| `from` | Importing module. |
| `toType` | Referenced generated type. |
| `style` | `InternalDirectFile` (between generated types) \| `ExternalBarrel` (consumer via leaf barrel) \| `RootAggregation` (consumer via opt-in root barrel). |
| `specifier` (derived) | Internal same-ns → `./<file>`; internal cross-ns → `#<alias>/<full-ns>/<file>`; external → `<pkg-or-alias>/<full-ns>` (leaf barrel); root → `<pkg-or-alias>` (opt-in only). |

**Rules**
- Generated-to-generated references MUST be `InternalDirectFile` (FR-016) — never through a barrel (no ESM cycles).
- A consumer importing a type uses `<pkg-or-alias>/<full-ns>` which resolves in clean generation without orphans (FR-012).
- Root-level imports resolve only when the root aggregation barrel is enabled (FR-012).

## Entity: Package metadata (package.json) subpaths

| Field | Description |
|-------|-------------|
| `imports["#<alias>/*"]` | Maps the isolated alias to the output root (unchanged). |
| `exports["./…/<full-ns>"]` | One entry per emitted leaf barrel, at its full-namespace subpath (FR-013). |

**Rules**
- Subpath entries are derived from the actually-emitted leaf-barrel paths (R7), so they always mirror the real (now stable) layout.

## State transitions (run lifecycle)

```text
load → compile → transform (compute Type→Path for every type, full namespace)
     → emit current output set (type files + leaf barrels [+ root barrel if opt-in])
     → reconcile: delete (previous.outputPaths − current.outputPaths) + empty dirs   [skipped on cache hit / --no-cache]
     → write manifest (OutputHashes + layout-versioned fingerprint)
     → update package.json subpaths from emitted barrels
```
