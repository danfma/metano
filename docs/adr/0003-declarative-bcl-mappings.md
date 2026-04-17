# ADR-0003 — Declarative BCL mappings via `[MapMethod]` / `[MapProperty]`

**Status:** Accepted
**Date:** 2026-02-25

## Context

The original `BclMapper.cs` was ~400 lines of hardcoded string-matching
branches: `if (methodName == "Add" && containingType.StartsWith("System.Collections.Generic.List<"))`.
Every new BCL mapping (`Enumerable.Where`, `Temporal.PlainDate`,
`List<T>.Remove`, …) required editing the mapper source, and external
packages that shipped their own types couldn't extend the mapping without
forking the compiler.

Two pressures made this unsustainable:

1. The BCL surface Metano cares about kept growing (LINQ, immutable
   collections, `Decimal`, `Temporal`, `Guid`, `Queue`/`Stack`,
   `Dictionary.TryGetValue`, …).
2. Third-party type mappings (`decimal` → `Decimal` from `decimal.js`,
   `Guid` → `UUID` from `metano-runtime`) needed to be declared close to
   their types, not hardcoded in the compiler.

## Decision

Replace the hardcoded switch with assembly-level attributes
`[MapMethod]` / `[MapProperty]` living under the `Metano` project in
`Metano/Runtime/`. The compiler walks every referenced assembly's
attributes, indexes them by `(declaringType, memberName)`, and dispatches
BCL → JS lowering through a `DeclarativeMappingRegistry`. `BclMapper.cs`
becomes pure dispatch infrastructure — no string matching, no
type-name branches.

Schema features:

- `JsMethod` / `JsProperty` — simple rename that preserves the receiver.
- `JsTemplate` — full template with placeholders: `$this` (receiver),
  `$0`/`$1`/… (positional arguments), `$T0`/`$T1`/… (generic method
  type-argument names). Expanded as real AST nodes via `TsTemplate`, so
  nested calls, lambdas, and binary operators round-trip cleanly.
- `WhenArg0StringEquals` — literal-argument filter for cases like
  `Guid.ToString("N")` vs. the default `Guid.ToString()`.
- `WrapReceiver` — injects a wrapping call around the receiver
  (`Enumerable.from(x).where(...)`), with generic chain detection so long
  fluent chains only wrap once.
- `RuntimeImports` — declares runtime helper identifiers the template
  body references (e.g. `listRemove`, `immutableInsert`,
  `dayNumber`) so the import collector emits the corresponding
  `import { ... } from "metano-runtime";` line automatically.

Default mappings live under `Metano/Runtime/`, organized by area: Lists,
Strings, Math, Console, Guid, Tasks, Temporal, Enums, Queues, Stacks,
Dictionaries, Sets, Linq — roughly 140 declarations total.

## Consequences

- (+) `BclMapper.cs` shrank from ~400 lines of hardcoded logic to ~340
  lines of dispatch infrastructure. No string matching remains in the
  mapper itself.
- (+) External packages ship their own mappings. Any assembly with
  `[assembly: MapMethod]` declarations is picked up automatically — the
  compiler walks references and indexes them alongside the defaults.
- (+) Mappings are declarative data, reviewable as a list. Diffing a
  mapping change is reading attribute literals, not following control
  flow.
- (+) Version metadata on `[ExportFromBcl(..., Version = "^x.y.z")]` and
  `[Import(..., Version = "^x.y.z")]` composes naturally with the
  auto-generated `package.json#dependencies` merge
  (cf. [ADR-0011](0011-emit-package-ssot.md) — planned).
- (−) Templates are strings with a custom placeholder syntax. Errors
  surface only when the template is actually expanded, not at C# compile
  time. Mitigated by tests for every default mapping.
- (−) Debugging a faulty mapping requires knowing the registry's lookup
  key — `(typeof(List<>), "Add")` — which isn't always obvious from a
  call site.

## Alternatives considered

- **Keep the hardcoded `BclMapper`**: rejected. It didn't scale, and
  external extension was impossible without forking.
- **F#-style quotation-based mapping**: rejected. Would require embedding
  a C# expression parser into the compiler — far more machinery than the
  problem needed.
- **Source-generator-based mapping**: rejected. Source generators add
  build-time complexity and indirection; assembly attributes are simpler
  to write, inspect, and test.

## References

- `src/Metano/Annotations/MapMethodAttribute.cs`,
  `src/Metano/Annotations/MapPropertyAttribute.cs`
- `src/Metano/Runtime/` — all default mappings by area
- `src/Metano.Compiler.TypeScript/Bridge/IrToTsBclMapper.cs`
- `src/Metano.Compiler.TypeScript/Transformation/DeclarativeMappingRegistry.cs`
- `tests/Metano.Tests/IR/IrToTsBclMapperTests.cs`

## Post-refactor note (2026-04)

The decision here — declarative assembly-level mappings driving a
per-compilation registry — is unchanged. The consumer moved from
`BclMapper` (Roslyn-walking) to `IrToTsBclMapper` in
`src/Metano.Compiler.TypeScript/Bridge/`, which takes IR call expressions
and member accesses and renders `TsTemplate` / `TsCallExpression` nodes.
The `DeclarativeMappingRegistry` grew full-name-keyed secondary indices
(`GetStableFullName` / `CSharpErrorMessageFormat`) so the IR lookup is
symbol-free and deterministic. The Dart target will consume the same
registry through its own bridge when per-target templates are added.
See [ADR-0013](0013-shared-ir-as-canonical-semantic-representation.md)
for the wider IR architecture.
