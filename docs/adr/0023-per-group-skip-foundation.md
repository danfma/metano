# ADR-0023 — Per-group skip foundation: type + closure signature hashing

**Status:** Accepted (foundation only — integration deferred)
**Date:** 2026-05-11

## Context

PR 3a (#214 / ADR-0021) shipped the whole-build short-circuit: if
every source file, reference fingerprint, and output content matches
the previous run, the entire pipeline is skipped. The follow-up
asked for **per-group skip**: when only one file group's closure
changes, only that group regenerates while the rest of the build
reuses cached output.

The dependency graph from PR 1 (#211 / ADR-0018) gives the
"what types belong to a closure" answer. What's missing is the
"did this closure change?" answer — a stable fingerprint that
flips on body edits, signature edits, and attribute edits to
anything inside the closure, and stays stable when an unrelated
type elsewhere in the project moves.

## Decision

Ship the per-type and per-closure hashers as standalone, reusable
primitives in `src/Metano.Compiler/Caching/`. The integration into
`TypeTransformer.TransformGroup` is deferred to a follow-up PR
once the file-descriptor abstraction is designed (see
"Deferred" below).

### `IrTypeSignatureHasher.Hash(INamedTypeSymbol)`

SHA-256 over a deterministic serialization that includes:

- **Identity**: FQN, TypeKind, accessibility, modifiers
  (abstract / sealed / static / record / value-type / readonly),
  generic arity.
- **Hierarchy**: base type FQN, every interface FQN (sorted).
- **Attributes**: every `[…]` on the type, sorted, with
  constructor argument values folded in via
  `TypedConstant` pretty-printing.
- **Members**: every method / property / field / event signature
  (sorted), including return / parameter types, accessibility,
  static / virtual / override / abstract bits, ref kinds, params
  and default-value presence.
- **Body text**: the full declaring-syntax text per partial
  declaration. This catches edits to method bodies, field
  initializers, and property getters that the symbol shape alone
  would miss — the dep graph (ADR-0018) is signature-only, so
  body-level invalidation has to live here.

### `GroupClosureHasher.HashGroupClosure(groupFqns, graph, sigIndex)`

SHA-256 over the per-type hashes of every member of the group's
**transitive dependency closure**, sorted by FQN. Combined with
the global cache key from PR 3a (target language + configuration
fingerprint + reference fingerprints), this becomes the exact
unit of invalidation per-group skip will need: change a type
inside the closure and the hash flips; change a type outside
and the hash stays.

`BuildSignatureHashIndex` amortises `Hash()` across groups whose
closures overlap (a popular base class gets hashed once, then
reused for every dependent group).

### Why these primitives, separately, now

The hashers stand on their own:

- Pure functions, no shared state, trivially testable.
- The dep graph (PR 1) is already merged; the hasher is the
  smallest unit that turns "graph + symbol" into "stable cache
  key".
- Future per-group skip integration only needs to *call* these —
  they pin the contract of what counts as a "change" so the
  integration PR is a wiring exercise, not a design exercise.

## Deferred (follow-up PR)

The wiring inside `TypeTransformer` is not in this PR. The
blocker is the **file-descriptor abstraction**: downstream stages
(`BarrelFileGenerator`, `CyclicReferenceDetector`) consume the
full `TsSourceFile` AST. A cache hit must hand them something
that walks identically without forcing the per-group transform
to actually run.

Two designs under consideration for the follow-up:

1. **Stub `TsSourceFile`**: reconstruct a `TsSourceFile` from
   cached metadata containing exactly the AST nodes the two
   downstream stages read (TsImport + minimal export markers).
   Lightweight cache schema but brittle — any new downstream
   stage that reads other AST nodes silently breaks.
2. **`IGeneratedFileSummary` interface**: refactor
   `BarrelFileGenerator` and `CyclicReferenceDetector` to consume
   a lightweight descriptor (path + import paths + export entries).
   `TsSourceFile` implements it via an AST walk; cached files
   implement it from the on-disk metadata. Cleaner long-term but
   touches more files.

The follow-up PR will pick one based on the trade-offs and
deliver the user-visible per-group skip.

## Consequences

(+) The dep graph (PR 1) gets a concrete consumer; before this
   only the architectural ADR (ADR-0018) referenced it.
(+) The cache invalidation key for per-group skip is now defined,
   tested, and frozen. The follow-up PR cannot accidentally
   diverge.
(+) The hashers are reusable beyond per-group skip — any future
   incremental work (cross-target consistency checks, type-level
   diff tooling) can layer on the same fingerprint.
(−) No user-visible improvement in this PR. The win lands in the
   follow-up.
(−) The hashers cost CPU per run even when nothing else uses
   them. Mitigation: callers opt in by invoking
   `BuildSignatureHashIndex` only when the per-group cache path
   activates.

## Alternatives considered

- **Inline the hasher into `TypeTransformer`.** Rejected: hides a
  load-bearing primitive inside an already-large class and makes
  unit testing the invalidation contract harder.
- **Hash from the IR instead of Roslyn symbols.** Rejected for
  the MVP: `IrCompilation.Modules` is not eagerly populated
  (ADR-0018), and the targets read from the Roslyn symbols
  directly during transform. Hashing from symbols matches what
  the transform actually depends on.
- **Reuse PR 3a's source-file SHA as the per-group hash.**
  Rejected: a source file can declare multiple types belonging
  to different groups, and one type's edit would invalidate
  unrelated groups that happened to share a `.cs` file. The
  type-level fingerprint is the right granularity.

## References

- `src/Metano.Compiler/Caching/IrTypeSignatureHasher.cs`
- `src/Metano.Compiler/Caching/GroupClosureHasher.cs`
- `tests/Metano.Tests/Caching/IrTypeSignatureHasherTests.cs`
- `tests/Metano.Tests/Caching/GroupClosureHasherTests.cs`
- ADR-0018 — type-level dependency graph (the closure walker)
- ADR-0020 — parallel TypeTransformer (the loop the skip will
  cut into)
- ADR-0021 — incremental cache (the per-group entries layer on
  top of the whole-build cache)
- Issue #21 — incremental compilation + parallel TypeTransformer
