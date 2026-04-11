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

## References

- `src/Metano.Compiler.TypeScript/Transformation/TypeGuardBuilder.cs`
- `src/Metano.Compiler.TypeScript/TypeScript/AST/TsTypePredicateType.cs`
- `src/Metano/Annotations/GenerateGuardAttribute.cs`
- `js/metano-runtime/src/system/primitives/` — `isInt32`, `isString`,
  etc.
- `tests/Metano.Tests/TypeGuardTranspileTests.cs`
- [Issue #24](https://github.com/danfma/metano/issues/24) — `assertX`
  throwing variant + discriminated unions (tracked follow-ups).
