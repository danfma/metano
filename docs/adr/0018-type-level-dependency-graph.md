# ADR-0018 — Type-level dependency graph as the backbone for incremental + watch

**Status:** Accepted
**Date:** 2026-05-07

## Context

Two pipeline-perf items in the backlog (#21 incremental compilation, #18 watch
mode) both need the same answer: "if type T was touched, which types must
regenerate?". The current pipeline lacks any persistent dependency information —
every run reprocesses every transpilable type, which is fine for a sample (~30
types) but scales linearly with project size.

Both features cannot ship without first agreeing on the granularity of "what
depends on what" and where that information lives. Building a separate graph
inside the cache subsystem and another inside the watcher would lead to drift,
slightly different invalidation semantics, and double the maintenance.

## Decision

Introduce `Metano.Compiler.DependencyGraph.IrTypeDependencyGraph` — a single,
type-level dependency graph derived from the Roslyn symbols carried by
`IrCompilation.TranspilableTypeEntries`.

- **Granularity is the type, not the file.** A `.cs` file can declare N types;
  a single type can span N files via `partial`; `[EmitInFile]` collapses N
  types into a single output file. Tracking dependencies per file would lose
  precision (over-invalidation) and per-output-file would lose the connection
  to the C# source. The type FQN — `namespace.Name` from
  `INamedTypeSymbol.OriginalDefinition` — is the stable key.
- **Edges come from Roslyn symbols, not from the IR.** `IrCompilation.Modules`
  is currently empty (the IR is built per-type by the bridges, not eagerly
  populated as a single tree). Walking Roslyn directly avoids waiting for that
  population work and keeps the graph builder independent from per-target
  bridges.
- **Only edges between transpilable types of the current compilation.** BCL
  references, primitives, and types from other assemblies do not produce
  edges because the cache cannot regenerate them from this project. Foreign
  assembly changes invalidate consumers via the cache's separate
  metadata-hash key (planned for #21).
- **Both directions are precomputed.** Forward edges (`DependenciesOf`) drive
  consistency checks; reverse edges (`DependentsOf`) drive incremental
  invalidation. Computing both upfront is O(E), and dirty propagation is then
  a single transitive closure over the reverse graph.

## Consequences

(+) #21 (incremental) and #18 (watch) consume the same graph — invalidation
   semantics stay aligned by construction.
(+) Type-level granularity matches the cache key (per-type compile output) so
   there is no projection step between the graph and the cache.
(+) Roslyn-based discovery means generic-type arguments, base types, and
   nested types all contribute edges through the existing symbol API — no
   custom IR walker to maintain.
(+) Foreign assembly references stay out of the graph, keeping it focused on
   the current project's compilation surface.
(−) The graph rebuilds every run today (cheap — O(types × members)). Caching
   the graph itself for cold-start incremental is a separate concern handled
   in #21's cache file.
(−) Dependencies arising from method bodies (call expressions, generic
   instantiations inside expressions) are not yet tracked because those go
   through the per-target bridges. Conservative: bodies that call into
   another transpilable type without referencing it on the signature surface
   currently miss the edge. Acceptable for the MVP — signature-level
   coverage catches the common API-change-invalidates-consumer case.
(−) `ToDisplayString()` on `OriginalDefinition` is the FQN; cross-language
   targets that need a different identifier will need their own naming
   policy (#210 adjacent concern).

## Alternatives considered

- **Per-file dependency graph.** Rejected: fewer keys but lossy granularity;
  partial classes and `[EmitInFile]` co-location both fall apart.
- **Run discovery during the per-target transform pass.** Rejected: makes
  parallelization harder (each worker would re-discover deps) and forces the
  cache subsystem to know about target-specific output paths.
- **Hand-rolled IR walker over `IrTypeDeclaration`.** Rejected for now: the
  IR is not eagerly populated from `IrCompilation.Modules` — that field
  stays empty by design (see `BuildIrCompilation`). Once the IR migration
  finishes (separate roadmap), the graph builder can switch to walking IR
  instead of Roslyn symbols and gain access to body-level dependencies.

## References

- `src/Metano.Compiler/DependencyGraph/IrTypeDependencyGraph.cs`
- `tests/Metano.Tests/DependencyGraph/IrTypeDependencyGraphTests.cs`
- Issue #18 — watch mode
- Issue #21 — incremental compilation + parallel TypeTransformer
- ADR-0013 — shared IR as canonical semantic representation
