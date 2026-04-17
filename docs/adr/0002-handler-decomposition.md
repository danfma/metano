# ADR-0002 — Handler decomposition (not formal GoF Visitor)

**Status:** Accepted
**Date:** 2026-02-20

## Context

Even after the core/target split ([ADR-0001](0001-target-agnostic-core.md)),
the TypeScript target's `TypeTransformer.cs` and `ExpressionTransformer.cs`
were still God Objects — 2826 and 973 lines respectively, with every C#
sub-grammar inlined as a `switch` case or method. Adding support for a new
construct (e.g. `switch` expression, pattern matching) required editing the
same monster file. Unit-testing a single lowering in isolation was
impossible; a test always pulled in the whole transformer.

Two canonical refactor shapes were on the table: the **GoF Visitor** (with
`IVisitor<T>` double dispatch) and an **ad-hoc handler decomposition**
(each sub-grammar becomes its own class, composed through the parent).

## Decision

Decompose `TypeTransformer` and `ExpressionTransformer` into focused
handler classes, one per C# sub-grammar with non-trivial lowering. Not a
formal GoF Visitor — we don't need double dispatch, and introducing
`IVisitor<T>` would have added ceremony without buying anything. Handlers
take the parent transformer (or `TypeScriptTransformContext`) as a
dependency and compose through lazy properties on the parent:
`SwitchHandler` reaches `parent.Patterns` for `PatternMatchingHandler`,
etc.

Only trivial 1-line AST renames (parenthesized, ternary, cast, `await`,
`this`, element access) stay inline in the parent. Everything else gets
its own file.

Representative handlers extracted:

- **Types** (from `TypeTransformer`): `BarrelFileGenerator`,
  `RecordSynthesizer`, `TypeGuardBuilder`, `OverloadDispatcherBuilder`,
  `EnumTransformer`, `InterfaceTransformer`, `ExceptionTransformer`,
  `InlineWrapperTransformer`, `ModuleTransformer`, `RecordClassTransformer`,
  `ImportCollector`, `PathNaming`.
- **Expressions** (from `ExpressionTransformer`): `PatternMatchingHandler`,
  `SwitchHandler`, `LambdaHandler`, `ObjectCreationHandler`,
  `MemberAccessHandler`, `InvocationHandler`, `InterpolatedStringHandler`,
  `OptionalChainingHandler`, `CollectionExpressionHandler`,
  `OperatorHandler`, `StatementHandler`, `ThrowExpressionHandler`,
  `ArgumentResolver`.

## Consequences

- (+) `TypeTransformer.cs`: **2826 → 466 lines (-83.5%)**. Now it's a thin
  router + shared state holder.
- (+) `ExpressionTransformer.cs`: **973 → 174 lines (-82.1%)**. Same shape.
- (+) Each handler is unit-testable in isolation — construct it with a
  minimal fake context and assert on the AST it produces.
- (+) Adding a new C# construct = new handler file + one wire-up line in
  the parent. No surgery on anything existing.
- (+) Handlers compose naturally via internal properties, so ordering
  between `Switch` and `PatternMatching` is explicit in the type system.
- (−) 30+ new files to navigate. The upfront discovery cost for a new
  contributor is higher than a single big file.
- (−) The router parent still wires handlers explicitly — no automatic
  discovery. Worth the trade-off (explicit > magic), but it's a cost.

## Alternatives considered

- **Formal GoF Visitor with `IVisitor<T>`**: rejected. Double dispatch
  solves a problem we don't have (adding operations without modifying the
  type hierarchy), and adds `Accept` methods on every AST node. Lazy
  properties on the parent give us the same composition without the
  ceremony.
- **Pipeline of independent passes (Rust/LLVM style)**: rejected. Ordering
  constraints between passes are complex, and each pass would need to
  thread the same shared state (compilation, symbol maps, diagnostics)
  anyway. The handler model keeps composition local and the shared state
  in one place.

## References

- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs` (466
  lines, down from 2826)
- `src/Metano.Compiler.TypeScript/Transformation/ExpressionTransformer.cs`
  (174 lines, down from 973)
- `src/Metano.Compiler.TypeScript/Transformation/TypeScriptTransformContext.cs`
  — the immutable shared state handed to every handler
- 30+ handler files under `src/Metano.Compiler.TypeScript/Transformation/`

## Post-refactor note (2026-04)

The handler decomposition pattern survives intact — it's still the shape
of the TypeScript target — but the handlers migrated from consuming
Roslyn directly to consuming the shared IR introduced by
[ADR-0013](0013-shared-ir-as-canonical-semantic-representation.md). The
TS-specific "transformer" classes listed above were either renamed to
`IrTo*Bridge` and relocated to `src/Metano.Compiler.TypeScript/Bridge/`,
or folded into `IrToTsClassEmitter` (the surviving orchestrator):

| Retired class | Replacement |
| --- | --- |
| `EnumTransformer` | `IrToTsEnumBridge` |
| `InterfaceTransformer` | `IrToTsInterfaceBridge` |
| `ExceptionTransformer` | `IrToTsExceptionBridge` |
| `InlineWrapperTransformer` | `IrToTsInlineWrapperBridge` |
| `ModuleTransformer` | `IrToTsModuleBridge` |
| `RecordClassTransformer` | `IrToTsClassEmitter` (orchestrator) + `IrToTsClassBridge` (helpers) |
| `RecordSynthesizer` | `IrToTsRecordSynthesisBridge` |
| `OverloadDispatcherBuilder` | `IrToTsOverloadDispatcherBridge` + `IrToTsConstructorDispatcherBridge` |
| `TypeCheckGenerator` | `IrTypeCheckBuilder` |
| `BclMapper` | `IrToTsBclMapper` |
| `TypeMapper` | `IrToTsTypeMapper` |
| `ExpressionTransformer` + 13 child handlers | `IrToTsExpressionBridge` + `IrToTsStatementBridge` |

The decision recorded here (decompose over monolith, compose via parent
properties, no formal Visitor) was ratified by the Dart target — the same
shape now lives in `src/Metano.Compiler.Dart/Bridge/` with no TypeScript
contamination.
