# Implementation Plan: JS-Interop Foundational Primitives (`[JsTuple]`, `[JsCallable]`, Tuple Deconstruction)

**Branch**: `003-js-interop-primitives` | **Date**: 2026-06-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-js-interop-primitives/spec.md`

## Summary

Add three declarative TypeScript-target primitives that let binding authors model JS array-tuples and callable values without hand-written `[Emit]` templates: (1) `[JsTuple]` — a positional record lowered to a JS array-tuple (the array-shape sibling of `[PlainObject]`); (2) `[JsCallable]` — an erased interface whose `Invoke(...)` calls lower to direct receiver invocation, with overloaded `Invoke` (which delegates cannot express); (3) tuple deconstruction `var (a, b) = expr` → `const [a, b] = expr`. The work mirrors the shipped `[PlainObject]` lowering path for `[JsTuple]`, threads a new `Invoke`-call origin for `[JsCallable]`, and adds the first destructuring-binding AST node + extraction. Native `ValueTuple` mapping is out of scope (deferred). Validated by golden tests, including a self-contained `Signal<T>` binding proving the three compose into idiomatic `const [count, setCount] = createSignal(0); count(); setCount(v)` with zero `[Emit]`. The real SolidJS binding migration + consumer revalidation is the dependent 002 reactivity refactor (post-merge), NOT this feature.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (preview), Roslyn 5.3.0

**Primary Dependencies**: Roslyn (semantic model + syntax), TUnit (.NET golden tests), CSharpier

**Storage**: N/A (compiler)

**Testing**: TUnit golden-output tests (`dotnet run --project tests/Metano.Tests/`) against `tests/Metano.Tests/Expected/`; inline-compiled C# via `TranspileHelper`

**Target Platform**: Generated TypeScript; transpiler runs on .NET 10 SDK

**Project Type**: Compiler / transpiler (target-agnostic core + TypeScript adapter)

**Performance Goals**: No regression to transpile throughput; recognition is per-type/per-member metadata, no extra whole-program passes

**Constraints**: Core (`Metano.Compiler`) MUST NOT depend on the TypeScript target. `[JsTuple]`/`[JsCallable]` are TS-specific (no-op for other targets). All committed C# passes `dotnet csharpier format .` with warnings-as-errors. Diagnostics use stable `MS00NN` codes — **`MS0027`+** here (`MS0026` is reserved by the in-flight JSX feature on branch `002`).

**Scale/Scope**: Three primitives + their extraction/lowering/printing + diagnostics + golden tests. The motivating consumer (`Signal<T>`) is exercised by a self-contained test binding. Does NOT modify branch-002 artifacts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| **I. Clean Code as the Baseline** | PASS — new bridge/handlers follow the one-responsibility-per-file convention (`Bridge/`, `Transformation/`); CSharpier + warnings-as-errors. Recognition predicates named (`HasJsTuple`, `IsJsCallableInvoke`). |
| **II. Expressive, Intention-Revealing Code** | PASS — domain vocabulary (`JsTuple`, `JsCallable`, `tuple`, `deconstruction`, `lowering`, `bridge`). New AST nodes make the destructuring shape explicit. |
| **III. Screaming, Feature-Semantic Organization** | PASS — `[JsTuple]` lowering in a dedicated `Bridge/IrToTsJsTupleBridge.cs` mirroring `IrToTsPlainObjectBridge`; attributes co-located in `Annotations/TypeScript/`. No catch-all buckets. |
| **IV. Clean Architecture via Ports & Adapters** | PASS (one tracked tradeoff) — `IsJsTuple` recognition metadata lives in core `IrTypeSemantics` (mirrors `IsPlainObject`/`IsBranded` precedent: metadata, not a code dependency on the TS target). `[JsCallable]` lowering + `[JsTuple]` emission live in the TS adapter. Tuple-deconstruction IR is target-agnostic (Dart could consume it). Core gains no reference to the TS target. See Complexity Tracking. |
| **V. Developer Experience First** | PASS — single `dotnet run` test command; new diagnostics `MS0027`/`MS0028` are actionable; golden tests ship with the behavior; removes hand-written `[Emit]` templates (improves binding-author DX). |
| **VI. Pragmatism Over Dogma** | PASS — mirrors the existing `[PlainObject]` path instead of inventing new machinery; `[JsCallable]` reuses the call-origin channel; native ValueTuple deferred (no speculative generality). |

**Spec traceability**: FR-001…FR-016 / SC-001…SC-005 in `specs/003-js-interop-primitives/spec.md`. New capability; a baseline capability-matrix + attribute-catalog + diagnostic-catalog entry is added when it lands.

**Gate result**: PASS. One justified deviation in Complexity Tracking (JSX-adjacent metadata in core IR — same precedent as `[PlainObject]`).

## Project Structure

### Documentation (this feature)

```text
specs/003-js-interop-primitives/
├── plan.md              # This file
├── spec.md              # Feature specification (with Clarifications)
├── research.md          # Phase 0 — design decisions + rationale
├── data-model.md        # Phase 1 — IR + AST additions
├── quickstart.md        # Phase 1 — build/run/validate
├── contracts/           # Phase 1 — input→output lowering contracts
│   ├── jstuple-lowering.contract.md
│   ├── jscallable-lowering.contract.md
│   ├── deconstruction-lowering.contract.md
│   ├── signal-composition.contract.md
│   └── diagnostics.contract.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/Metano/Annotations/TypeScript/
├── JsTupleAttribute.cs            # NEW — [JsTuple] (class/record target)
└── JsCallableAttribute.cs         # NEW — [JsCallable] (interface target)

src/Metano.Compiler/                                  # target-agnostic core
├── IR/IrTypeSemantics.cs                             # ADD IsJsTuple
├── IR/IrExpression.cs                                # ADD IrMemberOrigin flags: IsJsCallableInvoke, IsJsTupleElement (+ element index)
├── IR/IrStatement.cs                                 # ADD IrTupleDeconstruction (+ IrDeconstructionElement)
├── Extraction/IrClassExtractor.cs                    # ExtractSemantics: IsJsTuple
├── Extraction/IrStatementExtractor.cs                # extract `var (a,b)=e` deconstruction declaration
├── Extraction/IrExpressionExtractor.cs               # member access → tuple-element origin; Invoke → JsCallable origin
├── SymbolHelper.cs                                   # HasJsTuple, HasJsCallable, IsJsCallableInvoke, tuple-element-index helpers
└── Diagnostics/MetaSharpDiagnostic.cs                # ADD MS0027 (InvalidJsTuple), MS0028 (InvalidJsCallable)

src/Metano.Compiler.TypeScript/                       # TypeScript adapter
├── Bridge/IrToTsJsTupleBridge.cs                     # NEW — mirrors IrToTsPlainObjectBridge (tuple type alias / erased when [Import])
├── Bridge/IrToTsExpressionBridge.cs                  # MapCall: Invoke→direct call; member access on JsTuple→element index
├── Bridge/IrToTsStatementBridge.cs                   # lower IrTupleDeconstruction → destructuring declaration
├── Transformation/TypeTransformer.cs                 # TryEmitJsTuple route (before PlainObject/class); skip emission for [JsCallable]/[Import] [JsTuple]
├── TypeScript/AST/TsDestructuringDeclaration.cs      # NEW — const [a, , b] = init (array binding pattern; holes for discards)
└── TypeScript/Printer.cs                             # print TsDestructuringDeclaration; (TsTupleType already prints)

tests/Metano.Tests/
├── JsTupleTranspileTests.cs / JsCallableTranspileTests.cs / DeconstructionTranspileTests.cs / SignalCompositionTests.cs   # NEW
└── Expected/*.ts                                     # NEW expected outputs (inline minimal Signal binding)
```

**Structure Decision**: Ports-and-adapters preserved. `[JsTuple]` clones the `[PlainObject]` lowering path (`IrToTsJsTupleBridge` ⟷ `IrToTsPlainObjectBridge`, `TryEmitJsTuple` ⟷ `TryEmitPlainObjectViaIr`). `[JsCallable]` reuses the `MapCall` origin-dispatch channel (parallel to the PlainObject-instance-method path). Tuple deconstruction adds the first destructuring-binding AST node. No new top-level project. **No branch-002 file is touched.**

## Phasing (implementation order, mapped to user stories)

Detailed tasks come from `/speckit-tasks`; this is the dependency skeleton.

1. **Attributes + core recognition** (enables US1/US2). Add `[JsTuple]`/`[JsCallable]` attribute classes; `IsJsTuple` on `IrTypeSemantics`; `SymbolHelper` predicates; `MS0027`/`MS0028` constants. No behavior change yet (regression gate: existing golden tests green).
2. **US2 (P1) — `[JsTuple]`**. `IrToTsJsTupleBridge` (tuple type alias when standalone; erased when `[Import]`), `TryEmitJsTuple` route, suppress synthesized members, member access → `[i]`, `MS0027` for non-positional misuse.
3. **US1 (P1) — `[JsCallable]`**. `Invoke`-call origin in extraction; `MapCall` lowers `recv.Invoke(args)` → `recv(args)`; erased interface (no emission); `MS0028` for misuse.
4. **US3 (P2) — tuple deconstruction**. `IrTupleDeconstruction` IR + extraction of `var (a,b)=e` (incl. discards); `TsDestructuringDeclaration` AST + printer; reference resolution to destructured names.
5. **US4 (P2) — composition**. Self-contained `Signal<T>` test binding (`[JsTuple, Import] record Signal<T>(Func<T>, [JsCallable] ISignalSetter<T>)`) → golden proving `const [count, setCount] = createSignal(0); count(); setCount(v); setCount(c=>c+1)` with zero `[Emit]`.
6. **Polish**. Baseline capability-matrix + attribute-catalog + diagnostic-catalog entries; dual-agent review (`compiler-man` + `bob`); `dotnet csharpier format .`; quickstart validation.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `IsJsTuple` flag added to core `IrTypeSemantics` | Recognizing `[JsTuple]` and routing emission needs a type-semantics flag the TS adapter reads at the IR boundary; only the frontend resolves the attribute. | Re-resolving Roslyn symbols in the TS adapter breaks the IR boundary and duplicates symbol logic. A boolean mirrors the shipped `IsPlainObject`/`IsBranded`/`IsException` precedent and adds no core→target code dependency. |
| New `IrTupleDeconstruction` IR node | `var (a,b)=e` is currently dropped (falls to `IrUnsupportedStatement`); JS array destructuring needs the binding shape. | Overloading `IrVariableDeclaration` (single `Name`) with a pattern would muddy the common case; a dedicated node is clearer and is target-agnostic (Dart records could consume it). |
| `IsJsCallableInvoke` / `IsJsTupleElement` flags on `IrMemberOrigin` | The expression bridge dispatches call/member-access lowering off `IrMemberOrigin` (same channel as the PlainObject-instance-method path); recognition needs the symbol context only the frontend has. | Pattern-matching type names in the adapter would be brittle and duplicate symbol resolution. |
