# Claude Task: Metano.Compiler Reorganization Review

You are reviewing the Metano repository on branch `refactor/reorganization`.
Be critical and concrete. The goal is not to perform a broad rename for its own
sake, but to decide whether the compiler/core namespaces and documentation
structure should be reorganized, and to propose an incremental plan that keeps
the project shippable.

## Repository Context

Metano is a C#-to-TypeScript transpiler with an experimental Dart/Flutter
backend. The current architecture is centered on:

- `src/Metano`: public annotations and runtime mapping declarations.
- `src/Metano.Compiler`: target-agnostic compiler core.
- `src/Metano.Compiler.TypeScript`: TypeScript target.
- `src/Metano.Compiler.Dart`: Dart target.
- `src/Metano.Build`: MSBuild integration.

The core currently has these notable folders/namespaces:

- `Metano.Compiler.IR`: shared intermediate representation records.
- `Metano.Compiler.Extraction`: Roslyn/C# -> IR extraction.
- `Metano.Compiler.Diagnostics`: diagnostic model.
- `Metano.Compiler.DependencyGraph`: type dependency graph.
- `src/Metano.Compiler/Transformation/DeclarativeMappingRegistry.cs`, but the
  file declares `namespace Metano.Transformation;`, which appears inconsistent
  with the core project namespace and overlaps with TypeScript target
  transformation namespaces.

The TypeScript and Dart projects both consume `Metano.Compiler.IR`. Many
classes outside `IR` are named `Ir*` because they extract, scan, map, or bridge
the IR. Do not assume every `Ir*` class belongs inside the `IR` namespace.

## Initial Observations To Validate

1. `Metano.Compiler.IR` seems architecturally justified as a boundary: it is
   the target-agnostic contract between source frontend(s) and target backends.
   The core IR records should probably not be scattered into
   `Extraction`, `Diagnostics`, or `Transformation`.

2. Some contents of `IR` are not pure IR:
   - `IrTranspilableTypeEntry` carries Roslyn `INamedTypeSymbol`.
   - `IrEntryPointInfo` carries Roslyn `IMethodSymbol` and `INamedTypeSymbol`.
   - `IrCompilation` includes transitional fields whose comments call them
     escape hatches while targets still depend on Roslyn.
   These may belong in a transitional Roslyn/frontend bridge namespace, or they
   may need to stay until the IR migration is complete. Evaluate carefully.

3. `DeclarativeMappingEntry` lives in `IR`, but it carries target-specific
   names (`JsName`, `JsTemplate`, `DartName`, `DartTemplate`). Decide whether
   this is acceptable shared mapping metadata, a leaky IR concern, or should
   move into a clearer namespace such as `Metano.Compiler.Mappings`.

4. `IrEqualityClassifier` lives in `IR` but is behavior/analysis over IR,
   consumed by runtime-requirement scanning and TypeScript record synthesis.
   Decide whether helper behavior should remain beside the IR model or move to
   `Analysis` / `Semantics`.

5. `IrRuntimeRequirementScanner` is under `Extraction`, but it scans IR
   declarations after extraction. It may fit better under `Analysis` or
   `Semantics`.

6. `IrTypeDependencyGraph` is under `DependencyGraph` and currently walks
   Roslyn symbols from `IrCompilation.TranspilableTypeEntries`, not populated
   `IrModule`s. The namespace is probably acceptable, but evaluate whether it
   should become `Metano.Compiler.Analysis`.

## Documentation Context

The repository currently has:

- `README.md`: product overview and quickstart.
- `docs/`: user/contributor guides and architecture notes.
- `docs/adr/`: architecture decision records.
- `spec/`: formal product specification.
- No `plans/` directory before this task.

Current docs structure is mostly reasonable but uneven:

- `spec/README.md` says `spec/` is normative product requirements and stable
  context for agents.
- `docs/README.md` says `docs/` is explanatory documentation from usage to
  compiler internals.
- `docs/adr/` is historical decision context.
- `docs/better_flutter_support_plan.md` is a roadmap/execution plan living
  inside `docs/`; consider moving active plans/roadmaps into `plans/` or
  `docs/roadmap/`.
- Some docs are stale after recent work:
  - `docs/architecture.md` and `spec/04-functional-requirements.md` still refer
    to diagnostics as `MS0001`-`MS0008`, while `spec/10-diagnostic-catalog.md`
    lists `MS0001` through `MS0022`.
  - ADRs intentionally preserve historical text, but some code pointers now
    reference moved/deleted files, e.g.
    `src/Metano.Compiler.TypeScript/Transformation/DeclarativeMappingRegistry.cs`
    and `src/Metano.Compiler.TypeScript/Transformation/BclMapper.cs`.
  - `docs/architecture.md` says the active `ITranspilerTarget` discovers
    transpilable types, but `CSharpSourceFrontend` now owns part of that
    discovery through `IrCompilation.TranspilableTypeEntries`.

## Task

Please review the codebase and documentation, then produce a concise but
high-signal recommendation.

Answer these questions:

1. Should `Metano.Compiler` be reorganized by feature/responsibility? If yes,
   what should the target namespace/folder layout be?

2. What should happen to `Metano.Compiler.IR`?
   - Which files should definitely stay there?
   - Which files are suspect and should move later?
   - Which files should move now because the risk is low and the current
     namespace is actively misleading?

3. Should the namespace be `IR`, `Ir`, or something else? Consider .NET naming
   conventions, existing public API churn, and readability.

4. How should the project treat Roslyn escape hatches during the frontend/IR
   migration? Propose a naming/location strategy that makes their temporary
   nature obvious without breaking the current targets.

5. What should happen to `DeclarativeMappingRegistry`,
   `DeclarativeMappingEntry`, `IrRuntimeRequirementScanner`,
   `IrEqualityClassifier`, and `IrTypeDependencyGraph`?

6. How should documentation be reorganized?
   - What belongs in `spec/`?
   - What belongs in `docs/`?
   - What belongs in `docs/adr/`?
   - Should a new `plans/` or `roadmap/` area exist?
   - Which current files should move, stay, or be updated?

7. Propose an incremental implementation sequence with small PR-sized steps.
   Prioritize low-risk fixes first, then architectural cleanup, then broader
   moves. Include tests/build commands to run after each phase.

## Constraints

- Avoid a broad mechanical namespace rename unless it has clear payoff.
- Preserve the current TypeScript and Dart targets.
- Do not break public user-facing annotations in `Metano`.
- Prefer codebase-local naming and existing architecture over theoretical
  purity.
- Treat ADRs as historical records: update indexes or add notes when needed,
  but do not rewrite old decisions as if they were made today.
- Keep `spec/` normative and product-facing; do not turn it into an
  implementation scratchpad.

## Expected Output

Return:

- A recommended target layout.
- A table of proposed moves/renames with risk level.
- Documentation taxonomy and proposed file moves.
- A phased plan with validation commands.
- Any strong objections to the premise, if you think some reorganization would
  make the project worse.
