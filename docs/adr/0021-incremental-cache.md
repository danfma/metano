# ADR-0021 — Incremental cache: whole-build short-circuit

**Status:** Accepted
**Date:** 2026-05-07

## Context

Issue #21 asks for an incremental compilation cache so the transpiler
skips work when nothing changed. Watch mode (#18) needs the same
machinery: a file save that does not actually alter content (an IDE's
save-on-focus-loss, a no-op formatter pass, a `touch`) should produce
zero output instead of triggering a full re-emit.

The dependency graph from PR 1 (#211 / ADR-0018) and the parallel
TypeTransformer from PR 2 (#213 / ADR-0020) both expect a cache layer
on top. The hard question is *granularity*: per-type (using sig
hashes + the dependency graph) gives the finest skip but requires a
file-descriptor abstraction so downstream stages (barrel generation,
cyclic-import detection, file emit) can consume both freshly
transformed and cached outputs uniformly.

## Decision

PR 3 ships the **whole-build short-circuit** as the MVP. Per-type
skip ("PR 3b") is a follow-up that builds on top of this layer.

The cache file lives at `<outputDir>/.metano-cache.json`. Shape:

```json
{
  "formatVersion": 2,
  "target": "TypeScript",
  "configurationFingerprint": "target=namespaceBarrels=False;stripInterfacePrefix=False;filePrefix=",
  "sourceHashes":           { "<absolute .cs path>": "<sha256-hex>" },
  "referenceFingerprints":  { "<absolute .dll path>": "<length>:<lastWriteTimeUtc.ticks>" },
  "outputHashes":           { "<relative output path>": "<sha256-hex>" }
}
```

The `configurationFingerprint` mixes the host's `--file-prefix` with each
target's per-run flags exposed via the new
`ITranspilerTarget.ConfigurationFingerprint` property (the TypeScript
target pins `NamespaceBarrels` and `StripInterfacePrefix`; the Dart
target has no flags today and returns the empty string). Flag flips
invalidate the cache even when sources and references are
byte-identical.

`TranspilerHost.RunAsync` short-circuits the pipeline when **all** of
the following match the cached fingerprint:

1. The set of `.cs` files Roslyn parsed and each file's SHA-256.
2. Every metadata reference's `length:lastWriteTimeUtc` tuple.
3. Every previously emitted output file still exists on disk with a
   SHA-256 matching the cached value.

If anything diverges, the host runs the full pipeline and overwrites
the cache file at the end. `--clean` wipes the output directory
(including the cache file) before the run, so a clean run always
rebuilds, but the post-run write still happens — the next run gets a
fresh cache to consult. `--no-cache` opts the run out of both reading
and writing. `--dry-run` opts out of writes — there are no on-disk
files to pin.

The cache hit path validates and rehydrates outputs in a single disk
pass: each cached path is checked for traversal segments
(`..` / rooted), opened once, streamed through `SHA256.HashData`, and
then read back as text for the rehydrated `GeneratedFile`. The host
strips the file-prefix block when present so
`TranspileResult.Files` on a cache hit matches what
`target.Transform` would return on a full run (no prefix — the host
adds it at write time).

### Why source-hash + reference-fingerprint, not type-level sig hashes (yet)

Whole-build short-circuit does not need the dependency graph or
per-type signature hashing. The `.cs` content hashes already cover
both signature edits and body edits, and the reference fingerprints
catch external assembly swaps without reading every .dll on every
run. Per-type granularity (PR 3b) needs body-vs-signature separation
because the dep graph is signature-only (ADR-0018) and a body-only
edit must still re-emit the affected file even though no dependent's
sig closure changed. That separation, plus a file-descriptor type
that lets cached files participate in barrels and cyclic detection
without an AST, is the work PR 3b takes on.

### Why `length:ticks` for reference fingerprints

Hashing a 50 MB BCL .dll on every run would dominate the cold-start
budget. The mtime+length tuple flips on a recompile of the
referenced project, on a NuGet upgrade, and on a manual swap — the
three real change cases. Drift cases are file-system-clock skew
across machines (each machine maintains its own cache) and an
identical-length write that preserves mtime (extremely rare; opt-out
via `--no-cache`).

### Why store output hashes

The cache is meaningless if the user — or a sibling target's
`--clean` — deleted half the output directory. The output-hash check
detects both deletions and out-of-band edits (someone hand-fixed a
generated file; we should regenerate to overwrite it). The cost is
a content read per output on cache check, which is bounded by the
size of the previous emit and dwarfed by the avoided full pipeline.

## Consequences

(+) Watch mode's "nothing changed" tick is now O(file count) — read
   syntax trees, hash, compare, exit. No transform, no print, no
   write.
(+) Re-running a green build is free even without watch mode (CI
   re-runs on the same SHA, local rebuild loops).
(+) Cache file format is versioned (`formatVersion: 1`); future
   schema changes bump the version and treat older caches as stale.
(+) `--clean` semantics unchanged: nuking the output directory
   rebuilds from scratch. Users do not have to learn a separate
   cache-clear command.
(−) Coarse granularity. A single character change in any .cs file
   re-runs the full pipeline. PR 3b lifts this to per-group skip
   once the file-descriptor abstraction lands.
(−) The reference fingerprint trusts the file system's mtime
   resolution. A pathological case (write of identical length within
   the same mtime tick) would hide a swap. Treated as a known
   limitation; `--no-cache` is the escape hatch.
(−) Cache files now live next to outputs. Users committing the
   `targets/js/...` tree to git need a `.gitignore` entry; the repo
   adds one centrally.
(−) Target-specific post-emit hooks (today only the TypeScript
   target's <c>PackageJsonWriter</c>) skip on a cache hit because
   their inputs (<c>target.LastSourceFiles</c>,
   <c>target.LastEmitPackageName</c>, …) live on the target instance
   and only get populated by <c>target.Transform</c>. In practice the
   writer is idempotent (merge semantics) so a hand-edited
   <c>package.json</c> survives a cache hit; the failure mode is "user
   deletes <c>package.json</c> between runs" — recovery is
   <c>--clean</c> or <c>--no-cache</c>. PR 3b folds the post-emit hook
   into the cache so this round-trips automatically.

## Alternatives considered

- **Type-level sig hashes from PR 1's dependency graph.** Rejected
  for the MVP: need a file-descriptor abstraction so cached files
  can flow through barrel generation and cyclic detection without an
  AST. Tracked as PR 3b.
- **mtime-only cache key.** Rejected: every editor save bumps mtime
  even when content is unchanged, defeating the watch-mode noop case
  (one of the two motivating wins).
- **Cache lives in `obj/metano-cache/<target>.json`.** Rejected: the
  output dir is the natural home — it is what `--clean` already
  manages and what users delete to "start over". Splitting cache
  state across two roots invites stale-cache-against-clean-output
  bugs.
- **Hash every metadata reference's bytes.** Rejected: too slow on
  cold start. The mtime+length tuple is good enough for the cases
  that matter and falls back gracefully via `--no-cache`.

## References

- `src/Metano.Compiler/Caching/TranspilationCache.cs`
- `src/Metano.Compiler/Caching/CacheKeyBuilder.cs`
- `src/Metano.Compiler/TranspilerHost.cs` — `TryShortCircuitFromCache`
- ADR-0018 — type-level dependency graph (input for PR 3b)
- ADR-0020 — parallel TypeTransformer (the parallel loop is the work
  the cache lets us skip)
- Issue #21 — incremental compilation + parallel TypeTransformer
- Issue #18 — watch mode (the cache's primary consumer)
