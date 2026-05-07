# Dart/Flutter Target Roadmap

Tracks the work that brings the Dart/Flutter backend up to the maturity of the
TypeScript backend. Updated as each phase lands so a future contributor can
read it as a snapshot of where the target stands today.

## Status

| Phase | Scope | State |
| ----- | ----- | ----- |
| 1 | Runtime foundation: `metano_runtime` package with deterministic `HashCode` (xxHash32), `deepEquals` / `deepHashCode`, console helpers | ✅ landed |
| 2 | Structured `DartImportCollector` wired to `IrRuntimeRequirementScanner` (`HashCode` etc.) | ✅ landed |
| 3 | `hashCode` codegen routed through runtime (`HashCode.combine[N]` / builder), replacing the broken `Object.hash` emission | ✅ landed |
| 4 | Target-aware `[MapMethod]` / `[MapProperty]` (`Dart*` siblings of `Js*`) + Dart `IrToDartBclMapper` + `DartIrRewriter`. First end-to-end mapping: `Console.WriteLine` → `print` | ✅ landed (rename form only — template form pending) |
| 5a | Delegates → Dart `typedef` | ✅ landed |
| 5b | `[ModuleEntryPoint]` → top-level `void main()` | ⏳ pending |
| 5c | Exception types → `class FooException implements Exception` | ⏳ pending |
| 5d | `[GenerateGuard]` → top-level `bool isFoo(dynamic)` | ⏳ pending |
| 5e | Overload dispatcher (Dart-specific design — Dart has no method overloading) | ⏳ pending |
| 5f | `[PlainObject]` shape verification | ⏳ pending |
| BCL | Surface expansion: `string`, `List<T>`, `Dictionary`, `Math`, LINQ extensions, etc. — additive, data-only | ⏳ pending |
| Body | `IrBodyPrinter` (Dart) full coverage (records, lambdas, switch expressions, pattern matching, exception handling) | ⏳ pending — emits placeholder comments today |
| Runtime | LINQ `Enumerable` / `Grouping` port to `metano_runtime` | ⏳ pending |
| Spike | ADR for `build_runner` viability | ⏳ pending |

## Architectural decisions captured along the way

- **No implicit base class for generated types.** The first iteration injected
  `extends MetanoObject` on every record and shipped a `MetanoObject` runtime
  class. That was reverted (`refactor(dart): drop MetanoObject auto-injection
  and runtime base`) before merging — generated records sit directly under
  Dart's `Object`. Reasoning: native interop, no concrete `is MetanoObject`
  consumer today, and the dispatchers planned for Phase 5e use specific
  runtime guards (`isInt32`, `isString`, …) rather than a marker base.

- **Target-aware mapping attributes.** `[MapMethod]` / `[MapProperty]` keep
  their `Js*` properties for the TypeScript backend and add `Dart*` siblings
  (`DartMethod` / `DartTemplate` / `DartRuntimeImports`). A single attribute
  can declare both. Frontend extraction stores the per-target data on the same
  `DeclarativeMappingEntry`; each backend filters by `HasJsMapping` /
  `HasDartMapping` so an entry that only carries Dart data is invisible to TS,
  and vice-versa.

- **`DeclarativeMappingRegistry` lives in the core.** It used to live under
  `Metano.Compiler.TypeScript/Transformation/`. Moving it to
  `Metano.Compiler/Transformation/` lets both targets consume the same factory
  without one transpiler depending on the other.

- **Dart has no JS-style `this` rebinding.** `[This]`-attributed delegate
  parameters degrade to a plain positional receiver named `self` (or whatever
  the formatter emits — see the dead-name note in `IrToDartDelegateBridge`).
  Dispatchers will not have a `[This]`-aware path on the Dart side.

## Validation flow

1. `dotnet build Metano.slnx` — solution builds.
2. `dotnet run --project tests/Metano.Tests/` — full suite green (Dart-specific
   golden tests in `DartBackendTests.cs`).
3. `dotnet run --project src/Metano.Compiler.Dart/ -- -p samples/SampleCounterV1/SampleCounterV1.csproj -o targets/flutter/sample_counter/lib/sample_counter --clean`
   — regenerates the Counter sample.
4. `cd targets/flutter/sample_counter && flutter analyze` — analyzer clean.
5. `flutter test` (when the sample gains tests) — runtime passes.

## Out of scope until later

- Full method-body rendering (Phase 6 in the original split).
- Sample app exercising LINQ + records + switch expressions to validate
  Phases 5b–5f end-to-end. Add when at least 5b–5d have landed.
