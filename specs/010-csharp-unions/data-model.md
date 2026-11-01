# Phase 1 Data Model: C# 15 union declarations

This feature changes compiler behavior. The "data model" is the set of IR additions, the runtime descriptor, and the rules that tie them together. All IR additions are additive. Every new field is optional, so existing constructors and goldens are unchanged.

## Entity: Union shape (Roslyn adapter, core)

`UnionShapeReader` is the only reader of `RSEXPERIMENTAL006` APIs.

| Member | Result |
|---|---|
| `IsUnion(INamedTypeSymbol)` | `type.IsUnion`. True for declared unions and for hand-written `[Union] struct/class : IUnion`. False for a plain `IUnion` struct. |
| `IsCompilerGenerated(INamedTypeSymbol)` | True for a source union declared with `UnionDeclarationSyntax`, or a metadata union whose case constructors all carry `[CompilerGenerated]`. Every other union is hand-written and gets MS0038 (FR-005). |
| `ReadCases(INamedTypeSymbol)` | The parameter types of the public, non-static, single-parameter instance constructors, in member order. |
| `ReadBodyMembers(INamedTypeSymbol)` | The user members outside the compiler-generated allowlist (`Value`, `get_Value`, case ctors, the parameterless ctor, the backing field): methods, get-only properties, static fields, nested types and the implementations of extra interfaces, plus the extra interfaces themselves (everything other than `IUnion`). The allowlist matches by name and kind, because metadata members are `IsImplicitlyDeclared = false`. It runs only on compiler-generated unions, so a name match cannot hide user code. These members feed the companion namespace (FR-042). |
| `ReadShapeViolations(INamedTypeSymbol)` | The body member kinds the companion namespace does not lower (indexers, events, operators, user-defined conversions, static constructors, instance setters / `init`, `Equals` / `GetHashCode` overrides): MS0038 (FR-012). |
| `ReadCases(INamedTypeSymbol constructed)` | For a constructed generic union (`Opt<int>`), the cases after type-argument substitution. The validator runs MS0038 / MS0039 on these once per constructed instance (FR-031). |

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
    IReadOnlyList<IrAttribute>? Attributes = null,
    IReadOnlyList<IrMemberDeclaration>? Members = null,      // body members (FR-042)
    IReadOnlyList<IrTypeDeclaration>? NestedTypes = null,    // nested types (FR-042)
    IReadOnlyList<IrTypeRef>? Interfaces = null              // extra interfaces, never IUnion
) : IrTypeDeclaration(Name, Visibility, TypeParameters, Attributes);
```

**Rules**
- `Cases` keeps declaration order. A nullable case is an `IrNullableTypeRef`.
- `Members`, `NestedTypes` and `Interfaces` follow `IrClassDeclaration`'s field names. They are `null` for a union without a body, and then no companion namespace is emitted (FR-042). Member kinds outside FR-042's list never reach the IR (MS0038).

## Entity: Union reference semantics (IR)

| Field | Description |
|---|---|
| `IrNamedTypeKind.Union` | New enum member. It sits before `Unknown`, and switches over the enum must handle it explicitly. |
| `IrNamedTypeSemantics.UnionCases` | `IReadOnlyList<IrTypeRef>?`. Set only when `Kind == Union`, following the `StringEnumValues` precedent. |

**Rules**
- `IrTypeRefMapper.BuildNamedTypeSemantics` checks for a union before the Branded, Record and Struct arms.
- A visited set guards self-referential cases such as `union U(List<U>, int)`.
- The default value of a union-typed slot is `null`, never `new U()` (D10).

## Entity: Switch subject typing (IR)

| Record | New optional field | Use |
|---|---|---|
| `IrSwitchExpression` | `IrTypeRef? ScrutineeType` | Decides whether to emit the FR-020 `never` tail. The field carries nullability. |
| `IrSwitchCase` | `IReadOnlyList<IrSwitchLabel>? PatternLabels` | Holds `case T x:` / `case > 5:` labels, each an `IrSwitchLabel(IrPattern Pattern, IrExpression? When)`. A section can mix constant labels (kept in `Labels`) and pattern labels; its test is the OR of all of them (FR-021). When both lists are empty, the section is `default`; every consumer must check `PatternLabels` before treating an empty `Labels` list as `default`. |
| `IrBreakStatement` | `bool TargetsSwitch` (default `false`) | Set when Roslyn's `IBranchOperation` target is the enclosing switch. Backends that lower a pattern switch to a labeled block render it as `break $switch` (FR-021). |

`main` already carries switch-statement pattern labels since #239 (`IrCaseLabel` variants on `IrSwitchCase`). If OQ-3 adopts that design, `PatternLabels` and `TargetsSwitch` are dropped from this model and only `IrSwitchExpression.ScrutineeType` remains.

## Entity: Exhaustive-tail predicate

The tail is emitted (FR-020) only when all four conditions hold:

1. `ScrutineeType` (with any `Nullable<>` / annotation stripped) is a named type with `Kind == Union`.
2. Every arm before the catch-all is an unguarded `IrTypePattern` whose type equals a distinct case, or an `IrConstantPattern(null)`.
3. There is no catch-all arm.
4. One of these is true:
   - neither the scrutinee nor any case is nullable;
   - a `null` arm exists.

## Entity: Default-widened slot (core, FR-039)

| Field | Description |
|---|---|
| `IrNullableTypeRef.IsDefaultWidening` | New optional flag (default `false`). Set on the declared type of a slot that the core widened from `U` to `U \| null` because it holds `default(U)` by its own declaration. |

**Rules**
- Widened slots: a field or auto-property of a type in the current compilation that has no initializer, is not `required` and is not definitely assigned by every constructor, or whose initializer is a default union value; a local whose initializer or any assignment in the same body is a default union value; a parameter whose default value is `default`. Return types and other parameters are never widened.
- A widened slot without an initializer starts as `null`.
- Every read of a widened slot whose C# converted type is the non-nullable `U` is wrapped in a non-null assertion (TS `x!`, type-only). Reads converted to `U?`, `object` or an interface are not wrapped.
- The flag lets a backend print the slot as nullable while every asserted read stays `U`, so FR-020's `never` tail is unaffected.
- A default union value outside a widened slot, landing in a non-nullable `U` position, is emitted as `null as unknown as U` and reported as MS0041 (FR-015).

## Entity: Companion namespace (FR-042)

| C# member in the union body | Companion member | Call site |
|---|---|---|
| instance method `R M(A a)` | `export function m(self: U, a: A): R` | `u.M(a)` → `U.m(u, a)` |
| instance get-only property `R P` | `export function p(self: U): R` | `u.P` → `U.p(u)` |
| static method `R S(A a)` | `export function s(a: A): R` | `U.S(a)` → `U.s(a)` |
| static get-only property `R Q` | `export function q(): R` | `U.Q` → `U.q()` |
| `static readonly` field / mutable static field | `export const f` / `export let f` | `U.F` → `U.f`; a write stays `U.f = v` |
| `override string ToString()` | `export function toString(self: U): string` | `u.ToString()`, `$"{u}"` → `U.toString(u)` |
| extra-interface implementation (implicit or explicit) | a function like any instance member | through a `U` receiver only; conversion to the interface is MS0038 (OQ-1) |
| nested type | the existing nested-type emission into the same namespace | `U.Inner` |

**Rules**
- Inside a body, `this`, the implicit receiver and `this.Value` all become `self` (FR-009).
- A generic union's type parameters prefix each function's own type parameters.
- Overloads use the existing `[ExportedAsModule]` function lowering; names use the naming policy and `[Name]`.
- A null-conditional receiver (`u?.P`) is evaluated once through `IrLetExpression` / `IrEvaluationTraits` (no union-specific IR node).
- The call-site rewrite runs in the core, so the TS and Dart targets receive the same IR. The Dart target skips the companion (FR-027).

## Entity: Synthesized structural guard (FR-041)

**Rules**
- For every interface or `[PlainObject]` case of a union in the current compilation without `[GenerateGuard]`, the core sets `IrNamedTypeSemantics.HasGeneratedGuard = true` on that case's references, so `IrRuntimeTypeTest.Of` returns `Guard`.
- The TS target runs `TypeGuardBuilder` for that type and emits the exported `isX` in the type's own file, once per compilation.
- A structural case from another assembly is guard-bearing only when its package exports a guard; otherwise MS0040 (OQ-2).

## Entity: Erased value domain (core, validation)

Derived from the FR-019 classification plus the type's TS mapping; used only by the MS0039 warning (FR-013), whose output keeps first-wins order (FR-040).

| Domain | Case types |
|---|---|
| `number` | numeric primitives, numeric enums, `[Branded]` over a number |
| `string` | `string`, `char`, `Guid`, `[StringEnum]`, `[Branded]` over a string |
| `boolean` | `bool` |
| `bigint` | types that map to TS `bigint` |
| `function` | delegates |
| `array` | types that erase to a JS array (`T[]`, `List<T>`, …) |
| `instanceof C` | transpilable classes and records, temporal types, `decimal`, `[Import]` / `[External]` classes, the runtime `HashSet` |
| `structural` | interfaces and `[PlainObject]` types, with declared or synthesized guards (FR-041) |

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
| `byte[]`, `char`, a `[StringEnum]` enum without a string-enum converter | not reproduced: MS0038 when JSON-reachable (Metano's descriptor writes a different token from STJ) |

Under `AllowReadingFromString` (including `JsonSerializerDefaults.Web`), a `number` case is a read candidate for both `number` and `string` tokens. `WriteAsString` and `AllowNamedFloatingPointLiterals` with a number case get MS0038.

**Rules**
- The descriptor is built inline for each union use (including each constructed generic instance), never as an object spec. `name` is STJ's display name (`Id`, ``GOpt`1[Id]``).
- The contract closure queues every object-kind case through `ChildTypesOf(Union)` (FR-030).
- Write order equals case order. The first case whose `test` matches wins (FR-024, FR-040). When MS0039 reported an overlap, more than one case can match, and the earlier case's descriptor writes the payload.
- Read candidates are the cases whose `token` equals the input's token kind. The messages are listed in [contracts/json-wire.md](./contracts/json-wire.md).

## Entity: Diagnostics

| Code | Name | Severity | Raised by |
|---|---|---|---|
| `MS0038` | `UnsupportedUnionShape` | Error | `UnionShapeValidator` (declarations, unsupported member kinds and constructed instances); `IrExpressionExtractor` for `IUnion` and extra-interface conversions; the JSON transformer for classifier, polymorphic, dictionary-key, token-parity and undescribable cases |
| `MS0039` | `IndistinguishableUnionCases` | Warning (D11) | `UnionShapeValidator` (declarations and constructed instances) |
| `MS0040` | `UntestableUnionCase` | Error | `UnionShapeValidator` (declaration); extractor (open `T` use site) |
| `MS0041` | `UnionDefaultValue` | Warning (D10) | `UnionDefaultWidening` (core), for default values in non-widenable `U` positions |
| `MS0042` | `AmbiguousUnionJsonCases` | Warning | `JsonSerializerContextTransformer` |
| `MS0043` | `UnionErasureDivergence` | Warning | `IrExpressionExtractor` |

The full reason list is in [contracts/diagnostics.md](./contracts/diagnostics.md).
