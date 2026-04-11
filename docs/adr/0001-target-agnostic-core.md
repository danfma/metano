# ADR-0001 — Target-agnostic core + per-target projects

**Status:** Accepted
**Date:** 2026-02-15

## Context

Metano started as a single project that transpiled annotated C# into
TypeScript. `TypeTransformer` had grown to 2826 lines mixing AST
construction with TypeScript-specific emission, and the vision had expanded
beyond TS: Dart (for Flutter), Kotlin (for native Android), and a
JSX/TSX plugin variant of the TypeScript target were all on the roadmap.
Adding a new target to the existing structure would have required either
forking the transformer wholesale or threading a `target` parameter through
every transformation site — both roads lead to parallel God Objects.

## Decision

Split the transpiler into two kinds of project:

- **`Metano.Compiler`** — target-agnostic core. Owns `ITranspilerTarget`,
  `TranspilerHost`, `MetanoDiagnostic`, the subset of `SymbolHelper` that
  doesn't know about TS, project loading, Roslyn compilation orchestration,
  and file writes.
- **`Metano.Compiler.{Target}`** — one project per target language.
  Implements `ITranspilerTarget`, owns its own AST + Printer + CLI tool
  (e.g. `metano-typescript`), and all target-specific transformers and
  handlers.

The core never knows which targets exist. `TranspilerHost.RunAsync(options,
target)` takes the target as a dependency, loads the project via Roslyn,
and hands the compilation to `target.Transform`. Each future target is a
new project that references `Metano.Compiler` but never touches it or any
other target.

## Consequences

- (+) Adding a target = new project + new AST + new handlers. Zero edits to
  the core or to sibling targets.
- (+) Core improvements (parallel compilation, richer diagnostics,
  incremental caching) benefit every target automatically.
- (+) CLI naming is predictable and discoverable: `metano-typescript`,
  `metano-dart`, `metano-kotlin`.
- (+) Handlers stay in the target project, so the core stays small and
  target-agnostic forever (no leakage of TS-isms into shared code).
- (−) Cross-target refactorings have more friction: moving a utility from
  one target into the core means creating a target-neutral abstraction,
  which is sometimes non-trivial.
- (−) Contributors must understand the core/target boundary before
  adding code — "where does this go?" is a new question.

## Alternatives considered

- **Monolithic project with `switch (target)` branches**: rejected. The
  branches would spread through every handler and make the code
  unreviewable.
- **Core + pluggable targets via assembly loading at runtime**: rejected.
  Over-engineered for current scale — targets are compiled in, and
  referencing the target project from a CLI entry point is enough.

## References

- `src/Metano.Compiler/ITranspilerTarget.cs`
- `src/Metano.Compiler/TranspilerHost.cs`
- `src/Metano.Compiler.TypeScript/TypeScriptTarget.cs`
- `src/Metano.Compiler.TypeScript/Commands.cs` (the `metano-typescript` CLI)
- Related: [ADR-0002](0002-handler-decomposition.md) — the target project's
  internal decomposition that this split unlocked.
