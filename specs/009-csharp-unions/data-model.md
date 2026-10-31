# Phase 1 Data Model: C# 15 union declarations

This feature changes compiler behavior. The "data model" is the set of IR additions, the runtime descriptor, and the rules that tie them together. All IR additions are additive. Every new field is optional, so existing constructors and goldens are unchanged.

## Entity: Union shape (Roslyn adapter, core)

`UnionShapeReader` is the only reader of `RSEXPERIMENTAL006` APIs.

| Member | Result |
|---|---|
| `IsUnion(INamedTypeSymbol)` | `type.IsUnion`. True for declared unions and for hand-written `[Union] struct/class : IUnion`. False for a plain `IUnion` struct. |
| `IsCompilerGenerated(INamedTypeSymbol)` | True for a source union declared with `UnionDeclarationSyntax`, or a metadata union whose case constructors all carry `[CompilerGenerated]`. Every other union is hand-written and gets MS0032 (FR-005). |
| `ReadCases(INamedTypeSymbol)` | The parameter types of the public, non-static, single-parameter instance constructors, in member order. |
| `ReadShapeViolations(INamedTypeSymbol)` | Everything outside the allowlist (`Value`, `get_Value`, case ctors, the parameterless ctor, the backing field), plus any interfaces other than `IUnion`. The allowlist matches by name and kind, because metadata members are `IsImplicitlyDeclared = false`. It runs only on compiler-generated unions, so a name match cannot hide user code. |
| `ReadCases(INamedTypeSymbol constructed)` | For a constructed generic union (`Opt<int>`), the cases after type-argument substitution. The validator runs MS0032 / MS0033 on these once per constructed instance (FR-031). |

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
- The record has no `Members`. Body members are rejected (MS0032, OD-5).

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
| `IrSwitchCase` | `IReadOnlyList<IrSwitchLabel>? PatternLabels` | Holds `case T x:` / `case > 5:` labels, each an `IrSwitchLabel(IrPattern Pattern, IrExpression? When)`. A section can mix constant labels (kept in `Labels`) and pattern labels; its test is the OR of all of them (FR-021). When both lists are empty, the section is `default`; every consumer must check `PatternLabels` before treating an empty `Labels` list as `default`. |
| `IrBreakStatement` | `bool TargetsSwitch` (default `false`) | Set when Roslyn's `IBranchOperation` target is the enclosing switch. Backends that lower a pattern switch to a labeled block render it as `break $switch` (FR-021). |

## Entity: Exhaustive-tail predicate

The tail is emitted (FR-020) only when all four conditions hold:

1. `ScrutineeType` (with any `Nullable<>` / annotation stripped) is a named type with `Kind == Union`.
2. Every arm before the catch-all is an unguarded `IrTypePattern` whose type equals a distinct case, or an `IrConstantPattern(null)`.
3. There is no catch-all arm.
4. One of these is true:
   - neither the scrutinee nor any case is nullable;
   - a `null` arm exists.

## Entity: Erased value domain (core, validation)

Derived from the FR-019 classification plus the type's TS mapping; used only by MS0033 (FR-013).

| Domain | Case types |
|---|---|
| `number` | numeric primitives, numeric enums, `[Branded]` over a number |
| `string` | `string`, `char`, `Guid`, `[StringEnum]`, `[Branded]` over a string |
| `boolean` | `bool` |
| `bigint` | types that map to TS `bigint` |
| `function` | delegates |
| `array` | types that erase to a JS array (`T[]`, `List<T>`, …) |
| `instanceof C` | transpilable classes and records, temporal types, `decimal`, `[Import]` / `[External]` classes, the runtime `HashSet` |
| `structural` | guarded interfaces and `[PlainObject]` types |

Two cases overlap when they share a primitive domain, both are `array`, one class derives from the other, either is `structural` next to another `structural` or `instanceof` case, or one converts implicitly to the other.

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

The token is the one **STJ** writes for the case under the context's options (FR-037, research R10), not the token Metano's own descriptor uses.

| Case type | `token` |
|---|---|
| number primitives, `decimal`, numeric enums | `number` |
| an enum with `JsonStringEnumConverter` (on the enum or the context) | `string` |
| `string`, `Guid`, `Uri`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `[Branded]` over a string | `string` |
| `bool` | `boolean` |
| arrays (except `byte[]`), lists, sets | `array` |
| record / class, `Dictionary<string, V>`, `[PlainObject]` | `object` |
| `nullable<T>` | the token of `T`. The null token is accepted for every union anyway. |
| `byte[]`, `char`, a `[StringEnum]` enum without a string-enum converter | not reproduced: MS0032 when JSON-reachable (Metano's descriptor writes a different token from STJ) |

Under `AllowReadingFromString` (including `JsonSerializerDefaults.Web`), a `number` case is a read candidate for both `number` and `string` tokens. `WriteAsString` and `AllowNamedFloatingPointLiterals` with a number case get MS0032.

**Rules**
- The descriptor is built inline for each union use (including each constructed generic instance), never as an object spec. `name` is STJ's display name (`Id`, ``GOpt`1[Id]``).
- The contract closure queues every object-kind case through `ChildTypesOf(Union)` (FR-030).
- Write order equals case order. The first case whose `test` matches wins. MS0033 guarantees that at most one case matches.
- Read candidates are the cases whose `token` equals the input's token kind. The messages are listed in [contracts/json-wire.md](./contracts/json-wire.md).

## Entity: Diagnostics

| Code | Name | Severity | Raised by |
|---|---|---|---|
| `MS0032` | `UnsupportedUnionShape` | Error | `UnionShapeValidator` (declarations and constructed instances); `IrExpressionExtractor` for `IUnion` access; the JSON transformer for classifier, polymorphic, dictionary-key, token-parity and undescribable cases |
| `MS0033` | `IndistinguishableUnionCases` | Error | `UnionShapeValidator` (declarations and constructed instances) |
| `MS0034` | `UntestableUnionCase` | Error | `UnionShapeValidator` (declaration); extractor (open `T` use site) |
| `MS0035` | `UnionDefaultValue` | Error (OD-3) | `IrExpressionExtractor` |
| `MS0036` | `AmbiguousUnionJsonCases` | Warning | `JsonSerializerContextTransformer` |
| `MS0037` | `UnionErasureDivergence` | Warning | `IrExpressionExtractor` |

The full reason list is in [contracts/diagnostics.md](./contracts/diagnostics.md).
