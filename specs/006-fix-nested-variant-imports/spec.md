# Feature Specification: Complete imports for nested record variants

**Feature Branch**: `006-fix-nested-variant-imports`

**Created**: 2026-06-28

**Status**: Draft

**Input**: User description: "Generated TypeScript for nested record variants (the companion-namespace lowering of an abstract record with nested record variants, used by the `[StrictUnionGuard]` pattern) omits required imports for any symbol referenced only inside the nested namespace block. The generated file is missing both an intra-project type import and a runtime-helper import, so the file fails to type-check."

## Overview

When Metano transpiles a C# abstract record whose nested record types form a discriminated union (the pattern paired with `[StrictUnionGuard]`), it emits a companion `namespace` block that holds the variant classes. The generated file omits imports for any symbol referenced **only** inside that nested block. The resulting `.ts` file does not type-check.

This was reproduced against a real consumer (the Vigiata project): the generated `get-user-profile-response.ts` references the type `UserProfile` and the runtime helper `valueEquals` inside the nested variant `UserProfileLoaded`, but imports neither — producing two `TS2304: Cannot find name` errors. The import that the same file needs at the top level (`HashCode`) **is** emitted correctly, which is the tell: only symbols used exclusively inside the nested block are dropped.

This is a bug fix in the Metano C# → TypeScript transpiler. The consuming project (Vigiata) and the already-correct `JsonSerializerContext` emission are out of scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nested-variant files type-check out of the box (Priority: P1)

A developer annotates an abstract record with nested record variants (the discriminated-union pattern) where a variant carries a field whose type lives in another generated file, and another variant carries a value-typed field that requires structural equality. After transpiling, the generated `.ts` file compiles with no type errors — every symbol the file references is imported, including the ones used only inside the nested companion namespace.

**Why this priority**: This is the defect. Without it, any project using the discriminated-union pattern with a typed variant produces a `.ts` file that fails `tsc`, breaking the consumer build. It is the minimum that must ship.

**Independent Test**: Transpile the reproduction C# (an abstract record with a `Loaded(SomeDto value)` variant and an empty variant, plus the referenced DTO in the same namespace) and run the TypeScript type-checker on the output. The file type-checks with zero `TS2304` errors.

**Acceptance Scenarios**:

1. **Given** an abstract record whose nested variant has a field typed as another generated type from the same package, **When** the project is transpiled, **Then** the generated file imports that type and the file type-checks.
2. **Given** a nested variant that carries a non-strict (value-equality) field, **When** the project is transpiled, **Then** the generated file imports the value-equality runtime helper used by the variant's generated equality method and the file type-checks.
3. **Given** a file where a symbol (e.g., the hash-code helper) is used both at the top level and inside a nested variant, **When** the project is transpiled, **Then** the file emits exactly one import for that symbol (no duplicates).

---

### User Story 2 - Import completeness holds for every symbol kind used in a variant (Priority: P2)

A developer writes variants that reference a broader range of symbols — a type from another package, a value reference (`new T(...)` or an `instanceof` check), a generated type guard, or an extension helper — used only inside the nested block. The generated file imports all of them.

**Why this priority**: The root cause is a traversal gap, not a `UserProfile`-specific miss. Fixing only the two reproduced symbols would leave the same class of bug latent for every other symbol kind. This story makes the fix general and prevents a follow-up round of the same defect.

**Independent Test**: Transpile a variant that references a cross-package type and a value reference used nowhere else in the file, then type-check the output. All references resolve.

**Acceptance Scenarios**:

1. **Given** a nested variant that references a type from a different `[EmitPackage]` assembly used nowhere else in the file, **When** the project is transpiled, **Then** the cross-package import is emitted with the correct module specifier.
2. **Given** a nested variant whose body contains a value reference (constructor call or `instanceof`) to a transpilable type used nowhere else in the file, **When** the project is transpiled, **Then** the value import is emitted.
3. **Given** a nested variant that references a generated type guard or an extension helper used nowhere else in the file, **When** the project is transpiled, **Then** the corresponding import is emitted.

---

### User Story 3 - Regression protection for the companion-namespace pattern (Priority: P3)

A maintainer changing the transpiler later is protected by a golden test that exercises the discriminated-union / companion-namespace pattern, so this defect cannot silently return.

**Why this priority**: The pattern currently has zero golden coverage — no expected-output fixture in the test suite contains a `namespace` block. Without a regression test the fix is one refactor away from reverting. It is essential for durability but not part of the user-visible behavior change itself.

**Independent Test**: A golden test compiles the reproduction C# and asserts the full expected `.ts` output (including the import lines for the variant-only type and the runtime helper). The test fails against today's code and passes after the fix.

**Acceptance Scenarios**:

1. **Given** the test suite, **When** it runs against the pre-fix transpiler, **Then** the new golden test fails on the missing imports.
2. **Given** the test suite, **When** it runs against the fixed transpiler, **Then** the new golden test passes.

---

### Edge Cases

- A nested variant with **only strict fields** (no value-equality field): the value-equality helper MUST NOT be imported spuriously.
- A symbol used at both the top level and inside a variant: exactly one import (deduplicated), not two.
- A variant that references a **cross-package** type used nowhere else: the import must carry the external module specifier, not an intra-project path.
- A **value reference** (e.g., `new Variant(...)` or `x instanceof Variant`) inside the nested block whose type is otherwise unused at the top level.
- **Deeper nesting** (a namespace inside a namespace, should the lowering ever produce it): traversal must reach symbols at any depth, not just one level down.
- A variant that references a **type guard** or **extension helper**: that dependency must be collected like any other.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The transpiler MUST emit an import for every external symbol referenced anywhere in a generated file, including symbols referenced only inside nested companion-namespace variant classes.
- **FR-002**: The transpiler MUST emit the runtime-helper imports required by a nested variant's generated members (e.g., the value-equality helper used by a variant's generated equality method) using the same rules applied to top-level types.
- **FR-003**: Import completeness MUST be independent of nesting depth: a symbol used only inside a nested block MUST be collected identically to the same symbol used at the top level.
- **FR-004**: The transpiler MUST NOT emit duplicate imports when a symbol is referenced at both the top level and inside a nested variant (one import per module per symbol).
- **FR-005**: The transpiler MUST NOT emit imports for runtime helpers a variant does not actually use (e.g., no value-equality import for a variant with only strict fields).
- **FR-006**: Import collection MUST cover all symbol kinds usable inside a variant: intra-project types, cross-package types, value references (constructor calls and `instanceof`), generated type guards, and extension helpers.
- **FR-007**: The fix MUST preserve all currently-correct output — top-level imports, files without nested variants, and the `JsonSerializerContext` emission — with no behavioral regressions.
- **FR-008**: The repository MUST gain golden test coverage for the abstract-record + nested-record-variant (companion-namespace) pattern, exercising at minimum (a) an intra-project type reference used only inside a variant and (b) a non-strict field that forces the value-equality runtime import.
- **FR-009**: The previously failing reproduction (an abstract record with a typed variant referencing a same-namespace DTO and a value-equality field) MUST type-check cleanly after transpilation.

### Key Entities

- **Companion namespace**: The generated `namespace` block emitted alongside a discriminated-union base type; holds the variant classes lowered from C# nested records.
- **Variant class**: A generated class inside the companion namespace, one per nested record (e.g., the empty variant and the payload-carrying variant).
- **Referenced symbol**: Any imported name a generated file depends on — an intra-project type, a cross-package type, a runtime helper, a value reference, a type guard, or an extension helper.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The reproduction output (`get-user-profile-response.ts` and its equivalents) type-checks with **zero** "cannot find name" errors after regeneration.
- **SC-002**: For the discriminated-union pattern, **100%** of symbols referenced only inside nested variants that require an import are imported (verified by the type-checker passing on representative samples).
- **SC-003**: The existing test suite (337+ tests) and all sample-project builds continue to pass — **zero** regressions introduced by the fix.
- **SC-004**: A new golden test for the companion-namespace pattern **fails before** the fix and **passes after** it.
- **SC-005**: No file gains a duplicate import line, and no variant-only file gains an unused-helper import (verified by exact golden-output assertions).

## Assumptions

- This is a bug fix in the Metano C# → TypeScript transpiler (core + TypeScript target). The Vigiata project is only a consumer of regenerated output and is **out of scope**; it requires no source change beyond re-running the transpiler.
- The `JsonSerializerContext` emission (`contracts-serializer-context.ts`) is already correct and is **out of scope** — it imports its dependencies properly today.
- The companion-namespace lowering itself (the structure and members of the generated `namespace` and its variant classes) is correct; only the import-collection step is defective.
- The defect spans two independent transpiler paths — the type-reference import walker and the runtime-requirement scanner — and a complete fix must address both, since either alone leaves part of the reproduction broken.
- The TypeScript target is the focus. The Dart target is shape-only (no bodies) and is not affected by this defect.
- Per project convention, the governing import-completeness functional requirement already exists in the baseline spec; this feature corrects its implementation rather than introducing new product behavior. The exact baseline FR to cross-reference will be confirmed during planning.
