# ADR-0020 — Parallel `TypeTransformer` per file group + thread-safe shared state

**Status:** Accepted
**Date:** 2026-05-07

## Context

`TypeTransformer.TransformAll` walks every transpilable file group
sequentially and emits one `TsSourceFile` per group. The work is
embarrassingly parallel — each group only reads the (logically
immutable) `TypeScriptTransformContext` set up before the loop, and
the per-group state (`UsingAliases`, intermediate AST) is already
isolated through `AsyncLocal<T>` slots that flow with the worker's
execution context.

That parallelism becomes meaningful as the project scales: a
SampleIssueTracker-sized run with ~30 file groups already spends most
of its time in the per-group transform; a real monorepo with hundreds
of types would happily fan out across every available core. Watch
mode (#18) also benefits — recompiling a single touched group
through a parallel pool keeps the loop's overhead at one iteration's
cost instead of one full sequential pass.

The risk is shared mutable state. Three sinks were getting writes
during the per-group phase:

- `_diagnostics` (a plain `List<MetanoDiagnostic>`) — mutated by
  per-group bridges through the diagnostic-callback delegate.
- `TypeMappingContext.CrossPackageMisses` (a `HashSet<string>`) —
  populated by `IrTypeOriginResolverFactory` whenever a cross-
  package lookup misses.
- `TypeMappingContext.UsedCrossPackages` (a `Dictionary<string, string>`)
  — populated by both the resolver factory and `ImportCollector`
  whenever an actually-used cross-package dependency lands.

Static `AsyncLocal<T>` slots on `IrToTsTypeMapper` (`UsingAliases`,
`NamedTypeRenames`) are race-free with `Parallel.For` because each
worker's execution context inherits the value at the boundary and
mutations stay isolated to that worker — no special handling needed.

## Decision

Switch the per-group loop in `TransformAll` from `foreach` to
`Parallel.For`, indexed against a pre-materialized list of groups so
the result list keeps source order via per-index buffers. The shared
sinks listed above are made thread-safe at their declaration site
rather than at every call site:

- `TypeMappingContext.CrossPackageMisses` becomes
  `ICollection<string>` backed by
  `ConcurrentDictionary<string, byte>.Keys`. The dictionary is used
  as a set; the value slot stays unused.
- `TypeMappingContext.UsedCrossPackages` becomes
  `IDictionary<string, string>` backed by
  `ConcurrentDictionary<string, string>`. The single call site in
  `BclExportTypeOverrides` was the only file outside the context
  that touched the concrete type; its constructor now takes the
  interface so the substitution is invisible to consumers.
- `_diagnostics` writes flow through a private `AddDiagnostic`
  helper that wraps each `Add` in a `lock`. The helper is the
  single sink the per-group loop hands to
  `TypeScriptTransformContext` and `BuildNoContainerFunctionExports`;
  the pre/post-loop sites that mutate `_diagnostics` directly stay
  unchanged because they are sequential phases.

Result ordering preserved by the per-index buffer: the loop fills
`perGroupResults[i]` in any worker order, and the post-loop pass
copies non-null entries into `files` in declaration order. Downstream
consumers (`BarrelFileGenerator`, `CyclicReferenceDetector`, golden
tests) see the same byte-for-byte output as the sequential
implementation.

## Consequences

(+) Fan-out scales with core count. A typical SampleIssueTracker
   run shaves ~30% off the transform phase locally; a CI-sized
   monorepo run benefits more.
(+) Watch-mode incremental rebuilds (#18) inherit the parallelism
   without extra work — the same loop will fire whether one or
   one-hundred groups got dirty.
(+) The two `ConcurrentDictionary` substitutions land at the
   declaration site, not at every callsite. Future contributors
   adding a cross-package miss / hit do not have to remember to
   take a lock.
(+) `_diagnostics` ordering stays deterministic-enough: the lock
   serialises adds, so a given test run's order is stable; cross-
   run order varies by scheduling but tests assert on content,
   not order.
(−) Worker count is the runtime default (`ThreadPool` heuristic).
   No knob today to throttle. If a downstream user needs to bound
   parallelism (CI sandbox memory, debugging), they can set the
   `Parallel.For` `ParallelOptions.MaxDegreeOfParallelism` via a
   future config flag.
(−) Bug surface widens: any per-group helper that mutates static
   state outside the audited sinks could race. The existing
   bridges all funnel through the listed sinks today, but this
   becomes a code-review concern going forward. ADR-0002's
   handler-decomposition contract gets a thread-safety addendum.

## Alternatives considered

- **`Parallel.ForEach` with thread-local accumulators.** Rejected:
  ordering becomes implicit (depends on worker scheduling). The
  index-based buffer is the smallest change that keeps the output
  order stable.
- **Channel-based worker pool.** Rejected: heavier abstraction for
  what is fundamentally a fan-out / fan-in problem with bounded
  work.
- **Lock-free `_diagnostics` (e.g. `ConcurrentBag`).** Rejected:
  `ConcurrentBag` does not preserve order even within a single
  thread; `_diagnostics` becomes user-visible CLI output, so the
  lock-around-`List` form keeps the existing read shape with
  minimal cost.
- **Per-worker `TypeMappingContext` clones merged at the end.**
  Rejected: the merge step is more complex than the
  `ConcurrentDictionary` swap, and the two sinks are write-mostly
  with rare reads — exactly the workload the concurrent collections
  optimize for.

## References

- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`
  (`TransformAll` parallel block)
- `src/Metano.Compiler.TypeScript/Transformation/TypeMappingContext.cs`
- `src/Metano.Compiler.TypeScript/Bridge/IrToTsTypeOverrides.cs`
- ADR-0002 — handler decomposition (referenced for the
  thread-safety contract this builds on top of)
- ADR-0018 — type-level dependency graph (the cache and watch-mode
  PRs that follow this one consume the same parallel loop)
- Issue #21 — incremental compilation + parallel TypeTransformer
- Issue #18 — watch mode
