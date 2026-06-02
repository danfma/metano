# Research & Design Decisions: JS-Interop Primitives

**Feature**: `003-js-interop-primitives` | **Date**: 2026-06-01

Decision → Rationale → Alternatives, grounded in the current (pre-002) `main` codebase verified by exploration.

## D1 — `[JsTuple]` recognition & emission (FR-006, FR-007, FR-009)

**Decision**: Mirror the shipped `[PlainObject]` path. Add `IsJsTuple` to `IrTypeSemantics` (populated in `IrClassExtractor.ExtractSemantics` via `SymbolHelper.HasJsTuple`). Add a `TryEmitJsTuple` route in `TypeTransformer.BuildTypeStatements` **before** `TryEmitPlainObjectOrClass`, delegating to a new `IrToTsJsTupleBridge`. Emission:
- **`[JsTuple]` alone** → `export type <Name><...> = [T0, T1, ...]` (a `TsTypeAlias` to a `TsTupleType`, which already exists and prints `[T0, T1]`). The named type is usable in annotations.
- **`[JsTuple, Import(...)]`** → **erased**, no type emitted; the type's annotation resolves to the imported library tuple (e.g. `Signal<T>` from `solid-js`).
- In both cases: no class, no `equals`/`hashCode`/`with` (shape-only, exactly like `[PlainObject]` emits an interface with no class body).

**Rationale**: `[PlainObject]` (object shape) and `[JsTuple]` (array shape) are siblings; cloning the proven path is the lightest correct design (Constitution VI). `TsTupleType` already exists and prints correctly, so the type-alias form is nearly free.

**Alternatives considered**: native ValueTuple as the representation — deferred (D7); a `new`-construction lowering for `[JsTuple]` — unnecessary because a `[JsTuple]` value is produced by a JS factory (`createSignal`), never `new`'d in transpiled code (FR; the producing function is `[Import]`/`[Emit]`).

## D2 — Positional member access on a `[JsTuple]` value (FR-008)

**Decision**: A member access `value.<Member_i>` on a `[JsTuple]`-typed receiver lowers to element access `value[i]`, where `i` is the member's positional declaration index. The extractor (`IrExpressionExtractor`) resolves the accessed member's position and tags the `IrMemberAccess` origin (`IrMemberOrigin.IsJsTupleElement` + `TupleIndex`); `IrToTsExpressionBridge` emits a `TsElementAccess(receiver, i)`.

**Rationale**: Positional records map field order → array index. This is the fallback when a value is not deconstructed (deconstruction, D4, is the idiomatic path). Reuses the existing `IrMemberOrigin` dispatch channel.

**Alternatives**: name-based access only — insufficient (JS array has no named fields).

## D3 — `[JsCallable]` invoke lowering (FR-001, FR-002, FR-003, FR-004)

**Decision**: At extraction, a call to `Invoke` whose declaring type carries `[JsCallable]` is tagged `IrMemberOrigin.IsJsCallableInvoke`. In `IrToTsExpressionBridge.MapCall`, a parallel branch to the existing PlainObject-instance-method path lowers `recv.Invoke(args)` → `TsCallExpression(receiver, args)` (i.e. `recv(args)`), for any arity. Overloaded `Invoke` all map identically (only the argument list differs). A `[JsCallable]` interface is **erased** (no declaration emitted) and composes with `[Import]`/`[External]`.

**Rationale**: An interface can express overloaded call signatures that a delegate cannot; `Invoke` is the conventional .NET call-operation name (delegate `.Invoke`). Reusing the `MapCall` origin channel avoids new machinery and replaces repetitive `[Emit("$0($1)")]` with one declarative marker. The receiver, after destructuring, IS the JS function — so `recv(args)` is correct.

**Alternatives**: per-method `[Emit("$0($1)")]` (status quo — repetitive, error-prone, can't be uniform across arities); a delegate (can't overload).

## D4 — Tuple deconstruction (FR-011, FR-012, FR-013)

**Decision**: Add a target-agnostic IR node `IrTupleDeconstruction(IReadOnlyList<IrDeconstructionElement> Elements, IrExpression Initializer, bool IsConst)`, where each element is a name or a discard. Extraction (`IrStatementExtractor`) handles the C# deconstructing declaration — an `ExpressionStatement` whose assignment Left is a `DeclarationExpressionSyntax` with a `ParenthesizedVariableDesignationSyntax` (elements are `SingleVariableDesignationSyntax` / `DiscardDesignationSyntax`). A new TS AST node `TsDestructuringDeclaration(IReadOnlyList<string?> Names, TsExpression Initializer, bool Const)` (null = discard hole) prints `const [a, , b] = init`. `IrToTsStatementBridge` maps the IR node to it. Discards emit an empty hole; bound names resolve normally thereafter.

**Rationale**: Deconstruction is currently dropped to `IrUnsupportedStatement`. A dedicated IR node (not an overload of `IrVariableDeclaration`'s single `Name`) keeps the common case clean and is reusable by other targets (Dart records). Flat deconstruction only (nested deferred — Out of Scope).

**Alternatives**: extend `IrVariableDeclaration` with an optional pattern — muddies the single-name common path.

## D5 — Attributes & namespace (FR-015)

**Decision**: `JsTupleAttribute` (targets class/struct/record) and `JsCallableAttribute` (targets interface) live in `Metano.Annotations.TypeScript`, with XML docs matching the existing TS-annotation style (`[External]`/`[Optional]`). They are no-ops for non-TypeScript targets.

**Rationale**: `Js`-prefixed, JS-emission-specific; consistent with the documented TS-namespace rationale (a cross-target project opting into `using Metano.Annotations;` does not see TS-only knobs).

## D6 — Diagnostics (FR-005, FR-010)

**Decision**: `MS0027 InvalidJsTuple` — `[JsTuple]` on a type with no positional shape (non-positional record/class). `MS0028 InvalidJsCallable` — `[JsCallable]` on a non-interface, or a `[JsCallable]` interface declaring members other than `Invoke`. Raised in `CSharpSourceFrontend` validation with the Roslyn `Location`.

**Rationale**: Constitution V (no silent failure). **`MS0027`/`MS0028` deliberately skip `MS0026`**, which is reserved by the in-flight JSX feature on branch `002` (not yet merged) — avoids a code collision at merge time.

## D7 — Native `ValueTuple` deferred (FR-016)

**Decision**: Native C# `(T, U)` → TS tuple is out of scope here. The named `[JsTuple]` record covers the motivating need (a nominal, importable tuple type). Native ValueTuple is a future general/cross-target primitive (also maps to Dart 3 records `(a, b)`).

**Rationale**: YAGNI for this iteration; the signal binding needs a *named* type (`Signal<T>`), which `[JsTuple]` provides. Keeps scope tight.

## D8 — Cross-branch coordination (the SolidJS binding lives on branch 002)

**Decision**: This feature (on `003`, branched from `main`) delivers ONLY the primitives + self-contained golden tests. It does **not** create or modify the `bindings/Metano.TypeScript.SolidJs` binding (which exists only on branch `002`) and does **not** touch any 002 artifact. US4 (composition) is validated with a minimal inline `Signal<T>` binding defined in the test source. The actual SolidJS binding rewrite (`ISignal.Value/.Set` → `Signal<T>` deconstruction) and the consumer revalidation are the **dependent 002 reactivity refactor**, performed after `003` merges.

**Rationale**: Keeps `003` a clean, independently-mergeable foundational feature. Avoids entangling two in-flight branches. The composition guarantee is fully testable in isolation with an inline binding.

**Alternatives**: building the real binding here — rejected (it lives on 002; would couple the branches and duplicate/conflict the binding).

## D9 — Ambiguities resolved during analysis (I1, U1)

Two `/speckit-analyze` findings are resolved here before implementation:

- **I1 — `[JsCallable]` erasure**: `[JsCallable]` **implies** no declaration is emitted (it is a callable facade with no TS type of its own). `[External]` is therefore **redundant** on a `[JsCallable]` interface (harmless if present). `TypeTransformer` skips emission for any `[JsCallable]` interface, with or without `[External]`. Examples are normalized to drop the redundant `[External]`.
- **U1 — constructing a `[JsTuple]`**: `new <JsTuple>T(a, b)` lowers to a **TS array literal** `[a, b]` (positional), mirroring how `[PlainObject]` `new T(...)` lowers to an object literal. This covers the case where a binding author builds a tuple directly (not via an `[Import]` factory). No diagnostic needed; the construction is well-defined.

## Resolved unknowns summary

| Unknown | Resolution |
|---------|-----------|
| `[JsTuple]` emission | type alias `= [T0,T1]`, or erased when `[Import]`; mirror `[PlainObject]` (D1) |
| Tuple element access | positional `[i]` via member-origin (D2) |
| `[JsCallable]` lowering | `recv.Invoke(args)`→`recv(args)` via MapCall origin (D3) |
| Deconstruction | new `IrTupleDeconstruction` + `TsDestructuringDeclaration` (D4) |
| Namespace | `Metano.Annotations.TypeScript` (D5) |
| Diagnostics | `MS0027`/`MS0028` (skip `MS0026` = branch 002) (D6) |
| ValueTuple | deferred (D7) |
| SolidJS binding | not here — dependent 002 refactor; inline test binding for US4 (D8) |

No NEEDS CLARIFICATION markers remain. Deferred (non-blocking): nested deconstruction; assignment-deconstruction `(a,b)=e`; foreach deconstruction; native ValueTuple; `.ItemN` on ValueTuples.
