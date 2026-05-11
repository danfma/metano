# ADR-0024 — Per-group skip integration

**Status:** Accepted
**Date:** 2026-05-11

## Context

PR 3b (#216 / ADR-0023) shipped the hashers — `IrTypeSignatureHasher`
for per-type fingerprints and `GroupClosureHasher` for per-group
closure hashes. PR 3a (#214 / ADR-0021) shipped the whole-build
short-circuit. Per-type sig hash plus the dep graph from PR 1
(#211 / ADR-0018) gave us "did this group's closure change?", but
the question we punted was **how to wire the skip into
`TypeTransformer` without breaking downstream stages**
(`BarrelFileGenerator`, `CyclicReferenceDetector`) that consume the
full `TsSourceFile` AST.

ADR-0023 sketched two designs (stub `TsSourceFile` vs
`IGeneratedFileSummary` interface). This PR picks the **stub**
design — surgical, minimal file churn, no abstraction refactor —
and documents the constraints.

## Decision

### Cache file: `<outputDir>/.metano-cache-groups-typescript.json`

Per-target group cache, keyed by `namespace/fileName`:

```json
{
  "formatVersion": 1,
  "groups": {
    "Demo.Issues.Domain/issue": {
      "closureHash": "<sha256>",
      "files": [
        {
          "path": "issues/domain/issue.ts",
          "imports": [
            { "names": ["UserId"], "from": "#/shared-kernel/user-id" }
          ],
          "exports": [
            { "name": "Issue", "typeOnly": false }
          ]
        }
      ]
    }
  }
}
```

The TypeScript target manages this file independently from PR 3a's
target-agnostic cache; both files coexist next to the generated
output so a single `--clean` wipes both.

### Stub `TsSourceFile` on cache hit

`FileMetadataExtractor`:
- `Extract(TsSourceFile)` → builds `CachedFileMetadata` (path,
  imports preserved with full `TsImport` shape, exports collapsed
  to `(name, typeOnly)` pairs).
- `BuildStub(CachedFileMetadata)` → reconstructs a stub
  `TsSourceFile` with the original `TsImport` records plus minimal
  export markers (`TsInterface` for type-only, `TsConstObject` for
  value). Those are exactly the two `TsTopLevel` shapes the
  downstream stages match on.

The stub flows through `BarrelFileGenerator` and
`CyclicReferenceDetector` identically to a fresh AST: barrel
emission picks the right re-export form (value vs type-only),
cycle detection walks the imports.

### Bypass the Printer for cached files

The stub deliberately does **not** carry the real body. Printing it
would produce empty TypeScript, overwriting the cached `.ts` file.
`TypeTransformer.CachedFileContents` exposes the raw on-disk
content per cache-hit path (with the host's file-prefix block
stripped so the emit pass can re-apply it without doubling). The
TypeScript target consults this map before calling `Printer.Print`
— hits emit the cached content verbatim, misses go through the
normal printer.

### Skip decision

`TypeTransformer.TransformAll`:

1. Build the dep graph + per-type sig index (`BuildSignatureHashIndex`).
2. Compute the closure hash for every group (`HashGroupClosure`).
3. Load the on-disk group cache if `CacheOutputDir` is set.
4. Per group, inside the existing `Parallel.For`:
   - **Hit** when the cached entry's closure hash matches AND the
     on-disk file still exists. Build the stub, store the raw
     content, increment the skip counter, return.
   - **Miss** runs `TransformGroup` as before. Extract the
     metadata for the next run's cache.
5. After the loop, write the fresh cache (all groups, hits and
   misses).

A summary line —
`Per-group cache: 17/18 group(s) reused from cache.` — fires when
the skip activates.

### Why ITranspilerTarget.Transform now takes outputDir + filePrefix

The TypeScript target needs to know **where** the cache lives and
**which prefix** to strip from cached content. The host knew both
all along; we extended `Transform` with two optional parameters
instead of inventing a side-channel property. Backwards-compatible
default (`null`) keeps any third-party target compiling.

## Consequences

(+) PR 3a's whole-build path stays as the fast lane; PR 3c wins
   when PR 3a misses but only one type changed — a single editor
   save now regenerates one file, the other N-1 reuse the disk
   content.
(+) Watch mode (#18 / ADR-0022) inherits the per-group skip
   automatically — the same `TranspilerHost.RunAsync` runs on
   every tick.
(+) No abstraction refactor. `BarrelFileGenerator` and
   `CyclicReferenceDetector` are unchanged. The stub design's risk
   (downstream stages start reading other AST nodes) is contained
   to two callers and easy to revisit if it bites.
(+) Smoke-verified on SampleIssueTracker: 18 groups, one type
   edit → 17 reused, 1 regenerated; byte-identical output.
(−) The stub approach assumes downstream stages only read
   `TsImport` + the seven export-bearing `TsTopLevel` variants
   (`TsClass`, `TsFunction`, `TsEnum`, `TsConstObject`,
   `TsNamespaceDeclaration`, `TsTypeAlias`, `TsInterface`). Any
   future stage that reads e.g. `TsVariableDeclaration` will
   silently get an empty list from a cached file. The
   `FileMetadataExtractor` is the contract point — extend it
   when adding a new downstream stage.
(−) Group cache is target-specific (different file name per
   target). Cross-target reuse is not in scope.
(−) Per-group cache only invalidates when a closure member's hash
   changes. Edits to types **outside** every group's closure
   (orphaned types) leave the cache stale on disk; PR 3a's
   reference-fingerprint check catches edits to BCL / foreign
   assemblies, but a truly orphaned source file would not flip
   the per-group cache. Acceptable today — orphaned types do not
   emit and so do not appear in any group anyway.

## Alternatives considered

- **`IGeneratedFileSummary` interface refactor.** Cleaner
  long-term but requires `BarrelFileGenerator` and
  `CyclicReferenceDetector` to migrate. Out of scope for this PR.
- **Cache the full `TsSourceFile` AST (JSON or binary).** Would
  remove the stub fragility but at the cost of writing custom
  `JsonConverter`s for every `TsTopLevel` subtype. Heavyweight
  for the marginal win.
- **Single shared cache file for all targets.** Rejected: the
  per-group cache schema is target-specific (different AST
  shapes per target). One file per target keeps the schemas
  decoupled.

## References

- `src/Metano.Compiler.TypeScript/Caching/CachedFileMetadata.cs`
- `src/Metano.Compiler.TypeScript/Caching/FileMetadataExtractor.cs`
- `src/Metano.Compiler.TypeScript/Caching/GroupCacheFile.cs`
- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`
  (`TransformAll` per-group skip block + helpers)
- `src/Metano.Compiler.TypeScript/TypeScriptTarget.cs`
  (`CachedFileContents` bypass)
- ADR-0018 — dep graph
- ADR-0021 — whole-build cache (the layer this builds on top of)
- ADR-0023 — per-group hashers (PR 3b foundation)
- Issue #21 — incremental compilation + parallel TypeTransformer
