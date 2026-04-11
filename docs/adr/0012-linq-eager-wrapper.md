# ADR-0012 — LINQ as eager wrapper hierarchy (with pipe-based migration tracked)

**Status:** Accepted
**Date:** 2026-03-25

## Context

C# LINQ over `IEnumerable<T>` is lazy: operators like `Where`, `Select`,
`OrderBy` build a composed pipeline that is only evaluated when a
terminal operator (`ToArray`, `Count`, `First`, etc.) walks it. This
laziness matters for:

- **Short-circuit terminals.** `items.Take(5).First()` must not
  materialize the whole `items` sequence.
- **Long chains over large data.** `items.Where(…).Select(…).Where(…)`
  should not allocate intermediate arrays between stages.
- **Infinite or expensive sources.** `Enumerable.Range(0, int.MaxValue).Take(10)`
  must not try to enumerate all ints.

TypeScript has no built-in lazy-enumerable abstraction. Only
`Array.prototype.filter/map/...` (eager, allocating arrays at each
step) and generators / iterators (which preserve laziness but require
explicit iteration protocols).

When transpiling LINQ, the compiler has three translation strategies:

1. **Eager array methods.** `items.Where(x => x > 0).Select(x => x * 2).ToArray()`
   → `items.filter(x => x > 0).map(x => x * 2)`. Loses laziness —
   intermediate arrays are materialized. Breaks short-circuit
   terminals.
2. **Eager-dispatch wrapper class hierarchy.** Ship an
   `EnumerableBase<T>` runtime abstraction with one concrete subclass
   per operator (`WhereEnumerable`, `SelectEnumerable`, …). Each
   subclass holds a reference to its source and defers work; terminal
   methods walk the chain. Laziness preserved, but the runtime ships
   every operator and isn't tree-shakeable.
3. **Pipe-based functional composition.** Standalone operator
   functions imported individually, composed via a `pipe` function:
   `pipe(from(items), where(x => x > 0), select(x => x * 2), toArray())`.
   Tree-shakeable — bundlers drop operators the user didn't import.
   More RxJS-like.

Option 1 breaks LINQ semantics. Options 2 and 3 both preserve
semantics; the trade-off is between "simple lowering + bundle includes
every operator" and "granular bundle + larger upfront runtime
refactor".

## Decision

Implement **option 2** as the current design, and **explicitly track
option 3** as
[issue #20](https://github.com/danfma/metano/issues/20) — the
eventual migration when tree-shaking LINQ becomes load-bearing.

`metano-runtime` ships `EnumerableBase<T>` with concrete subclasses per
operator. `BclMapper` lowers C# LINQ calls into fluent chains against
`Enumerable.from(x)`, using the declarative mapping infrastructure
([ADR-0003](0003-declarative-bcl-mappings.md)): one `JsTemplate` per
LINQ method, `WrapReceiver = "Enumerable.from"` to inject the outer
wrap on the first call, and `IsAlreadyLinqChain()` as an
anti-double-wrap guard so long fluent chains only receive the
`Enumerable.from` wrap once.

Detection uses Roslyn's semantic model: `IsLinqExtensionMethod`
distinguishes `Enumerable.Where(this IEnumerable<T>, Func<T,bool>)`
from `List<T>.Where(Func<T,bool>)` (hypothetically), and
`IsCollectionType` handles the direct-method case. The two paths lower
differently — collection methods go straight to array methods, LINQ
extension methods go through `Enumerable.from`.

Supported operators:

- **Composition:** `where`, `select`, `selectMany`, `orderBy`,
  `orderByDescending`, `take`, `skip`, `distinct`, `distinctBy`,
  `groupBy`, `concat`, `takeWhile`, `skipWhile`, `reverse`, `zip`,
  `append`, `prepend`, `union`, `intersect`, `except`.
- **Terminals:** `toArray`, `toMap` (`ToDictionary`), `toSet`
  (`ToHashSet`), `first`, `firstOrDefault`, `last`, `lastOrDefault`,
  `single`, `singleOrDefault`, `any`, `all`, `count`, `sum`,
  `average`, `min`, `max`, `minBy`, `maxBy`, `contains`, `aggregate`.

The planned migration to option 3, tracked as
[issue #20](https://github.com/danfma/metano/issues/20), will:

- Introduce `OperatorFn<T, R>` + `pipe()` with typed overloads.
- Convert each operator into a standalone function: `where(pred)`
  returns an `OperatorFn`.
- `EnumerableBase.pipe(...operators)` applies the chain.
- `BclMapper` generates `pipe()` chains instead of fluent calls.
- Granular imports in generated code — one import per operator used.
- Fluent API remains available as optional sugar for manual use.

The refactor is localized to `BclMapper` + `metano-runtime/system/linq`.
**User C# code is unaffected.** When it ships, existing consumers
regenerate their output and get smaller bundles without editing a
single C# file.

## Consequences

- (+) C# LINQ semantics are preserved: lazy composition, short-circuit
  terminals (`First`, `Take`), infinite-source safety.
- (+) `BclMapper` lowering is simple. One declarative mapping per
  LINQ method; no special control flow for the LINQ lowering in the
  compiler itself.
- (+) Roslyn-driven detection (LINQ extension vs. collection method)
  keeps the lowering correct even when the same method name exists
  on both an extension and a concrete collection type.
- (+) The anti-double-wrap guard keeps generated chains clean — a
  long `Where().Select().Where()` chain wraps once with
  `Enumerable.from`, not three times.
- (+) The migration path to option 3 is clear and localized. When it
  runs, it will churn every generated TS file that uses LINQ
  (regenerated once), but user C# stays untouched.
- (−) The runtime is **not tree-shakeable**. Importing `Enumerable`
  pulls the whole operator registry. For apps that only use
  `Where` + `ToArray`, the bundle still contains `GroupBy`,
  `DistinctBy`, `Aggregate`, and everything else.
- (−) Allocation overhead: each operator call allocates a new
  `EnumerableBase` subclass instance. For hot paths over large data,
  this matters; consumers who need maximum throughput can bypass LINQ
  and use array methods directly (eager, allocates intermediates but
  skips the wrapper class).
- (−) The eventual migration will be a visible churn event for every
  consumer. Worth doing once, but not for free.

## Alternatives considered

- **Option 1 — eager array methods**: rejected. Loses laziness,
  breaks `First()` / `Take(N)` short-circuits, violates C# LINQ
  semantics.
- **Option 3 — pipe-based from day one**: deferred. Bigger upfront
  refactor, less critical until tree-shaking becomes a must. The
  fluent wrapper is also easier to debug (stack traces show
  human-readable class names like `WhereEnumerable`), which matters
  while the runtime is still stabilizing.
- **Third-party library (`ix.js`, `linq.ts`, `itertools-ts`)**:
  rejected. External dependency with its own quirks, we'd still need
  a mapping layer between C# LINQ and the library's API, and bugs in
  the upstream would block us.

## References

- `js/metano-runtime/src/system/linq/` — `EnumerableBase` +
  operator subclasses + terminals
- `src/Metano/Runtime/Linq.cs` — declarative LINQ mappings via
  `[MapMethod]`
- `src/Metano.Compiler.TypeScript/Transformation/BclMapper.cs` —
  `WrapReceiver`, `IsLinqExtensionMethod`, `IsCollectionType`,
  `IsAlreadyLinqChain`
- `tests/Metano.Tests/` — LINQ lowering tests across
  `ExtensionTranspileTests.cs`, `GenericTranspileTests.cs`,
  `MethodOverloadTests.cs`
- [Issue #20](https://github.com/danfma/metano/issues/20) — LINQ
  pipe-based migration for tree-shaking (tracks the eventual migration
  to option 3).
- Related: [ADR-0003](0003-declarative-bcl-mappings.md) (the
  declarative mapping infrastructure the LINQ lowering rides on).
