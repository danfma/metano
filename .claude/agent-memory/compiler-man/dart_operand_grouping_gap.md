---
name: dart-operand-grouping-gap
description: Dart printer grouping of conditional/throw operands was missed by TS-only probes in #287; always dart-analyze probes for core lowerings
metadata:
  type: project
---

When a core-extractor lowering produces `IrConditionalExpression` or `IrThrowExpression` as a binary operand, the TS printer groups it correctly but the Dart printer does not (`DartBinaryOperandGrouping` inspects only `IrBinaryExpression` children). Found reviewing #287 (2026-10-31): `(a + 1) ?? b` → `a == null ? null : a + 1 ?? b` (wrong value), and `x ?? throw E` is a Dart syntax error (Dart needs `?? (throw E)`). Fixed in #287 (`DartBinaryOperandGrouping.BindsLooserThanAnyOperator`); the guard/let closures remain untyped `dynamic`.

**Why:** TS-only probes (bun/tsgo) pass while Dart output silently breaks; Dart guard closures (`(($receiver) => ...)(x)`) and BindOnce IIFEs are also untyped (`dynamic`) parameters in Dart, so extension methods would fail at runtime.

**How to apply:** For any extractor change that emits conditional/throw/lambda operands, transpile a probe with the Dart CLI and run `dart analyze` + `dart run`, not just the TS side. Suggest Dart `switch (x) { final r? => ..., _ => null }` for typed guards. See [[explicit-interface-lowering]] for the sample-regen rule.
