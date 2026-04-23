# ADR-0009 — Type guards as shape validation with `instanceof` fast path

**Status:** Accepted
**Date:** 2026-03-15

## Context

Generated TypeScript code often has to narrow a runtime value to a
specific type before using it:

- Deserializing JSON from an untrusted source (network, filesystem,
  user input).
- Dispatching method overloads by argument type (see
  [ADR-0008](0008-overload-dispatch.md)).
- Accepting `unknown` at a trust boundary and proving the value is
  safe to use.
- Writing library code that takes `value: unknown` and returns a typed
  result.

TypeScript's structural type system doesn't answer "is this value a
`Money`?" at runtime. `instanceof` only works for values produced by
the class's constructor — it fails for plain objects deserialized from
JSON (which lost their prototype), for interface-typed values (no
runtime class at all), for string enums (value literals, not
instances), and for nested or generic types.

Two paths were on the table:

1. **Generated shape-validation functions per type.** The compiler
   walks the type's fields and emits a guard that checks each one at
   runtime.
2. **A schema validation library** (Zod, io-ts, Yup, Valibot). Users
   import the library and write schemas that parallel their types.

Option 2 introduces a hard runtime dependency and a second type system
that must stay in sync with the transpiled types by hand. The whole
point of Metano is that the types and the validation both derive from
the same C# source. Path (1) matches that promise.

## Decision

Generate `is{TypeName}(value: unknown): value is TypeName` guard
functions per transpilable type. Opt-in via `[GenerateGuard]` on the C#
type, or globally via the `--guards` CLI flag. Each guard combines two
strategies:

1. **`instanceof` fast path.** When the type lowers to a class with a
   runtime prototype (records, exceptions, explicit classes), the
   guard starts with `value instanceof TypeName`. For a live instance,
   this returns `true` immediately and the guard is done.
2. **Shape validation fallback.** When the fast path fails (or the type
   has no runtime class — interfaces, string enums, JSON-deserialized
   values), the guard walks every field:
   - Primitives use the runtime type-check helpers from
     `metano-runtime` (`isInt32`, `isString`, `isBool`, `isBigInt`,
     `isFloat64`, …).
   - Transpiled types recurse into their own generated guard
     (`isCurrency`, `isMoney`).
   - String enums validate against the literal set
     (`value === "BRL" || value === "USD" || …`).
   - Numeric enums check `typeof value === "number"` plus the valid
     value set.
   - Nullable fields prepend `v.field == null ||` so `null` and
     `undefined` are both accepted.
   - Inheritance composes: a derived type's guard validates the base
     type's fields first, then its own.

`[ExportedAsModule]` static classes and exception types skip guard
generation — the first is a namespace of functions with no instance
shape, the second has the runtime class for `instanceof` already and
no additional fields worth shape-checking.

Cross-file guard imports are resolved automatically. If `Money.ts`
references `Currency` via a shape check, the import collector pulls in
`isCurrency` from the Currency module and re-exports it through the
namespace barrel. The `TsTypePredicateType` AST node carries the
`value is TypeName` predicate syntax for the Printer.

## Consequences

- (+) Zero external dependency. Guard code is plain TypeScript plus the
  primitive checks from `metano-runtime`. Nothing to install, nothing
  to configure.
- (+) The generated guard always matches the C# type. Add a field,
  regenerate, and the guard picks it up automatically — no schema to
  update.
- (+) `instanceof` fast path keeps live instances cheap. Only raw /
  deserialized values pay the shape-validation cost.
- (+) Cross-file composition works without manual wiring. The import
  collector chains guards through the namespace barrels the same way
  it chains type imports.
- (+) Guards are ordinary functions, so they tree-shake if a consumer
  doesn't reference them.
- (−) Shape validation is structural, not nominal. Two unrelated types
  with identical fields are indistinguishable from a shape guard.
  Acceptable because the workaround is already available: users who
  want nominal identity at the TS boundary should use
  [`[InlineWrapper]`](0005-inline-wrapper-branded-types.md) /
  branded types. Shape guards are for "is this value safe to treat as
  T" (parsing), not "was this value produced by T's constructor"
  (which is better answered by `instanceof`).
- (−) Guards for deep types get long. A type with many nested types
  produces a long chain of recursive checks. Mitigated by the fact
  that each check is a plain function call and the JIT inlines
  aggressively. Not a hot-path concern in the samples so far.
- (−) Two future refinements are tracked but not implemented:
  `assertX(value): X` (throws on failure instead of returning a
  boolean) and discriminated unions (when a type has an enum field
  that can act as a discriminant, the guard could narrow by the
  discriminant first and skip the rest). Both are tracked as
  [issue #24](https://github.com/danfma/metano/issues/24).

## Alternatives considered

- **Zod / io-ts / Valibot**: rejected. External dependency, parallel
  type system to maintain, and the compiler would still need to emit
  the schema from the C# type — defeating the point.
- **Only `instanceof` checks**: rejected. Fails for deserialized
  values, interfaces, and string enums, which are the cases where
  guards are actually needed.
- **Runtime reflection via TS metadata** (`emitDecoratorMetadata`,
  `reflect-metadata`): rejected. TS doesn't emit enough metadata by
  default, and enabling it couples the build to tsc quirks that differ
  from esbuild / Bun / tsgo.

## Addendum (2026-04-22) — `assertT` throwing companion

Every `[GenerateGuard]` type now emits a second function alongside
`isT`: `assertT(value: unknown, message?: string): asserts value is T`.
The body is a thin wrapper that negates `isT` and throws
`TypeError(message ?? "Value is not a TName")`. Consumers use `isT` in
conditional branches and `assertT` at trust boundaries (parsing JSON,
accepting `unknown` from a network handler, validating request
bodies) where a missed narrowing should fail loudly.

Kept inline — no `metano-runtime` helper. The assertion is four lines
of TS; importing a helper would couple every guarded type to the
runtime package and defeat the zero-dep / tree-shakable trade-off
accepted above. Error messages include the TS-facing type name (so
`[Name(TypeScript, "Ticker")]` surfaces as `"Value is not a Ticker"`,
matching the name consumers actually see in the generated module).

Field-path threading ("Money.amount was not a number") is **not**
included in the initial emission — the existing shape check builds a
single AND-chain expression, and surfacing which conjunct failed
requires rewriting the chain into sequential `if (!check) throw`
statements. Deferred until users ask for it; the basic error message
already covers the common "reject and surface" case at boundaries.

Shipping `assertT` also surfaced a latent bug that had no user today:
the `instanceof` fast path was emitted unconditionally for records,
including `[PlainObject]` records that lower to bare TS interfaces
(no class at runtime). The fast path now skips when
`SymbolHelper.HasPlainObject(type)` is true — shape validation alone
narrows those correctly.

## Addendum (2026-04-23) — `[Discriminator("FieldName")]` short-circuit

Types annotated with `[Discriminator("FieldName")]` (from
`Metano.Annotations.TypeScript`) short-circuit the generated guard on
a nominated field before walking the rest of the shape. The check
emits as a literal comparison — `if (v.kind !== "TypeName") return
false;` — placed immediately after the null/object gate. When the
discriminator matches, the remaining field checks still run so
reserved fields stay validated.

The expected discriminator value follows a **type-name convention**:
the emitted check compares against the type's TS name (after
`[Name(TypeScript, …)]` resolution). A `Circle` class tagged
`[Discriminator("Kind")]` expects `v.kind === "Circle"`; a class
renamed via `[Name(TypeScript, "Round")]` expects `v.kind === "Round"`.
This keeps the attribute surface minimal — no second argument for the
expected value — at the cost of requiring the enum member name to
match the type name. The convention is enforced at runtime (a
mismatch rejects every payload); the frontend validator only checks
field shape, not enum-member-to-type-name alignment.

Frontend validator emits `MS0011` when the `[Discriminator]` field is
missing on the type, not a `[StringEnum]`, or nullable — any of those
would produce a guard that can't narrow safely. The short-circuit
lowering runs after the null-safety gates so the discriminator access
is always on an object, and the field itself has no null guard of its
own.

The discriminator field is also **skipped from the general field
loop** — the literal comparison above is strictly tighter than the
`isKind(v.kind)` recursive call the default emission would produce,
so re-checking would be dead code and drag in an extra runtime import
for the enum guard.

The sealed-hierarchy follow-up (issue #24, part 3 — union type guard
that switches on a shared discriminant across multiple subtypes) stays
out of scope. Metano doesn't model sealed hierarchies at the IR layer
today; adding one is a separate design conversation.

## References

- `src/Metano.Compiler.TypeScript/Transformation/TypeGuardBuilder.cs` —
  `Generate` returns `[isT, assertT]`; `GenerateAssert` builds the
  throwing companion; `GenerateShapeGuard` emits the discriminator
  short-circuit when `[Discriminator]` is present and filters the
  field out of the remaining shape loop.
- `src/Metano.Compiler.TypeScript/TypeScript/AST/TsTypePredicateType.cs` —
  extended with an `IsAsserts` flag for `asserts value is T` emission.
- `src/Metano/Annotations/GenerateGuardAttribute.cs`
- `src/Metano/Annotations/TypeScript/DiscriminatorAttribute.cs` —
  TS-specific attribute, namespaced away from cross-target annotations.
- `src/Metano.Compiler/CSharpSourceFrontend.cs` —
  `ValidateDiscriminatorAttribute` raises MS0011 for invalid uses.
- `targets/js/metano-runtime/src/type-checking/primitive-type-checks.ts` —
  `isInt32`, `isString`, etc.
- `tests/Metano.Tests/TypeGuardTranspileTests.cs` — isT + assertT +
  discriminator matrix.
- `targets/js/sample-todo-service/test/guards.test.ts` — bun test
  exercising `assertCreateTodoDto` at a mock trust boundary.
- `targets/js/sample-todo-service/test/events.test.ts` — bun test
  demonstrating discriminator-based narrowing between `TodoCreated`
  and `TodoUpdated` variants that share the `TodoEventKind` enum.
- [Issue #24](https://github.com/danfma/metano/issues/24) — `assertX`
  throwing variant (shipped) + `[Discriminator]` short-circuit
  (shipped) + sealed-hierarchy union guard (deferred).
