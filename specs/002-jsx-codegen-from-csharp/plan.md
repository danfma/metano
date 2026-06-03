# Implementation Plan: JSX/TSX Code Generation from C# Components

**Branch**: `002-jsx-codegen-from-csharp` | **Date**: 2026-06-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/002-jsx-codegen-from-csharp/spec.md`

## Summary

Lower C# *renderable record components* (records deriving from a `[JsxComponentBuilder]` base whose `Render()` returns the marked `JsxElement` type) into idiomatic SolidJS **TSX function components**. The work splits across four layers: (1) extend the target-agnostic IR + extractor to capture object-initializer member assignments and JSX recognition metadata; (2) add JSX AST nodes + printing + `.tsx` file selection to the TypeScript target; (3) add a `IrToTsJsxComponentBridge` that emits `export function <Name>(props: <Name>Props)` with the `Render()` body lowered and props hoisted; (4) build the SolidJS signal binding on the feature-003 `[JsTuple]`/`[JsCallable]`/tuple-deconstruction primitives so `Signal<T>`/`ISignalSetter<T>` erase to the Solid `[get, set]` tuple (`const [count, setCount] = createSignal(...)`, read `count()`, write `setCount(...)`). Recognition is **type-driven** (the type of a `new T { … }` is JSX-renderable), not conversion-driven, which sidesteps the unused implicit-conversion machinery. SolidJS is the proving target; library-agnostic recognition is validated against an imported type (e.g. `solid-router`). Validated end-to-end by transpiling `samples/SampleSolidUi/` into a new Vite + SolidJS consumer at `targets/js/sample-solid-ui/`, plus TUnit golden tests.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (preview), Roslyn 5.3.0 (Microsoft.CodeAnalysis)

**Primary Dependencies**: Roslyn (semantic model + syntax), ConsoleAppFramework (CLI), TUnit (.NET tests), Bun + Vite + `vite-plugin-solid` + `solid-js` (consumer), CSharpier (format). Also depends on the feature-003 JS-interop primitives (`[JsTuple]`, `[JsCallable]`, tuple deconstruction) — the signal binding is realized on them.

**Storage**: N/A (compiler; reads C# projects, writes `.ts`/`.tsx` files)

**Testing**: TUnit golden-output tests (`dotnet run --project tests/Metano.Tests/`) comparing `filename → content` against `tests/Metano.Tests/Expected/`; `bun test` + `bun run build` on the generated SolidJS consumer

**Target Platform**: Generated code targets browsers via SolidJS (`jsx: "preserve"`, `jsxImportSource: "solid-js"`); the transpiler runs on the .NET 10 SDK (macOS/Linux/Windows)

**Project Type**: Compiler / transpiler (target-agnostic core + per-language adapter). The JSX work touches `Metano.Compiler` (IR/extraction), `Metano.Compiler.TypeScript` (AST/printer/bridges), `bindings/Metano.TypeScript.SolidJs`, and adds a sample consumer under `targets/js/`.

**Performance Goals**: No regression to existing transpile throughput; JSX recognition is per-type metadata, no extra passes over unrelated code. Generated SolidJS output must build under the existing consumer toolchain without manual edits.

**Constraints**: Core (`Metano.Compiler`) MUST NOT depend on the TypeScript target (Constitution IV). Nullable representation stays `T | null = null` except props (optional `name?: T` per spec). All committed C# passes `dotnet csharpier .` with warnings-as-errors. Diagnostics are actionable with a stable `MS00NN` code.

**Scale/Scope**: First vertical slice. Surface exercised by the prototype: one builder base, native HTML elements (`div`/`span`/`button`), component composition, `Solid.CreateSignal` (destructured getter `count()` + `[JsCallable]` setter `setCount(...)`)/`CreateEffect`/`For`/`render`, and one imported renderable type. Control-flow helpers beyond `For`, stores/context/resources, and automatic field-mutation reactivity are explicitly out of scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| **I. Clean Code as the Baseline** | PASS — new bridges/handlers follow the existing one-responsibility-per-file pattern (`Bridge/`, `Transformation/`); CSharpier + warnings-as-errors enforced. JSX-recognition predicates extracted into named helpers (`IsJsxRenderable`, `IsNativeElement`). |
| **II. Expressive, Intention-Revealing Code** | PASS — domain vocabulary (`JsxComponent`, `JsxElement`, `JsxAttribute`, `JsxChild`, `lowering`, `bridge`) used in type/method names. New AST records make the JSX shape explicit (illegal states unrepresentable). |
| **III. Screaming, Feature-Semantic Organization** | PASS — JSX AST nodes co-located under `TypeScript/AST/` alongside peers; JSX lowering in a dedicated `Bridge/IrToTsJsxComponentBridge.cs` + `Bridge/IrToTsJsxBridge.cs`. No new `Helpers/`/`Utils/` buckets. |
| **IV. Clean Architecture via Ports & Adapters** | PASS (with one tracked tradeoff) — JSX **recognition metadata** lives in core `IrTypeSemantics` (mirrors existing `IsPlainObject`/`IsBranded` precedent: metadata, not a code dependency on the TS target). All JSX **emission** lives in the TS adapter. Core gains no reference to `Metano.Compiler.TypeScript`. See Complexity Tracking. |
| **V. Developer Experience First** | PASS — single `dotnet build` of the sample transpiles via the existing `Metano.Build` MSBuild target; `bun run build`/`bun test` on the consumer. New diagnostic `MS0026` is actionable (FR-024). Golden tests ship with the behavior. |
| **VI. Pragmatism Over Dogma** | PASS — recognition is type-driven (reuse type semantics) rather than building an implicit-conversion dispatch engine that nothing else needs (YAGNI). Object-initializer support is added as the honest general primitive, not a JSX-only hack. |

**Spec traceability**: This feature is FR-001…FR-024 / SC-001…SC-006 in `specs/002-jsx-codegen-from-csharp/spec.md`. It is a new capability (UI components) not present in the baseline; per CLAUDE.md it is tracked here as the source-of-truth spec for the work. A baseline capability-matrix entry is added when the feature lands.

**Gate result**: PASS. One justified deviation recorded in Complexity Tracking (JSX metadata in core IR).

## Project Structure

### Documentation (this feature)

```text
specs/002-jsx-codegen-from-csharp/
├── plan.md              # This file
├── spec.md              # Feature specification (with Clarifications)
├── research.md          # Phase 0 — design decisions + rationale
├── data-model.md        # Phase 1 — IR + AST entities and their fields
├── quickstart.md        # Phase 1 — how to build/run/validate the slice
├── contracts/           # Phase 1 — input→output lowering contracts (golden shapes)
│   ├── component-lowering.contract.md
│   ├── native-element-lowering.contract.md
│   ├── reactivity-lowering.contract.md
│   └── diagnostics.contract.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (repository root)

```text
src/Metano/Annotations/TypeScript/
├── JsxComponentBuilderAttribute.cs   # exists: [JsxComponentBuilder], IJsxComponentBuilder<,>, JsxNativeElement
└── (no new attributes expected; reuse [Name], [Import], [Emit], [External])

src/Metano.Compiler/                            # target-agnostic core
├── IR/IrExpression.cs                          # EXTEND IrNewExpression with object-initializer member assignments
├── IR/IrTypeSemantics.cs                       # ADD IsJsxComponent / JsxNativeElementTag / RendersAsJsxElement
├── Extraction/IrExpressionExtractor.cs         # EXTEND ExtractObjectCreation to read oc.Initializer
├── Extraction/IrClassExtractor.cs              # EXTEND ExtractSemantics for JSX flags
├── SymbolHelper.cs                             # ADD HasJsxComponentBuilder / GetJsxNativeElementTag / IsJsxRenderable
└── Diagnostics/MetaSharpDiagnostic.cs          # ADD MS0026 (JSX marker insufficient / unrecognized renderable)

src/Metano.Compiler.TypeScript/                 # TypeScript adapter
├── TypeScript/AST/TsJsxElement.cs              # NEW JSX AST nodes
├── TypeScript/AST/TsJsxAttribute.cs            # NEW
├── TypeScript/AST/TsJsxChild.cs                # NEW
├── TypeScript/Printer.cs                       # EXTEND PrintExpression dispatch + Print Jsx* helpers
├── Transformation/PathNaming.cs                # EXTEND GetRelativePath with isJsx → .tsx
├── Transformation/TypeTransformer.cs           # ADD TryEmitJsxComponent route + JSX-in-file detection
├── Transformation/ImportCollector.cs           # EXTEND to walk JSX nodes for referenced names
├── Bridge/IrToTsJsxComponentBridge.cs          # NEW: component record → function + Props type
├── Bridge/IrToTsJsxBridge.cs                   # NEW: new-with-initializer (JSX-typed) → TsJsxElement; helper-call JSX (For)
└── Bridge/IrToTsExpressionBridge.cs            # EXTEND MapNewExpression to route JSX-typed initializers

bindings/Metano.TypeScript.SolidJs/             # signal binding on feature-003 primitives (FR-015/016)
├── Signal.cs / ISignalSetter.cs / Solid.cs     # [JsTuple] Signal<T> + [JsCallable] setter; erase to the Solid [get, set] tuple
├── Solid.Ui.cs                                 # For helper → JSX <For> mapping marker
└── Routing/ (NEW, optional)                    # one imported renderable type for SC-004 (e.g. a solid-router component stub)

samples/SampleSolidUi/
└── SampleSolidUi.csproj                        # ADD MetanoOutputDir → targets/js/sample-solid-ui/src + Metano.Build import

targets/js/sample-solid-ui/                      # NEW Vite + SolidJS consumer (mirror sample-counter-v1)
├── package.json / vite.config.ts / tsconfig.app.json / index.html
└── src/ (generated .tsx) + test/ (bun:test)

tests/Metano.Tests/
├── SolidJsJsxTranspileTests.cs                 # NEW golden tests (US1–US5)
└── Expected/*.tsx                              # NEW expected outputs
```

**Structure Decision**: Follows the established ports-and-adapters layout. The IR/extraction/diagnostics changes are additive metadata in `Metano.Compiler` (core); all JSX emission is in `Metano.Compiler.TypeScript` (adapter). The sample/consumer mirrors the existing `sample-counter-v*` pattern (MSBuild `MetanoOutputDir` auto-transpile + Bun workspace). No new top-level project is introduced.

## Phasing (implementation order, mapped to spec user stories)

The implementation is sliced so each phase is independently testable and builds on the prior, matching the spec's priorities. Detailed tasks come from `/speckit-tasks`; this is the dependency skeleton.

1. **Foundation — object-initializer IR + JSX AST/printer + `.tsx` selection** (enables US1/US2). Add object-initializer member assignments to `IrNewExpression` + extractor; add `TsJsxElement`/`TsJsxAttribute`/`TsJsxChild` + printer; wire `.tsx` selection. No behavior change for non-JSX code (regression gate: existing golden tests stay green).
2. **US1 (P1) — component record → TSX function**. `IrToTsJsxComponentBridge`: recognition via `IrTypeSemantics.IsJsxComponent`, `export function`/`<Name>Props`, props hoisting + default application, `Render()` body lowering, JSX return. `[Name]` honored.
3. **US2 (P2) — native element lowering**. JSX-typed `new T { … }` → `<tag …>` with attribute-name mapping (camelCase + `[Name]` override), `Children` → nested children, `Text(...)` → text/expression child, self-closing.
4. **US3 (P2) — component composition**. Component-record `new T { … }` in renderable position → `<Name … />`; render-entry lambda → JSX.
5. **US4 (P2) — SolidJS reactivity mapping + signal binding on feature-003 primitives**. `CreateSignal`→`createSignal` (destructured `[get, set]`), getter `count()`, `[JsCallable]` setter `setCount(...)`, `CreateEffect`, `For`→`<For>`, `render`; `Signal<T>`/`ISignalSetter<T>` erased.
6. **US5 (P3) — library-agnostic recognition + diagnostic**. Imported renderable type recognized (`[Import]`); `MS0026` for insufficient markers.
7. **Validation — sample + consumer + golden tests**. Wire `SampleSolidUi` to transpile; create `targets/js/sample-solid-ui` consumer; golden tests per US; `bun run build` + `bun test` green (SC-001/005).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| JSX recognition flags (`IsJsxComponent`, `JsxNativeElementTag`, `RendersAsJsxElement`) added to core `IrTypeSemantics` | Recognizing "derives from a `[JsxComponentBuilder]` base" and "this type's `new` lowers to JSX" requires resolving the base-type chain and attributes, which only the Roslyn-aware **frontend** has. The IR is the boundary the TS adapter consumes. | Re-resolving Roslyn symbols inside the TS adapter would push semantic analysis past the IR boundary and duplicate symbol logic in the adapter — a worse Constitution-IV violation. Storing booleans/strings as metadata mirrors the shipped `IsPlainObject`/`IsBranded`/`IsException` precedent and adds **no code dependency** from core to the TS target. |
| `IrNewExpression` gains object-initializer member assignments | `new T { Prop = v, Children = [...] }` is currently dropped by the extractor; JSX attributes/children require these assignments. | A JSX-only IR node would bury a general C# primitive (object initializers) inside a TS/JSX-specific shape, blocking reuse by other targets/features. Adding the general primitive is the honest, lighter choice (Constitution VI). |
