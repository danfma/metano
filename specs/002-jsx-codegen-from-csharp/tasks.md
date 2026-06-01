---
description: "Task list for JSX/TSX code generation from C# components"
---

# Tasks: JSX/TSX Code Generation from C# Components

**Input**: Design documents from `specs/002-jsx-codegen-from-csharp/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Golden-output tests ARE included — the spec's Success Criteria (SC-003) and Constitution V require golden/expected-output tests to accompany every transpiler behavior. They live under `tests/Metano.Tests/` with fixtures in `tests/Metano.Tests/Expected/`.

**Organization**: Tasks are grouped by user story (US1–US5 from spec.md) so each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 maps to the spec's user stories; Setup/Foundational/Polish carry no story label
- Exact file paths are included in each task

## Path Conventions

Compiler (target-agnostic core + TypeScript adapter). Core: `src/Metano.Compiler/`. TS adapter: `src/Metano.Compiler.TypeScript/`. Annotations: `src/Metano/`. Bindings: `bindings/`. Samples: `samples/`. Generated/consumer: `targets/js/`. Tests: `tests/Metano.Tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Get the tree building green before any feature work, and establish a regression baseline.

- [X] T001 Find and fix the pre-existing build error in the solution. Running `dotnet build Metano.slnx` currently fails with `MS0007` during the post-build transpilation of `samples/SampleCounterV5/SampleCounterV5.csproj`: *"Cannot resolve cross-package import for type 'Metano.TypeScript.SolidJs.ISignal<…>': its containing assembly declares [TranspileAssembly] but no [EmitPackage] for the JavaScript target."* Fix by adding `[assembly: EmitPackage("<npm-package-name>")]` (choose a name consistent with the other bindings, e.g. `metano-solid-js`) to `bindings/Metano.TypeScript.SolidJs/AssemblyInfo.cs` (and verify `bindings/Metano.TypeScript.DOM` likewise declares `[EmitPackage]` if it is consumed cross-package). Confirm `dotnet build Metano.slnx` succeeds end to end.
- [X] T002 Establish the green baseline: run `dotnet run --project tests/Metano.Tests/` and confirm the existing suite passes after T001. Record the passing count as the regression baseline for later phases.

**Checkpoint**: Solution builds and the existing test suite is green.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The IR/extraction primitives, JSX AST + printer, file-extension selection, and diagnostics scaffolding that ALL user stories depend on. No JSX user story can begin until this is complete.

**⚠️ CRITICAL**: Phase 2 must not change behavior for non-JSX code — existing golden tests MUST stay green (T015 gate).

### Core IR + extraction (target-agnostic)

- [X] T003 [P] Extend `IrNewExpression` with an optional ordered `IReadOnlyList<IrMemberInit>? Initializers` field, and add the `IrMemberInit(string MemberName, string? EmittedName, IrExpression Value)` record, in `src/Metano.Compiler/IR/IrExpression.cs` (per data-model.md A2).
- [X] T004 [P] Add JSX recognition flags `IsJsxComponent`, `JsxNativeElementTag` (string?), `RendersAsJsxElement` to `IrTypeSemantics` in `src/Metano.Compiler/IR/IrTypeSemantics.cs` with defaults that leave all non-JSX types unchanged (per data-model.md A1).
- [X] T005 [P] Add JSX symbol predicates to `src/Metano.Compiler/SymbolHelper.cs`: `HasJsxComponentBuilder`, `GetJsxNativeElementTag`, `DerivesFromJsxComponentBuilder` (walk base chain for `[JsxComponentBuilder]`), and `IsJsxRenderable` (component | `[JsxNativeElement]` | `[Import]`/`[External]` typed as/convertible to the marked `JsxElement`) — mirroring the existing `HasExternal`/`HasPlainObject` style in `Metano.Annotations.TypeScript`.
- [X] T006 Populate the JSX flags in `IrClassExtractor.ExtractSemantics` in `src/Metano.Compiler/Extraction/IrClassExtractor.cs`, using the T005 predicates (depends on T004, T005).
- [X] T007 Extend `ExtractObjectCreation`/`ExtractImplicitObjectCreation` → `BuildNewExpression` in `src/Metano.Compiler/Extraction/IrExpressionExtractor.cs` to read the `InitializerExpressionSyntax` (`oc.Initializer`), lower each `Member = value` assignment, and resolve `EmittedName` via `SymbolHelper.GetNameOverride(member, TargetLanguage.TypeScript)`, populating `IrNewExpression.Initializers` (depends on T003).

### TypeScript AST + printer + emission plumbing (adapter)

- [X] T008 [P] Add JSX AST nodes in `src/Metano.Compiler.TypeScript/TypeScript/AST/`: `TsJsxElement.cs` (TagName, Attributes, Children, SelfClosing), `TsJsxAttribute.cs` (+ `TsJsxAttributeValue`/`TsJsxAttributeStringValue`/`TsJsxAttributeExpressionValue`), `TsJsxChild.cs` (`TsJsxText`/`TsJsxExpressionChild`/`TsJsxElementChild`) per data-model.md D1–D3.
- [X] T009 Add printing for the JSX nodes in `src/Metano.Compiler.TypeScript/TypeScript/Printer.cs`: a `case TsJsxElement` in `PrintExpression`, plus `PrintJsxElement`/`PrintJsxAttribute`/`PrintJsxChild` helpers emitting `<tag …>…</tag>` / `<tag … />`, `name="…"` / `name={…}`, and text/`{expr}`/nested element (depends on T008).
- [X] T010 [P] Extend `ImportCollector.CollectReferencedTypeNames` in `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs` to recurse into `TsJsxElement`/attributes/children and collect component, `For`, and imported-renderable names as **value** imports (depends on T008).
- [X] T011 [P] Add an `isJsx` parameter to `PathNaming.GetRelativePath(ns, typeName, isJsx)` in `src/Metano.Compiler.TypeScript/Transformation/PathNaming.cs`, selecting `.tsx` when true and `.ts` otherwise.
- [X] T012 Add a `ContainsJsx` recursive scan over produced top-level statements/expressions in `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs` (`TransformGroup`) and thread its result as `isJsx` into `GetRelativePath` (depends on T009, T011).

### Diagnostics scaffolding

- [X] T013 [P] Add the `MS0026` constant `JsxRenderableUnrecognized` (+ XML-doc) to `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` per the diagnostics contract (raised in Phase 7).

### Foundational regression gate

- [X] T014 Build the solution (`dotnet build Metano.slnx`) and run `dotnet run --project tests/Metano.Tests/`; confirm the JSX AST/printer/IR additions compile and the wiring is sound (depends on T003–T013).
- [X] T015 Regression gate: confirm the existing golden tests still pass and no previously-`.ts` file became `.tsx` (non-JSX code path unchanged) — compare against the T002 baseline.

**Checkpoint**: IR carries object initializers + JSX flags; the printer can emit JSX; `.tsx` is selected only when JSX is present; non-JSX behavior is unchanged.

---

## Phase 3: User Story 1 - Renderable record component becomes a TSX function component (Priority: P1) 🎯 MVP

**Goal**: A `[JsxComponentBuilder]`-derived record lowers to `export function <Name>(props: <Name>Props)` + `export type <Name>Props`, with the `Render()` body lowered, props hoisted with defaults, and a JSX return.

**Independent Test**: Transpile a component whose `Render()` returns one element; assert a `.tsx` file with the function + Props type, and that referenced props are hoisted (contract C1–C4).

### Implementation for User Story 1

- [X] T016 [US1] Create `src/Metano.Compiler.TypeScript/Bridge/IrToTsJsxComponentBridge.cs` that, for an `IrClassDeclaration` with `Semantics.IsJsxComponent`, emits the `export type <Name>Props` object type from settable properties (`init`/`set`) + record positional params, all optional/camelCased, excluding get-only/computed/`[Ignore]` (FR-003, contract C3).
- [X] T017 [US1] In `IrToTsJsxComponentBridge`, emit `export function <Name>(props: <Name>Props)` and lower the render-method body into the function body, returning the produced JSX (FR-002, FR-006).
- [X] T018 [US1] Implement prop-reference hoisting + rewrite in `IrToTsJsxComponentBridge`: for each prop referenced in the body, emit `const props$<camel> = props.<camel> ?? <c#-type-default>;` at the top and rewrite reads of the prop to `props$<camel>` (FR-004, contract C2).
- [X] T019 [US1] Add a `TryEmitJsxComponent` route in `TypeTransformer.BuildTypeStatements` (`src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs`) that dispatches `IsJsxComponent` types to `IrToTsJsxComponentBridge` BEFORE the class/plain-object branch; honor `[Name]` for the function and `<Override>Props` (FR-001, FR-005) (depends on T016–T018).
- [X] T020 [P] [US1] Add golden tests in `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` covering contracts C1 (empty component), C2 (prop + default + hoist), C3 (membership rules), C4 (`[Name]`), with expected files `tests/Metano.Tests/Expected/jsx-hello.tsx`, `jsx-counter-span.tsx`, `jsx-membership.tsx`, `jsx-named-component.tsx` (depends on T019).

**Checkpoint**: A component record with simple props transpiles to a correct, self-contained TSX function component.

---

## Phase 4: User Story 2 - Native HTML element builders lower to intrinsic JSX elements (Priority: P2)

**Goal**: JSX-typed `new T { … }` lowers to `<tag …>` with attribute mapping, children, text, and self-closing.

**Independent Test**: Transpile `new Html.Div { ClassName = "x", Children = [...] }`; assert `<div class="x">…</div>` (contracts N1–N3).

### Implementation for User Story 2

- [X] T021 [US2] Create `src/Metano.Compiler.TypeScript/Bridge/IrToTsJsxBridge.cs` that converts an `IrNewExpression` whose `Type.Semantics.JsxNativeElementTag is not null` into a `TsJsxElement` using the declared tag, classifying the `Children` member assignment as children and all other assignments as attributes (FR-007, FR-010, contract N3, research D4).
- [X] T022 [US2] In `IrToTsJsxBridge`, map attribute names by `EmittedName` (the `[Name]` override) else camelCase of the member name, and map values to `TsJsxAttributeStringValue` for string literals vs `TsJsxAttributeExpressionValue` otherwise, including event handlers (FR-008, FR-009, contracts N1–N2).
- [X] T023 [US2] In `IrToTsJsxBridge`, lower children: nested JSX-typed `new` → `TsJsxElementChild`, `Text(literal)` → `TsJsxText`, `Text(expr)` → `TsJsxExpressionChild`; emit a self-closing element when there are no children (FR-010, FR-011, contract N3).
- [X] T024 [US2] Route JSX-typed `IrNewExpression` (`Type.Semantics.RendersAsJsxElement`) through `IrToTsJsxBridge` from `MapNewExpression` in `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.cs`, before the plain-object/branded branches (depends on T021–T023).
- [X] T024a [US2] Add `[Name("class")]` to `Html.Element.ClassName` (and any other HTML-literal attribute names, e.g. `HtmlFor` → `for`) in `bindings/Metano.TypeScript.SolidJs/Web/Html.Element.cs`, so the SolidJS binding emits `class` per the clarification and contract N1. Without this the engine (T022) would emit `className`. (Closes analyze gap G1.)
- [X] T025 [P] [US2] Add golden tests in `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` for contracts N1 (attribute + `[Name]`/camelCase), N2 (literal vs expr + handler), N3 (children/text/nested), with expected `.tsx` fixtures in `tests/Metano.Tests/Expected/` (depends on T024).

**Checkpoint**: Native elements with attributes, children, and text render as intrinsic JSX.

---

## Phase 5: User Story 3 - Component records lower to JSX component usage (Priority: P2)

**Goal**: A component-record `new T { … }` in a renderable position lowers to `<Name … />` with init assignments as attributes; the render-entry lambda lowers to JSX.

**Independent Test**: Transpile `new Counter { Count = 3 }` in a `Children` list; assert `<Counter count={3} />` (contract N4).

### Implementation for User Story 3

- [X] T026 [US3] In `src/Metano.Compiler.TypeScript/Bridge/IrToTsJsxBridge.cs`, handle an `IrNewExpression` whose `Type.Semantics.IsJsxComponent` (a component, not a native element): emit `TsJsxElement` with the component name as tag and init assignments as camelCased attributes; `new T()` with no initializer → self-closing `<T />` (FR-012, FR-013, contract N4).
- [X] T027 [US3] Ensure a lambda/expression producing a component at the module entry point (e.g. the `render` argument) lowers its body to JSX — verify `IrToTsExpressionBridge` arrow-function lowering reaches `IrToTsJsxBridge` for the JSX-typed `new` inside (FR-014, contract N5) (depends on T026).
- [X] T028 [P] [US3] Add golden tests for contracts N4 (`<Counter count={3} />`, `<Counter />`) and N5 (render-entry lambda) in `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` with expected `.tsx` fixtures (depends on T026, T027).

**Checkpoint**: Component composition renders correctly; US1+US2+US3 compose into a full tree.

---

## Phase 6: User Story 4 - SolidJS reactivity & helper primitives (Priority: P2)

**Goal**: The explicit signal API and Solid helpers lower to idiomatic SolidJS, with the `ISignal`/`SignalWrapper` abstraction elided.

**Independent Test**: Transpile a component using `Solid.CreateSignal`/`.Value`/`.Set`/`CreateEffect`/`For`; assert the documented Solid forms with correct imports (contracts R1–R6).

### Binding refactor (FR-015, FR-016)

- [X] T029 [US4] Refactor `bindings/Metano.TypeScript.SolidJs/Solid.cs` so `Solid.CreateSignal(value)` maps to `createSignal(value)` via `[Import("createSignal", from: "solid-js")]` + `[Emit("createSignal($0)")]`, dropping the `SignalWrapper`/`CreateRawSignal` indirection from emission (FR-015, contract R1).
- [X] T030 [US4] Refactor `bindings/Metano.TypeScript.SolidJs/ISignal.cs` so `Value` getter emits `$0[0]()` and `Set(value)`/`Set(updater)` emit `$0[1]($1)` (direct form, no `() => v` wrap) via `[Emit]`/`[MapProperty]`/`[MapMethod]`; mark `bindings/Metano.TypeScript.SolidJs/SignalWrapper.cs` so it is not emitted (FR-016, contracts R2–R3, research D6).

### Helper mapping (FR-017, FR-018, FR-019)

- [X] T031 [US4] In `src/Metano.Compiler.TypeScript/Bridge/IrToTsJsxBridge.cs`, recognize the `Solid.For(items, lambda)` helper call and emit `<For each={items}>{lambda}</For>` as a `TsJsxElement("For", [each=…], [expressionChild(lambda)])`, importing `For` from `solid-js` (FR-018, contract R5, research D7).
- [X] T032 [P] [US4] Confirm `Solid.CreateEffect` → `createEffect` (`solid-js`) and `SolidRenderer.Render` → `render` (`solid-js/web`) lower via the existing `[Import]` on `bindings/Metano.TypeScript.SolidJs/Solid.cs` / `Web/SolidRenderer.cs`; add `[Import]` where missing (FR-017, FR-019, contracts R4, R6).
- [X] T033 [P] [US4] Add golden tests for contracts R1–R6 in `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` with expected `.tsx` fixtures, asserting no `SignalWrapper`/wrapper allocation survives (SC-002) (depends on T029–T032).

**Checkpoint**: A stateful counter component lowers to idiomatic SolidJS with correct imports and no wrapper objects.

---

## Phase 7: User Story 5 - Library-agnostic renderable-type recognition + diagnostic (Priority: P3)

**Goal**: Imported renderable types (e.g. `solid-router`) are recognized as JSX, and unrecognized renderables raise `MS0026`.

**Independent Test**: Declare an `[Import]`-ed renderable typed as the marked element; assert it emits as JSX usage with the correct import; assert an unmarked POCO in a renderable position raises `MS0026` (contracts R7, diagnostics).

### Implementation for User Story 5

- [X] T034 [US5] Add a representative imported renderable type to `bindings/Metano.TypeScript.SolidJs/` (e.g. `Routing/Route.cs` carrying `[Import("Route", from: "solid-router")]`, typed as/convertible to `JsxElement`) to exercise FR-022/SC-004.
- [X] T035 [US5] In `IrToTsJsxBridge`, emit an imported renderable (`RendersAsJsxElement` via `[Import]`, not a native element, not a local component) as `<Name … />` and add its package import (FR-022, FR-023, contract R7) (depends on T034).
- [X] T036 [US5] Raise `MS0026` (via `IrToTsJsxBridge`/recognition) when a type used in a renderable position is not classifiable as component / native / imported renderable, carrying the Roslyn `Location`; thread the diagnostic through the existing collection path (FR-024, diagnostics contract) (depends on T013).
- [X] T037 [P] [US5] Add tests: a cross-package golden test via `TranspileHelper.TranspileWithLibrary` for the imported `Route` (contract R7), and a diagnostics test via `TranspileWithDiagnostics`/`TranspileWithLibraryAndDiagnostics` asserting `MS0026` for an unmarked renderable (SC-006) (depends on T035, T036).

**Checkpoint**: Recognition proven against native + one imported source; unrecognized renderables fail loudly, not silently.

---

## Phase 8: End-to-End Sample, Consumer & Polish (Cross-Cutting)

**Purpose**: Prove the whole slice end to end (SC-001, SC-005) and close out review/docs.

- [X] T038 Wire `samples/SampleSolidUi/SampleSolidUi.csproj` for transpilation: add `<MetanoOutputDir>../../targets/js/sample-solid-ui/src/</MetanoOutputDir>`, `<MetanoClean>true</MetanoClean>`, the `Metano.Build.targets` import, and the `MetanoPostBuildCommand` biome step, mirroring `samples/SampleCounterV1/SampleCounterV1.csproj`. (Resolves analyze note L1.)
- [X] T038a Ensure `samples/SampleSolidUi` marks its types for transpilation: add `[assembly: TranspileAssembly]` (e.g. a new `samples/SampleSolidUi/AssemblyInfo.cs`) or `[Transpile]` on `Counter`/`CounterGroup`, since the prototype records currently carry no transpilation marker and would emit nothing. (Closes analyze gap G2.)
- [X] T039 [P] Create the Vite + SolidJS consumer at `targets/js/sample-solid-ui/`: `package.json` (solid-js, metano-runtime workspace, vite, vite-plugin-solid), `vite.config.ts`, `tsconfig.app.json` (`jsx: "preserve"`, `jsxImportSource: "solid-js"`), and `index.html` — mirroring `targets/js/sample-counter-v1/`.
- [X] T040 [P] Register `sample-solid-ui` in the root Bun workspace `package.json` scripts (mirror the `sample-counter-*` entries).
- [X] T041 Build the sample (`dotnet build samples/SampleSolidUi/`) to transpile into `targets/js/sample-solid-ui/src`, then `cd targets/js/sample-solid-ui && bun install && bun run build`; confirm it builds with ZERO manual edits to generated `.tsx` (SC-001) (depends on T038–T040, and US1–US4).
- [X] T042 [P] Add a `bun test` end-to-end test in `targets/js/sample-solid-ui/test/` asserting the counter group renders and increments (SC-005) (depends on T041).
- [X] T043 [P] Add a baseline capability-matrix entry for the new "UI components (JSX/TSX)" capability under `specs/001-project-baseline-evolution/baseline/` with code+test traceability (CLAUDE.md spec-as-source-of-truth).
- [X] T044 Run `dotnet csharpier .`, then the dual-agent review (`compiler-man` + `bob` in parallel) on the full diff per CLAUDE.md/Constitution; fix findings before declaring complete.
- [X] T045 Run the `quickstart.md` validation steps end to end and confirm every "done" check (SC-001…SC-006) passes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 → T002. Must finish first (green tree + baseline).
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories. Internal order: T003/T004/T005 [P] → T006, T007 → T008 → T009/T010 [P], T011 → T012; T013 [P]; then T014 → T015 gate.
- **User Stories (Phases 3–7)**: All depend on Foundational. US1 is the MVP. US2 depends on the JSX element machinery it introduces; US3 depends on US2's `IrToTsJsxBridge`; US4 and US5 depend on US2/US3 element emission. Recommended sequential order P1 → P2 → P2 → P2 → P3 because they share `IrToTsJsxBridge`.
- **Polish (Phase 8)**: T041+ depend on US1–US4 landing; T038–T040 can be prepared earlier in parallel.

### User Story Dependencies

- **US1 (P1)**: After Foundational. Independent (function/Props shape only).
- **US2 (P2)**: After Foundational. Introduces `IrToTsJsxBridge` (native elements).
- **US3 (P2)**: After US2 (reuses `IrToTsJsxBridge` for component usage).
- **US4 (P2)**: After US2 (helper `For` is a JSX element; signals are body lowering). Binding refactor (T029–T030) is independent and can start any time after Setup.
- **US5 (P3)**: After US2/US3 (imported renderables reuse element emission) + T013 (diagnostic constant).

### Parallel Opportunities

- Setup: none ([T001] must precede [T002]).
- Foundational: T003, T004, T005 in parallel; T008 parallel with T010/T011; T013 anytime.
- Binding refactor T029–T030 (Phase 6) can proceed in parallel with Phases 3–5 (different files).
- Consumer scaffolding T039, T040 in parallel with feature phases.
- All golden-test tasks ([P]) for a story run together once that story's emission lands.

---

## Parallel Example: Foundational Phase

```bash
# Core IR additions (different files, no deps):
Task: "Extend IrNewExpression + add IrMemberInit in src/Metano.Compiler/IR/IrExpression.cs"   # T003
Task: "Add JSX flags to IrTypeSemantics in src/Metano.Compiler/IR/IrTypeSemantics.cs"          # T004
Task: "Add JSX symbol predicates in src/Metano.Compiler/SymbolHelper.cs"                        # T005
Task: "Add MS0026 constant in src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs"           # T013
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 (fix build + baseline) → Phase 2 (foundational) → Phase 3 (US1).
2. STOP and VALIDATE: a component record with props transpiles to a correct TSX function component (golden tests C1–C4).
3. This is the demonstrable MVP slice.

### Incremental Delivery

Setup + Foundational → US1 (MVP) → US2 (native elements) → US3 (composition) → US4 (reactivity) → US5 (library-agnostic + diagnostic) → Phase 8 (end-to-end sample + review). Each story adds value and stays golden-tested.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- [Story] label maps each task to its spec user story for traceability.
- The signal lowering form (`const count = createSignal(...)`, read `count[0]()`, write `count[1](...)`) and `class`-vs-camelCase attribute rules are fixed by the spec Clarifications — pin them in golden fixtures.
- Deferred (non-blocking, tracked in research.md): Solid `<For>` index-accessor adaptation, optional second diagnostic code (MS0027), `[JsxChildren]` marker for non-`Children` child slots.
- Deferred from the dual-agent review (justified in code): FR-016 defensive updater-wrap for **function-typed** signals — a declarative `[Emit]` template cannot introspect whether the closed generic `T` is a delegate, and an unconditional wrap would break the common value-set; rare per spec. Rationale in `bindings/Metano.TypeScript.SolidJs/ISignal.cs`.
- Out of scope, left untouched: stale committed Dart output under `targets/flutter/sample_counter` (pre-existing drift vs current `SampleCounterV1` source; not part of this feature).
- Commit after each task or logical group on branch `002-jsx-codegen-from-csharp`; reference the spec, no AI attribution.
