# Phase 1 Data Model: C# 15 union declarations

This feature changes compiler behavior. The "data model" is the set of IR additions, the runtime descriptor, and the rules that tie them together. All IR additions are additive. Every new field is optional, so existing constructors and goldens are unchanged.

## Entity: Union shape (Roslyn adapter, core)

`UnionShapeReader` is the only reader of `RSEXPERIMENTAL006` APIs.

| Member | Result |
|---|---|
| `IsUnion(INamedTypeSymbol)` | `type.IsUnion`. True for declared unions and for hand-written `[Union] struct/class : IUnion`. False for a plain `IUnion` struct. |
| `ReadCases(INamedTypeSymbol)` | The parameter types of the public, non-static, single-parameter instance constructors, in member order. |
| `ReadShapeViolations(INamedTypeSymbol)` | Everything outside the allowlist (`Value`, `get_Value`, case ctors, the parameterless ctor, the backing field), plus any interfaces other than `IUnion`. The allowlist matches by name and kind, because metadata members are `IsImplicitlyDeclared = false`. |

**Rules**
- Source and metadata symbols yield the same cases in the same order (NFR-005).
- Generic unions return their type parameters as cases (`Opt<T>` → `[T, None]`).

## Entity: `IrUnionDeclaration` (IR)

```csharp
public sealed record IrUnionDeclaration(
    string Name,
    IrVisibility Visibility,
    IReadOnlyList<IrTypeRef> Cases,
    IReadOnlyList<IrTypeParameter>? TypeParameters = null,
    IReadOnlyList<IrAttribute>? Attributes = null
) : IrTypeDeclaration(Name, Visibility, TypeParameters, Attributes);
```

**Rules**
- `Cases` keeps declaration order. A nullable case is an `IrNullableTypeRef`.
- The record has no `Members`. Body members are rejected (MS0031, OD-5).

## Entity: Union reference semantics (IR)

| Field | Description |
|---|---|
| `IrNamedTypeKind.Union` | New enum member. It sits before `Unknown`, and switches over the enum must handle it explicitly. |
| `IrNamedTypeSemantics.UnionCases` | `IReadOnlyList<IrTypeRef>?`. Set only when `Kind == Union`, following the `StringEnumValues` precedent. |

**Rules**
- `IrTypeRefMapper.BuildNamedTypeSemantics` checks for a union before the Branded, Record and Struct arms.
- A visited set guards self-referential cases such as `union U(List<U>, int)`.
- The default value of a union-typed slot is `null`, never `new U()`.

## Entity: Switch subject typing (IR)

| Record | New optional field | Use |
|---|---|---|
| `IrSwitchExpression` | `IrTypeRef? ScrutineeType` | Decides whether to emit the FR-020 `never` tail. The field carries nullability. |
| `IrSwitchCase` | `IrPattern? Pattern`, `IrExpression? When` | Holds `case T x:` labels. When `Pattern` is set, `Labels` is empty, and every consumer must check `Pattern` before treating an empty `Labels` list as `default`. |

## Entity: Exhaustive-tail predicate

The tail is emitted (FR-020) only when all four conditions hold:

1. `ScrutineeType` (with any `Nullable<>` / annotation stripped) is a named type with `Kind == Union`.
2. Every arm before the catch-all is an unguarded `IrTypePattern` whose type equals a distinct case, or an `IrConstantPattern(null)`.
3. There is no catch-all arm.
4. One of these is true:
   - neither the scrutinee nor any case is nullable;
   - a `null` arm exists.

## Entity: `UnionDescriptor` (runtime, `metano-runtime/system/json/types.ts`)

```ts
export type JsonTokenKind = "null" | "number" | "string" | "boolean" | "array" | "object";

export type UnionCaseDescriptor = {
  readonly token: Exclude<JsonTokenKind, "null">;
  readonly test: (value: unknown) => boolean;
  readonly descriptor: () => TypeDescriptor;   // thunk: avoids eager recursion through `base`
};

export type UnionDescriptor = {
  readonly kind: "union";
  readonly name: string;                       // the C# union name, used in STJ messages
  readonly cases: readonly UnionCaseDescriptor[];
};
```

**Token-kind table (case type → `token`)**

| Case type (as classified by the context transformer) | `token` |
|---|---|
| `primitive` number types, `decimal`, `numericEnum` | `number` |
| `string`, `char`, `Guid` (branded), `temporal`, `enum` (`[StringEnum]`), `[Branded]` over string | `string` |
| `bool` | `boolean` |
| `array`, `hashSet` | `array` |
| `ref` (record/class), `map`, `[PlainObject]` | `object` |
| `nullable<T>` | the token of `T`. The null token is accepted for every union anyway. |

**Rules**
- Write order equals case order. The first case whose `test` matches wins. MS0032 guarantees that at most one case matches.
- Read candidates are the cases whose `token` equals the input's token kind. The messages are listed in [contracts/json-wire.md](./contracts/json-wire.md).

## Entity: Diagnostics

| Code | Name | Severity | Raised by |
|---|---|---|---|
| `MS0031` | `UnsupportedUnionShape` | Error | `UnionShapeValidator`; the JSON transformer for classifier and polymorphic cases |
| `MS0032` | `IndistinguishableUnionCases` | Error | `UnionShapeValidator` |
| `MS0033` | `UntestableUnionCase` | Error | `UnionShapeValidator` (declaration); extractor (open `T` use site) |
| `MS0034` | `UnionDefaultValue` | Error (OD-3) | `IrExpressionExtractor` |
| `MS0035` | `AmbiguousUnionJsonCases` | Warning | `JsonSerializerContextTransformer` |
| `MS0036` | `UnionErasureDivergence` | Warning | `IrExpressionExtractor` |

The full reason list is in [contracts/diagnostics.md](./contracts/diagnostics.md).
