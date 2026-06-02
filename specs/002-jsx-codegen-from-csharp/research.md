# Research & Design Decisions: JSX/TSX from C#

**Feature**: `002-jsx-codegen-from-csharp` | **Date**: 2026-06-01

This document resolves the open design questions surfaced during planning. Each entry is **Decision → Rationale → Alternatives considered**, grounded in the current Metano architecture (verified by reading `Metano.Compiler` IR/extraction, `Metano.Compiler.TypeScript` AST/printer/bridges, and the SolidJS bindings).

## D1 — How is "this lowers to JSX" recognized? (FR-001, FR-007, FR-012, FR-022)

**Decision**: Recognition is **type-driven**, computed in the frontend and carried as metadata on `IrTypeSemantics`:
- `IsJsxComponent` — the type derives (transitively) from a base marked `[JsxComponentBuilder]` and is not itself the abstract base. Such a type is emitted as a **function component** (US1).
- `JsxNativeElementTag` (string?) — the type carries `[JsxNativeElement("tag")]`. A `new T { … }` of such a type lowers to an intrinsic `<tag>` element (US2).
- `RendersAsJsxElement` — the type is JSX-renderable in a value position (a component, a native element, or an `[Import]`-ed renderable typed as/convertible to the marked `JsxElement`). A `new T { … }` of such a type lowers to JSX **component** usage `<T … />` (US3, US5).

A `new T { … }` (or `new T()`) expression lowers to JSX when `T`'s `RendersAsJsxElement` is true. The decision is purely about the **type** of the constructed value.

**Rationale**: The C# `implicit operator JsxElement(JsxComponent)` exists only so the source compiles (a `Counter` is assignable to `JsxElement[] Children`). The transpiler does not need to *observe* the conversion — it needs to know the constructed type is renderable. Type-driven recognition reuses the existing type-semantics channel (`IsPlainObject`/`IsBranded` precedent) and the base-chain walk the frontend already does.

**Alternatives considered**:
- *Conversion-driven dispatch* (detect `op_Implicit` at each cast site): the agent survey confirmed implicit conversions are *extracted but never used for dispatch* (`IrExpressionExtractor.ExtractCast` only special-cases BigInteger/Decimal). Building a general user-defined-conversion dispatch engine is significant new machinery used by nothing else — rejected per Constitution VI (YAGNI). The implicit operator stays a C#-side compile-time affordance only.
- *Marker attribute on every component* (`[JsxComponent]` on `Counter` directly): redundant — the base already carries `[JsxComponentBuilder]`; forcing per-type annotation is needless ceremony.

## D2 — Where does JSX metadata live, given the core must stay target-agnostic? (Constitution IV)

**Decision**: Add `IsJsxComponent` / `JsxNativeElementTag` / `RendersAsJsxElement` to **core** `IrTypeSemantics` (in `Metano.Compiler`). Emission stays entirely in the TS adapter.

**Rationale**: Recognition needs the Roslyn base-type chain + attribute lookup, which only the frontend has. Storing the *result* as metadata is exactly how `IsPlainObject`, `IsBranded`, and `IsException` already work — none of which makes the core depend on a target. The markers live in `Metano.Annotations.TypeScript`, but the *fact* "this type is renderable" is just a boolean the adapter reads. Recorded as a tracked tradeoff in `plan.md` Complexity Tracking.

**Alternatives considered**:
- *Re-resolve symbols in the TS adapter*: would punch through the IR boundary and duplicate symbol analysis in the adapter — a worse violation.
- *A side-table keyed by type name in the TS target*: more moving parts, no benefit over the established semantics-record pattern.

## D3 — Object-initializer support in the IR (FR-008, FR-010)

**Decision**: Extend `IrNewExpression` with an optional ordered list of **member assignments** `(MemberName, EmittedName, Value)` extracted from `oc.Initializer`. The extractor (`ExtractObjectCreation`/`ExtractImplicitObjectCreation` → `BuildNewExpression`) reads the `InitializerExpressionSyntax`, resolves each assigned member's `[Name]` override (via the existing `SymbolHelper.GetNameOverride`) into `EmittedName`, and records the lowered value expression.

**Rationale**: Verified gap — today `ExtractObjectCreation` ignores `oc.Initializer` entirely, so `new Html.Div { ClassName = "x", Children = [...] }` loses everything in the braces. Object initializers are a *general* C# feature; adding them as a first-class IR primitive is the honest fix and unblocks JSX attributes/children. The TS adapter decides, per the target type's `RendersAsJsxElement`, whether to render the assignments as JSX attributes/children or (future) a TS object shape.

**Alternatives considered**:
- *A dedicated `IrJsxElement` node emitted by the frontend*: bakes a TS/JSX concept into the target-agnostic IR — rejected (Constitution IV/VI). Keep the IR describing C# faithfully; let the adapter interpret.

## D4 — Attribute vs child classification (FR-008, FR-010, FR-011)

**Decision**: Within a JSX-typed new-expression's member assignments, the member whose name is `Children` (the `JsxElement[]?`-typed collection on the element base) becomes the element's **children**; every other assignment becomes an **attribute** (name = `EmittedName`). Each child element of `Children` is lowered recursively:
- a JSX-typed `new T { … }` → nested `<…>` element,
- a `Text(literal)` call → `TsJsxText`,
- a `Text(expr)` call → `TsJsxExpressionChild` (`{expr}`),
- a JSX-producing helper call (`Solid.For(...)`) → its mapped JSX element.

Attribute values follow FR-009: a string literal → `TsJsxAttributeStringValue` (`attr="…"`); any other expression → `TsJsxAttributeExpressionValue` (`attr={…}`), including event handlers (`onClick={handler}`).

**Rationale**: Matches the prototype shape directly. `Children` is the single, well-known collection slot on the element base (`Html.Node.Children`), so classification is unambiguous and needs no extra marker.

**Alternatives considered**:
- *A `[JsxChildren]` marker on the property*: unnecessary now (one well-known slot); can be added later if a binding needs a differently-named children slot (tracked as future work, not v1).

## D5 — Component record → function component shape (FR-002, FR-003, FR-004, FR-006)

**Decision**: A JSX component record lowers via a new `IrToTsJsxComponentBridge` (routed from `TypeTransformer.BuildTypeStatements` *before* the class/plain-object branch) to:
- `export type <Name>Props = { <camelProp>?: <TsType>; … }` — one optional member per **settable** property (`init`/`set`) and record positional parameter (per clarification); get-only/computed/`[Ignore]` excluded.
- `export function <Name>(props: <Name>Props) { <hoisted prop locals> <lowered Render() body> }`.
- For each prop **referenced** in the body, hoist `const props$<camel> = props.<camel> ?? <default>;` at the function top, where `<default>` is the C# type default (`0`, `false`, `""`?-no, `null` for reference types) or the property's explicit initializer. References to the prop in the body rewrite to `props$<camel>`.
- The `Render()` body's `return <element>;` lowers to `return <jsx>;`.

`[Name("X")]` on the component renames both the function (`X`) and the props type (`XProps`).

**Rationale**: Mirrors the spec's worked example exactly. Hoisting once (not inlining `props.x ?? d` at every use) keeps output clean and matches the example (`const props$count = props.count ?? 0;`).

**Open implementation detail (deferred to tasks, not blocking)**: the exact default literal per C# type is mechanical (`default(T)` mapping already exists in the type mapper). The `props$` local naming is an internal convention; tests pin it via golden files.

## D6 — SolidJS signal facade elision (FR-015, FR-016)

**Decision**: Refactor the SolidJS binding so `ISignal<T>` is a **pure compile-time facade** over the Solid signal tuple `[Accessor<T>, Setter<T>]`, expressed declaratively:
- `Solid.CreateSignal(value)` → `createSignal(value)`, via `[Import("createSignal", from: "solid-js")]` + `[Emit("createSignal($0)")]` on the public method. The intermediate `SignalWrapper` and `CreateRawSignal` are removed from the emitted surface (the method maps directly).
- `ISignal<T>.Value` (getter) → `$0[0]()` via `[Emit("$0[0]()")]` (or `[MapProperty]`).
- `ISignal<T>.Set(value)` → `$0[1]($1)` and `ISignal<T>.Set(updater)` → `$0[1]($1)` via `[Emit("$0[1]($1)")]` on the methods — **direct form**, value not wrapped in `() => v` (per clarification D-Set).
- `SignalWrapper<T>` is marked so it is **not emitted** (no `.ts` file, no allocation at call sites).

So `var count = Solid.CreateSignal(Count)` → `const count = createSignal(props$count ?? 0)`; `count.Value` → `count[0]()`; `count.Set(count.Value - 1)` → `count[1](count[0]() - 1)`.

**Rationale**: Reuses the shipped `[Emit]`/`[Import]` machinery (no new lowering engine). The current prototype already puts `[Emit("$0[0]()")]`/`[Emit("$0[1]($1)")]` on `SignalWrapper`'s private helpers; this decision relocates those templates onto the `ISignal` member surface that call sites actually touch, and drops the wrapper object. FR-024 explicitly permits refining the bindings when a marker proves insufficient — this is that refinement.

**Risk**: A method-level `[Emit]` that elides a wrapper-allocating method (`CreateSignal` returns `new SignalWrapper(...)`) must take precedence over transpiling the method body. The expression bridge already consults `[Emit]`/`[Import]` before normal call emission (`IrToTsExpressionBridge.MapCall`), so the public `CreateSignal` mapping fires first; the wrapper body is never emitted because the type is marked no-emit. Validated by a golden test in Phase 5.

**Alternatives considered**:
- *Transpile `SignalWrapper` as a real runtime class*: produces a wrapper object in output, violating FR-015 ("no wrapper object allocated") and SC-002.
- *Treat `ISignal` as `[InlineWrapper]`/branded*: the branded machinery targets primitive value wrappers, not a get/set tuple facade — wrong fit.

> **Superseded (2026-06-01)**: D6's `ISignal.Value`/`.Set` facade with relocated `[Emit("$0[0]()")]`/`[Emit("$0[1]($1)")]` index templates is replaced by the feature-`003-js-interop-primitives` composition. The binding is now `[JsTuple, Import("Signal", from: "solid-js")] record Signal<T>(Func<T> Getter, [JsCallable] ISignalSetter<T> Setter)`: `var (count, setCount) = Solid.CreateSignal(0)` deconstructs to `const [count, setCount] = createSignal(0)`, reads are `count()`, and writes are `setCount(v)` / `setCount(c => c + 1)` via the `[JsCallable]` setter — **zero `[Emit]` templates**, no cryptic `count[0]()`/`count[1]()` output. The 002 reactivity refactor that adopts this composition depends on feature 003 landing first. FR-015/FR-016 and `contracts/reactivity-lowering.contract.md` reflect the new shape.

## D7 — `Solid.For` and render-prop children (FR-018)

**Decision**: Map `Solid.For(items, (item, index) => element)` to a JSX element `<For each={items}>{(item, index) => <element>}</For>`, importing `For` from `solid-js`. This is handled in `IrToTsJsxBridge` as a recognized **JSX-producing helper call**: a call whose target is the `For` binding method emits a `TsJsxElement` named `For` with an `each` attribute (first arg) and a single **expression child** holding the lowered lambda (which itself returns JSX).

**Rationale**: `<For>` is a component with a render-prop child — structurally a JSX element whose only child is a function. Modeling it as `TsJsxElement("For", [each={…}], [ {lambda} ])` reuses the JSX node we already need.

**Note (deferred)**: Solid's `<For>` callback receives `index` as an **accessor** (`() => number`), not a bare `number`. The C# lambda types it as `int`. The index-accessor adaptation (call `index()` where the C# code reads `index`) is a known refinement tracked for tasks; the prototype discards both lambda params (`(_, _)`), so v1 golden output is unaffected.

## D8 — `.tsx` file selection (FR-021)

**Decision**: `PathNaming.GetRelativePath(ns, typeName, isJsx)` gains an `isJsx` parameter selecting `.tsx` vs `.ts`. `TypeTransformer.TransformGroup` computes `isJsx` by scanning the produced top-level statements for any `TsJsxElement`/JSX node (a small recursive `ContainsJsx` walk over statements/expressions). Files with no JSX keep `.ts`.

**Rationale**: Single call site owns the extension (verified). Detecting JSX post-lowering (on the TS AST) is reliable and local — no need to thread a flag from the frontend.

## D9 — Diagnostics (FR-024)

**Decision**: Add `MS0026` — *"JSX renderable marker insufficient or unrecognized."* Raised when a type is used in a renderable position but cannot be classified (not a component, not a `[JsxNativeElement]`, not an `[Import]`-ed renderable typed as the marked element), or when a `[JsxComponentBuilder]` base is misapplied (e.g. `Render()` does not return the marked element type). Carries the Roslyn `Location` of the offending expression/declaration. Next free code confirmed as MS0026 (catalog ends at MS0025).

**Rationale**: FR-024 + Constitution V (no silent failure). One code covers the "can't recognize this as JSX" family; sub-cases are distinguished by message text. A second code (MS0027) is reserved only if a clearly distinct condition emerges during implementation.

## D10 — Validation harness (SC-001, SC-005)

**Decision**: Two-layer validation:
1. **TUnit golden tests** (`SolidJsJsxTranspileTests.cs` + `Expected/*.tsx`) using `TranspileHelper.Transpile` for inline single-file cases and `TranspileWithLibrary` for the imported-renderable case (SC-004). The harness already references `Metano.Annotations`; the SolidJS/DOM binding sources are supplied inline or via the binding assemblies.
2. **End-to-end sample**: wire `SampleSolidUi.csproj` with `MetanoOutputDir=../../targets/js/sample-solid-ui/src` + the `Metano.Build` MSBuild import (mirroring `SampleCounterV1`), create the `targets/js/sample-solid-ui` Vite + SolidJS consumer (`jsx: "preserve"`, `jsxImportSource: "solid-js"`), add it to the Bun workspace, and assert `bun run build` + `bun test` pass with zero manual edits to generated `.tsx`.

**Rationale**: Matches the shipped `sample-counter-v*` pattern exactly; reuses MSBuild auto-transpile so `dotnet build` produces the consumer input. Golden tests give fast, precise regression signal; the consumer proves the output actually renders (SC-005).

## Resolved unknowns summary

| Unknown | Resolution |
|---------|-----------|
| Detect "emit as JSX" | Type-driven via `IrTypeSemantics` flags (D1) |
| Core vs adapter placement | Metadata in core IR; emission in TS adapter (D2) |
| Object initializers in IR | New member-assignment list on `IrNewExpression` (D3) |
| Attributes vs children | `Children` slot = children; rest = attributes (D4) |
| Component → function shape | `IrToTsJsxComponentBridge`, hoisted props (D5) |
| Signal wrapper elision | Declarative `[Emit]`/`[Import]` facade, `SignalWrapper` not emitted (D6) |
| `For` render-prop | JSX element with lambda expression child (D7) |
| `.tsx` selection | `isJsx` flag on `PathNaming`, post-lowering scan (D8) |
| Diagnostic identity | `MS0026` (D9) |
| Validation | Golden tests + Vite/SolidJS consumer (D10) |

No NEEDS CLARIFICATION markers remain. Deferred (non-blocking, for `/speckit-tasks`): exact per-type default literals, `props$` naming pinned by golden files, Solid `<For>` index-accessor adaptation, optional second diagnostic code.
