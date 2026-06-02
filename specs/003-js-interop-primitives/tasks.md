---
description: "Task list for JS-interop foundational primitives ([JsTuple], [JsCallable], tuple deconstruction)"
---

# Tasks: JS-Interop Foundational Primitives (`[JsTuple]`, `[JsCallable]`, Tuple Deconstruction)

**Input**: Design documents from `specs/003-js-interop-primitives/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Golden-output tests ARE included — SC-001…SC-005 and Constitution V require golden/expected-output tests for transpiler behavior. They live under `tests/Metano.Tests/` with fixtures in `tests/Metano.Tests/Expected/`.

**Organization**: Grouped by user story (US1–US4). This feature is self-contained — it does NOT create or modify the branch-002 SolidJS binding; US4 uses an inline `Signal<T>` test binding (research D8).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4 maps to spec user stories; Setup/Foundational/Polish carry no story label
- Exact file paths included

## Path Conventions

Core (target-agnostic): `src/Metano.Compiler/`. TS adapter: `src/Metano.Compiler.TypeScript/`. Annotations: `src/Metano/Annotations/TypeScript/`. Tests: `tests/Metano.Tests/`.

---

## Phase 1: Setup

**Purpose**: Confirm a green baseline before feature work.

- [X] T001 Establish the green baseline: `dotnet build Metano.slnx` and `dotnet run --project tests/Metano.Tests/`; record the passing/skipped counts as the regression baseline. (This branch is `main`-based and should already build clean — the branch-002 MS0007 break is not present here.)

**Checkpoint**: Solution builds; existing suite green.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Attributes, core recognition flags, symbol predicates, and diagnostic constants that all user stories depend on. Additive only — no behavior change yet.

**⚠️ CRITICAL**: Existing golden tests MUST stay green (T009 gate).

- [X] T002 [P] Add `JsTupleAttribute` (targets class/struct, `Inherited = false`) in `src/Metano/Annotations/TypeScript/JsTupleAttribute.cs` with XML docs matching the `[External]`/`[Optional]` style (array-shape sibling of `[PlainObject]`; TS-specific, no-op for other targets).
- [X] T003 [P] Add `JsCallableAttribute` (targets interface, `Inherited = false`) in `src/Metano/Annotations/TypeScript/JsCallableAttribute.cs` with XML docs (erased callable; `Invoke` lowers to direct receiver call; supports overloaded `Invoke`).
- [X] T004 [P] Add `IsJsTuple` to `IrTypeSemantics` in `src/Metano.Compiler/IR/IrTypeSemantics.cs` (default false; `<param>` doc; same pattern as `IsPlainObject`).
- [X] T005 [P] Add `IsJsCallableInvoke`, `IsJsTupleElement`, and `int TupleIndex = -1` to `IrMemberOrigin` in `src/Metano.Compiler/IR/IrExpression.cs` (defaults preserve current behavior).
- [X] T006 [P] Add JSX-interop symbol predicates to `src/Metano.Compiler/SymbolHelper.cs`, mirroring `HasExternal`/`HasPlainObject` (namespace `Metano.Annotations.TypeScript`): `HasJsTuple(ISymbol)`, `HasJsCallable(ISymbol)`, and a helper to detect a call to `Invoke` on a `[JsCallable]` interface + a helper to resolve a member's positional tuple index.
- [X] T007 [P] Add diagnostic constants to `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs`: `InvalidJsTuple = "MS0027"` and `InvalidJsCallable = "MS0028"` (+ XML docs per the diagnostics contract). NOTE: skip `MS0026` — reserved by branch `002`. Not raised yet.
- [X] T008 Build (`dotnet build Metano.slnx`) and run `dotnet run --project tests/Metano.Tests/`; confirm the additions compile (0 warnings) and wiring is sound (depends on T002–T007).
- [X] T009 Regression gate: confirm existing golden tests still pass vs the T001 baseline (no behavior change from additive metadata).

**Checkpoint**: Attributes + flags + diagnostic codes exist; nothing emits differently yet.

---

## Phase 3: User Story 2 - `[JsTuple]` record → JS array-tuple (Priority: P1)

**Goal**: A `[JsTuple]` positional record lowers to a JS array-tuple — type alias `= [T0,T1]` when standalone, erased when `[Import]`; no class/equals/hashCode/with; positional member access → `[i]`.

**Independent Test**: Transpile `[JsTuple] record Pair<A,B>(A First, B Second);` → `export type Pair<A,B> = [A, B];`; access `.First`/`.Second` → `[0]`/`[1]` (contracts T1–T5).

### Implementation for User Story 2

- [X] T010 [US2] Populate `IsJsTuple` in `IrClassExtractor.ExtractSemantics` (`src/Metano.Compiler/Extraction/IrClassExtractor.cs`) via `SymbolHelper.HasJsTuple` (depends on T004, T006).
- [X] T011 [US2] Create `src/Metano.Compiler.TypeScript/Bridge/IrToTsJsTupleBridge.cs` mirroring `IrToTsPlainObjectBridge`: for `Semantics.IsJsTuple`, emit `export type <Name><...> = [T0, T1, ...]` (a `TsTypeAlias` to a `TsTupleType`) from positional members; suppress `equals`/`hashCode`/`with`; emit NOTHING (erased) when the type also carries `[Import]`.
- [X] T012 [US2] Add a `TryEmitJsTuple` route in `TypeTransformer.BuildTypeStatements` (`src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`) BEFORE `TryEmitPlainObjectOrClass`, delegating to `IrToTsJsTupleBridge` (mirror `TryEmitPlainObjectViaIr`) (depends on T011).
- [X] T013 [US2] Lower positional member access on a `[JsTuple]` value: tag the `IrMemberAccess` origin (`IsJsTupleElement` + `TupleIndex`) in `src/Metano.Compiler/Extraction/IrExpressionExtractor.cs`, and emit `TsElementAccess(receiver, index)` in `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.cs` (depends on T005).
- [X] T014 [US2] Raise `MS0027 InvalidJsTuple` in `CSharpSourceFrontend` validation when `[JsTuple]` is on a type with no positional shape, carrying the Roslyn `Location` (depends on T007).
- [X] T015 [P] [US2] Golden tests in `tests/Metano.Tests/JsTupleTranspileTests.cs` for contracts T1 (type alias), T2 (`[i]` access), T3 (`[Import]` erased), T5 (MS0027 via `TranspileWithDiagnostics`), with expected `.ts` fixtures in `tests/Metano.Tests/Expected/` (depends on T010–T014).

**Checkpoint**: `[JsTuple]` lowers to a tuple type / erases, with positional access and misuse diagnostic.

---

## Phase 4: User Story 1 - `[JsCallable]` interface → direct invocation (Priority: P1)

**Goal**: `recv.Invoke(args)` on a `[JsCallable]` interface lowers to `recv(args)`, including overloaded `Invoke`; the interface is erased.

**Independent Test**: Transpile `[JsCallable] interface I{void Invoke(int v); void Invoke(Func<int,int> f);}` with `cb.Invoke(5)` and `cb.Invoke(x=>x+1)` → `cb(5)` and `cb(x => x + 1)` (contracts C1–C5).

### Implementation for User Story 1

- [X] T016 [US1] Tag `IrMemberOrigin.IsJsCallableInvoke` in `src/Metano.Compiler/Extraction/IrExpressionExtractor.cs` when a call's target method is `Invoke` on a `[JsCallable]` interface (depends on T005, T006).
- [X] T017 [US1] In `IrToTsExpressionBridge.MapCall` (`src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.cs`), add a branch (parallel to the PlainObject-instance-method path) that lowers `IsJsCallableInvoke` calls to `TsCallExpression(receiver, args)` for any arity (depends on T016).
- [X] T018 [US1] Ensure a `[JsCallable]` interface is erased (no declaration emitted) — confirm `TypeTransformer` skips emission (it composes with `[External]`/`[Import]`); add a skip if needed in `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`.
- [X] T019 [US1] Raise `MS0028 InvalidJsCallable` in `CSharpSourceFrontend` validation when `[JsCallable]` is on a non-interface or the interface declares non-`Invoke` members, with the Roslyn `Location` (depends on T007).
- [X] T020 [P] [US1] Golden tests in `tests/Metano.Tests/JsCallableTranspileTests.cs` for contracts C1 (single), C2 (overloaded value/updater), C3 (arity), C5 (MS0028), with expected `.ts` fixtures (depends on T016–T019).

**Checkpoint**: `[JsCallable]` invoke lowering works with overloads; misuse diagnosed.

---

## Phase 5: User Story 3 - Tuple deconstruction (Priority: P2)

**Goal**: `var (a, b) = expr` → `const [a, b] = expr`, including discards.

**Independent Test**: Transpile `var (a, b) = makePair();` → `const [a, b] = makePair();`; `var (_, b) = …` → `const [, b] = …` (contracts D1–D4).

### Implementation for User Story 3

- [X] T021 [US3] Add IR nodes `IrTupleDeconstruction(Elements, Initializer, IsConst)` and `IrDeconstructionElement(string? Name, IrTypeRef? Type)` in `src/Metano.Compiler/IR/IrStatement.cs` (per data-model.md B3).
- [X] T022 [US3] Extract the C# deconstructing declaration in `src/Metano.Compiler/Extraction/IrStatementExtractor.cs`: handle the `ExpressionStatement` whose assignment Left is a `DeclarationExpressionSyntax` with a `ParenthesizedVariableDesignationSyntax` (elements = `SingleVariableDesignationSyntax` / `DiscardDesignationSyntax`) → `IrTupleDeconstruction` (depends on T021).
- [X] T023 [P] [US3] Add `TsDestructuringDeclaration(IReadOnlyList<string?> Names, TsExpression Initializer, bool Const, bool Exported)` in `src/Metano.Compiler.TypeScript/TypeScript/AST/TsDestructuringDeclaration.cs` (null entry = discard hole).
- [X] T024 [US3] Print `TsDestructuringDeclaration` in `src/Metano.Compiler.TypeScript/TypeScript/Printer.cs` as `const [a, , b] = <init>;` (empty slot for null) (depends on T023).
- [X] T025 [US3] Lower `IrTupleDeconstruction` → `TsDestructuringDeclaration` in `src/Metano.Compiler.TypeScript/Bridge/IrToTsStatementBridge.cs`; ensure later references resolve to the destructured names (depends on T021, T023).
- [X] T026 [P] [US3] Golden tests in `tests/Metano.Tests/DeconstructionTranspileTests.cs` for contracts D1 (basic), D2 (discard), D3 (reference resolution), D4 (let vs const), with expected `.ts` fixtures (depends on T022, T024, T025).

**Checkpoint**: Deconstructing declarations lower to JS array destructuring.

---

## Phase 6: User Story 4 - Composition (idiomatic signal) (Priority: P2)

**Goal**: The three primitives compose so an inline `Signal<T>` binding yields `const [count, setCount] = createSignal(0); count(); setCount(v); setCount(c=>c+1)` with zero `[Emit]`.

**Independent Test**: Transpile the inline-binding usage (contract S1) and assert the exact SolidJS-shaped output with no `[Emit]`/index forms and no surviving facade type.

### Implementation for User Story 4

- [X] T027 [P] [US4] Add a golden test `tests/Metano.Tests/SignalCompositionTests.cs` (contract S1) using an inline `[JsTuple, Import("Signal", from:"solid-js")] record Signal<T>(Func<T> Getter, [JsCallable] ISignalSetter<T> Setter)` + `Solid.CreateSignal` `[Import]`, plus a method body using `var (count, setCount) = …; count(); setCount.Invoke(…)`. Expected `.ts` fixture asserts `const [count, setCount] = createSignal(0); count(); setCount(...)`, `import { createSignal } from "solid-js"`, and zero `[Emit]`/`count[0]()`/wrapper artifacts (depends on US1, US2, US3).

**Checkpoint**: Composition proven in isolation; the primitives are ready for the dependent 002 reactivity refactor.

---

## Phase 7: Polish & Cross-Cutting

- [X] T028 [P] Add baseline attribute-catalog entries for `[JsTuple]` and `[JsCallable]` in `specs/001-project-baseline-evolution/baseline/attribute-catalog.md`.
- [X] T029 [P] Add baseline diagnostic-catalog entries `MS0027`/`MS0028` in `specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md` (and bump the range note).
- [X] T030 [P] Add a baseline feature-support-matrix row for the JS-interop primitives in `specs/001-project-baseline-evolution/baseline/feature-support-matrix.md`.
- [X] T031 Run `dotnet csharpier format .`, then the dual-agent review (`compiler-man` + `bob` in parallel) on the full diff per CLAUDE.md/Constitution; fix findings before declaring complete.
- [X] T032 Run the `quickstart.md` validation steps end to end; confirm SC-001…SC-005 pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 first.
- **Foundational (Phase 2)**: depends on Setup. BLOCKS all user stories. Internal: T002–T007 [P] → T008 → T009 gate.
- **US2 `[JsTuple]` (Phase 3)** and **US1 `[JsCallable]` (Phase 4)**: both P1, INDEPENDENT of each other (different code paths) — can be done in parallel after Foundational.
- **US3 deconstruction (Phase 5)**: independent of US1/US2; needed by US4.
- **US4 composition (Phase 6)**: depends on US1 + US2 + US3.
- **Polish (Phase 7)**: after US1–US4.

### Parallel Opportunities

- Foundational: T002, T003, T004, T005, T006, T007 all [P].
- US1 and US2 can proceed concurrently (no shared files except the shared `IrToTsExpressionBridge.cs` — T013 and T017 touch it, so sequence those two).
- US3 can proceed concurrently with US1/US2.
- All golden-test tasks ([P]) per story run together once that story's emission lands.
- Catalog tasks T028–T030 [P].

---

## Parallel Example: Foundational Phase

```bash
Task: "Add JsTupleAttribute in src/Metano/Annotations/TypeScript/JsTupleAttribute.cs"          # T002
Task: "Add JsCallableAttribute in src/Metano/Annotations/TypeScript/JsCallableAttribute.cs"     # T003
Task: "Add IsJsTuple to IrTypeSemantics"                                                        # T004
Task: "Add IrMemberOrigin flags (IsJsCallableInvoke/IsJsTupleElement/TupleIndex)"               # T005
Task: "Add SymbolHelper JS-interop predicates"                                                  # T006
Task: "Add MS0027/MS0028 constants"                                                             # T007
```

---

## Implementation Strategy

### MVP First (P1 primitives)

1. Phase 1 (baseline) → Phase 2 (foundational) → Phase 3 (`[JsTuple]`) + Phase 4 (`[JsCallable]`).
2. STOP and VALIDATE: both P1 primitives lower correctly with golden tests + misuse diagnostics. This is the demonstrable MVP (the two attributes that remove `[Emit]`).

### Incremental Delivery

Setup + Foundational → `[JsTuple]` → `[JsCallable]` → deconstruction → composition (S1) → polish. Each adds value and stays golden-tested.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- [Story] label maps each task to its spec user story.
- Diagnostic codes use **MS0027/MS0028** (MS0026 reserved by branch 002).
- This feature does NOT touch branch-002 artifacts; US4 uses an inline `Signal<T>` test binding (research D8).
- Deferred (research/spec): native ValueTuple, nested/assignment/foreach deconstruction, `.ItemN` on ValueTuples.
- Downstream (separate, post-merge): the 002 reactivity refactor migrates the real SolidJS binding onto these primitives and revalidates its consumer.
- Commit after each task or logical group on branch `003-js-interop-primitives`; reference the spec, no AI attribution.
