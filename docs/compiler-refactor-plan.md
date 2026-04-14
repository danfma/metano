# Compiler Refactor Plan for Multi-Target Backends

## Goal

Refactor the current TypeScript-focused compiler pipeline into a target-agnostic compiler architecture that can support multiple output languages without duplicating semantic lowering logic.

The near-term constraint is critical:

- Keep the current TypeScript backend working.
- Preserve current observable behavior while refactoring.
- Migrate incrementally.
- Prepare the architecture for future targets beyond TypeScript.

## Current Baseline

The current implementation is centered in `src/Metano.Compiler.TypeScript/`.

Observed characteristics:

- Roslyn already provides parsing, binding, and semantic analysis.
- The TypeScript backend currently contains both:
  - semantic lowering decisions
  - TypeScript-specific emission decisions
- Large transformation hotspots include:
  - `Transformation/TypeTransformer.cs`
  - `Transformation/RecordClassTransformer.cs`
  - `Transformation/ImportCollector.cs`
  - `Transformation/TypeMapper.cs`
  - `TypeScript/Printer.cs`
- Current test baseline:
  - `357` passing tests
  - validated through the TUnit runner binary

## Desired Target Architecture

The compiler should evolve toward this pipeline:

`Roslyn front-end -> semantic model extraction -> shared IR -> target-specific lowering/emission`

### Layers

1. **Front-end**
   - Input: Roslyn `Compilation`, symbols, syntax, semantic models.
   - Responsibility: discover transpilable program shape and normalize semantic facts.

2. **Shared IR**
   - A target-agnostic intermediate representation of:
     - modules
     - types
     - members
     - expressions
     - statements
     - type references
     - runtime requirements
     - diagnostics metadata

3. **Common lowering**
   - Converts Roslyn semantics to shared IR.
   - Must contain language-independent rules such as:
     - records/value semantics
     - nullable handling
     - async/task concepts
     - extension methods
     - overload dispatch intent
     - pattern semantics

4. **Target backends**
   - Convert shared IR to a target-specific AST or textual output.
   - Examples:
     - TypeScript backend
     - future Dart/Kotlin/Swift/etc.

5. **Packaging/runtime integration**
   - Per-target concerns:
     - imports
     - package/module layout
     - runtime helper mapping
     - dependency manifest generation

## Design Rules

These rules should guide every refactor step:

- Do not break the TypeScript backend during architectural extraction.
- Do not move TypeScript-specific syntax decisions into shared lowering.
- Do not put semantic decisions into backend printers/emitters.
- Eliminate static ambient compiler state from shared logic.
- Each refactor step must leave the codebase in a releasable state.
- Prefer adapters and parallel paths over big-bang rewrites.

## Main Problems to Fix

### 1. TypeScript backend owns too much semantic logic

Today, backend classes decide both what the source means and how TypeScript should express it.

Examples:

- `RecordClassTransformer`
- `ImportCollector`
- `TypeMapper`

This will not scale across multiple targets.

### 2. Static state in `TypeMapper`

`TypeMapper` currently uses thread-static mutable state for mappings and dependency tracking.

Risks:

- hidden coupling
- difficult reasoning
- poor composability
- harder parallelization
- harder testing of isolated steps

### 3. Import/runtime decisions happen too late

The current TypeScript import logic infers runtime needs by walking TypeScript AST output. That is fragile for multi-target support.

Instead, runtime requirements should be modeled as explicit facts earlier in the pipeline.

### 4. Large files combine multiple concerns

Especially:

- `TypeTransformer.cs`
- `RecordClassTransformer.cs`
- `ImportCollector.cs`

These should be decomposed by responsibility, not only by size.

## Phased Execution Plan

## Phase 0 - Freeze the Baseline

### Objective

Create a stable safety net before moving architecture.

### Tasks

1. Record the current passing test baseline.
2. Add a short contributor note describing how to run tests through the TUnit binary.
3. Identify the highest-value golden/snapshot tests for TypeScript output.
4. Add a benchmark harness for transform time on representative samples if lightweight enough.

### Deliverables

- documented baseline
- test execution command documented
- no behavior changes

### Exit Criteria

- all current tests still pass
- no production code changed or only documentation/test harness changed

## Phase 1 - Define the Shared Compiler Model

### Objective

Create the target-agnostic contracts without yet migrating the full pipeline.

### Tasks

1. Define compiler service contracts for:
   - type resolution
   - symbol lowering
   - runtime dependency collection
   - diagnostics reporting
2. Define the first version of the shared IR:
   - `IrModule`
   - `IrTypeDecl`
   - `IrMemberDecl`
   - `IrMethodDecl`
   - `IrPropertyDecl`
   - `IrFieldDecl`
   - `IrExpression`
   - `IrStatement`
   - `IrTypeRef`
3. Add support for semantic annotations/capabilities in IR:
   - nullable
   - async
   - generator
   - extension method
   - record/value semantics
   - runtime helper requirements

### Deliverables

- initial IR model
- initial compiler service interfaces
- architecture note or ADR if needed

### Exit Criteria

- the IR compiles and is documented
- no existing backend behavior is removed

## Phase 2 - Remove Ambient State and Introduce Context Objects

### Objective

Make the current pipeline explicit and composable before rerouting it.

### Tasks

1. Replace thread-static `TypeMapper` state with an explicit per-compilation context/service.
2. Move dependency tracking and cross-package origin tracking into owned context objects.
3. Ensure current TypeScript transformation classes consume context explicitly.
4. Keep public behavior unchanged.

### Deliverables

- `TypeMapper` no longer depends on ambient mutable state
- dependency tracking lives in owned objects

### Exit Criteria

- all tests still pass
- no thread-static state remains in the shared mapping path

## Phase 3 - Extract Front-End Semantic Lowering

### Objective

Move language-independent analysis out of TypeScript-specific transformers.

### Tasks

1. Split `TypeTransformer` into:
   - discovery/orchestration
   - semantic lowering entrypoints
   - backend dispatch
2. Extract common semantic builders for:
   - records/classes/interfaces/enums
   - constructor model
   - method model
   - overload model
   - nested types
3. Introduce `Roslyn -> IR` builders alongside existing TypeScript logic.
4. Start with easy shapes:
   - enums
   - interfaces
   - type references
   - simple classes

### Deliverables

- partial Roslyn-to-IR pipeline
- backend still driven through adapters where necessary

### Exit Criteria

- at least one vertical slice reaches TypeScript through IR
- tests still green

## Phase 4 - Adapt TypeScript Backend to Consume IR

### Objective

Turn the current TypeScript backend into a true target backend.

### Tasks

1. Introduce `IR -> TypeScript AST` adapters.
2. Keep the existing `TypeScript/AST` and `Printer` initially.
3. Gradually replace direct Roslyn-driven lowering in TypeScript backend classes.
4. Migrate TypeScript-specific concerns to backend-only services:
   - naming/escaping
   - module path calculation
   - import rendering
   - package.json generation

### Deliverables

- TypeScript backend consuming IR for selected features
- reduced Roslyn coupling inside TS backend

### Exit Criteria

- core TypeScript features flow through IR
- output parity preserved for migrated areas

## Phase 5 - Model Runtime Requirements Explicitly

### Objective

Stop discovering runtime needs by reverse-walking backend-specific ASTs.

### Tasks

1. Represent runtime helper requirements in IR.
2. Represent cross-package dependencies and external imports as semantic facts.
3. Refactor `ImportCollector` into:
   - semantic dependency collection
   - TypeScript import rendering
4. Ensure packaging logic reads backend facts instead of inferring from syntax trees.

### Deliverables

- explicit runtime/dependency facts in IR or backend-bound metadata
- simpler TypeScript import assembly

### Exit Criteria

- import/runtime logic no longer depends primarily on heuristics over TS AST shape

## Phase 6 - Migrate Complex Features

### Objective

Move high-complexity features after the architecture is proven.

### Migration order

1. records and value semantics
2. overload dispatch
3. extension methods and module lowering
4. pattern matching / switch expressions
5. JSON serializer context features
6. operator overloading

### Deliverables

- migrated complex features through IR

### Exit Criteria

- TypeScript backend old/new paths substantially reduced
- parity maintained by tests

## Phase 7 - Pilot a Second Target

### Objective

Validate that the architecture is truly multi-target.

### Tasks

1. Choose one pilot backend.
2. Implement a minimal feature slice:
   - modules
   - simple classes
   - enums
   - interfaces
   - methods
3. Compare pain points against TypeScript-specific assumptions still present in IR.
4. Refine IR based on actual second-target pressure.

### Deliverables

- first non-TypeScript target prototype

### Exit Criteria

- the second target compiles a real sample
- new target does not require semantic rules to be copied out of the TypeScript backend

## Recommended Extraction Order in the Existing Codebase

This is the suggested file-by-file order for the first implementation wave.

### Wave 1

- `Transformation/TypeMapper.cs`
- `Transformation/TypeScriptTransformContext.cs`
- `Transformation/PathNaming.cs`
- `Transformation/ImportCollector.cs`

Reason:

- these are foundational dependency/context concerns
- they unblock removal of hidden state
- they reduce coupling before deeper transformations

### Wave 2

- `Transformation/TypeTransformer.cs`
- `Transformation/InterfaceTransformer.cs`
- `Transformation/EnumTransformer.cs`

Reason:

- these are good first vertical slices into shared IR

### Wave 3

- `Transformation/RecordClassTransformer.cs`
- `Transformation/OverloadDispatcherBuilder.cs`
- `Transformation/ModuleTransformer.cs`

Reason:

- these contain the most semantic complexity and should move after the framework is ready

### Wave 4

- `Transformation/ExpressionTransformer.cs`
- child handlers under `Transformation/`

Reason:

- expression lowering should migrate after top-level declaration structure is stabilized

## Test Strategy

## Keep Existing Tests

All current TypeScript behavior tests remain required.

## Add Layered Tests

Add tests at three levels:

1. Roslyn -> IR tests
2. IR -> TypeScript AST tests
3. end-to-end Roslyn -> TypeScript output tests

## Add Feature Matrix Coverage

Track, per feature:

- semantic support in IR
- TypeScript backend support
- next target support
- runtime helper dependency
- expected diagnostics

## Risks

### Risk: Big-bang rewrite

Mitigation:

- use adapters
- migrate feature slices
- preserve old path until parity is proven

### Risk: IR becomes TypeScript-shaped

Mitigation:

- validate with a second target early
- forbid TS syntax details in IR naming and constructs

### Risk: Semantic duplication during migration

Mitigation:

- establish one canonical lowering path per migrated feature
- deprecate old path immediately after parity is achieved

### Risk: Test suite only validates TypeScript text

Mitigation:

- add IR-level tests before major feature migration

## Suggested First Concrete Milestone

The first milestone should be intentionally narrow:

### Milestone A

- eliminate ambient `TypeMapper` state
- add initial IR model
- route enums and interfaces through IR
- keep TypeScript output unchanged

Why this milestone:

- high architectural leverage
- relatively low feature risk
- proves the new shape without touching the most fragile code first

## Suggested Prompting Guidance for a Follow-Up Agent

If another agent continues this work, it should:

- preserve behavior first
- avoid rewriting everything at once
- prioritize extraction order
- explicitly separate semantic lowering from TypeScript emission
- produce small, reviewable patches

