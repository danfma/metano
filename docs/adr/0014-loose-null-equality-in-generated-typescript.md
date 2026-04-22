# ADR-0014 — Loose null equality in generated TypeScript

**Status:** Accepted
**Date:** 2026-04-22

## Context

C# has a single "missing value" concept — `null`. JavaScript / TypeScript
has two: `null` (explicit absence) and `undefined` (not assigned). A
transpiled pipeline that emits strict `=== null` / `!== null` checks in
generated TS breaks whenever a JS-side value crosses the boundary as
`undefined` — optional object properties omitted at construction, array
elements past the declared length, sparse deserialization — because the
strict comparison fails to recognize those states as "equivalent to
null".

Concretely, `SampleTodo.Service` emits handlers that read the request
body and compare fields against `null`. Hono delivers request bodies
parsed from JSON; a client that sends `{}` (omitting a key) produces a
JS object where the key reads back as `undefined`, not `null`. With the
pre-existing `=== null` check, the guard failed and downstream code ran
with an undefined value the C# source assumed was absent.

Kotlin/JS — the closest-kin compile-to-JS ecosystem — faces the exact
same asymmetry and resolves it by emitting loose `== null` for nullable
comparisons, treating `null` and `undefined` as interchangeable at the
compare site. The referential `=== undefined` form stays available for
the rare case that genuinely needs to distinguish them.

## Decision

Generated TypeScript uses loose `==` / `!=` when comparing against a
`null` literal, and strict `===` / `!==` for every other comparison.
The emission site is `IrToTsExpressionBridge.BinaryOperatorToken`, which
now inspects the operator's operands and relaxes `IrBinaryOp.Equal` /
`NotEqual` to loose form exactly when either operand is an
`IrLiteral { Kind: Null }`. `is null` / `is not null` pattern forms
route through the same helper so the contract stays single-sourced.

Examples:

```ts
// C#: value is null          →  value == null
// C#: value is not null      →  !(value == null)
// C#: value == null          →  value == null
// C#: arr.Length == 3        →  arr.length === 3  (unchanged: not a null compare)
```

C# itself has no `undefined` concept; the relaxation only broadens the
JS-visible check and does not affect round-trip semantics. Targets
other than TypeScript are unaffected — the bridge is TS-specific.

## Consequences

- (+) TS consumers that produce `undefined` (omitted optional property,
      sparse JSON, React component props defaulting to the type's
      default) no longer silently bypass C#-authored null guards.
- (+) Opens the door to a future `[Optional]` attribute (issue #23)
      that emits `name?: T | null` at interface boundaries — consumers
      can genuinely omit the key, and the generated C#-authored reads
      still behave correctly.
- (+) Mirrors Kotlin/JS's established convention, reducing the surprise
      for developers who bring that mental model.
- (−) Generated code uses `==` / `!=` which some TypeScript style
      guides discourage. Biome and TypeScript ESLint both exempt
      `== null` / `!= null` specifically because the "sloppy" behavior
      is the desired behavior at this one comparison.
- (−) Developers reading the generated output who are not aware of the
      distinction might assume the emitter is "careless". The lowering
      is deliberate; the ADR documents the reasoning.
- (−) One-time sample churn: every `=== null` / `!== null` in
      previously-generated TS turns into `==` / `!=`. Follow-up
      regeneration is a mechanical rewrite the test suite covers.

## Alternatives considered

- **Strict `===` everywhere** — the prior state. Breaks any C# read
  of a JS-produced `undefined` value. Rejected.
- **Attribute-scoped relaxation** (`[Optional]`-tagged fields only) —
  would require threading the attribute through every binary
  comparison in the expression bridge and still leave non-attributed
  nullable compares exposed to the same hazard. Rejected as both
  invasive and partial.
- **Runtime boundary normalization** (coerce `undefined` → `null` in a
  deserialization helper) — fixes the specific JSON parse path but
  misses every other boundary (direct TS calls, React props,
  destructured arguments). Kept as a possible future layer on top of
  the loose-equality emission for strictly-typed round trips.

## References

- Code pointer: `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.cs`
  (`BinaryOperatorToken`, `IsNullCompare`, `MapIsPattern`,
  `BuildPatternTest`).
- Tests: `tests/Metano.Tests/SwitchPatternTranspileTests.cs`
  (`IsNull_GeneratesLooseEquality`, `IsNotNull_GeneratesNegatedLooseEquality`),
  `tests/Metano.Tests/IR/IrToTsBodyBridgeTests.cs`
  (`IsNullPattern_LowersToLooseEquality`, `NotPattern_LowersToNegation`).
- Related issue: [#23 `[Optional]` attribute for nullable-as-optional
  opt-in](https://github.com/danfma/metano/issues/23) — this ADR is a
  prerequisite.
- External: Kotlin/JS treats nullable types as `T | null | undefined`
  and uses loose equality at the JVM/JS interop boundary for the same
  reason (Kotlin/JS documentation on JS interop and the `kotlin.js`
  package).
