# ADR-0005 — `[InlineWrapper]` as branded type + companion namespace

**Status:** Accepted
**Date:** 2026-03-10

## Context

Domain-driven C# code typically uses small value types to strengthen type
safety around primitive values: `readonly record struct UserId(string
Value)`, `readonly record struct IssueId(string Value)`,
`readonly record struct Money(long Cents, Currency Currency)`. These
structs encapsulate a single primitive and add type-level distinction
without adding runtime overhead in C# (they live on the stack and the
JIT inlines the constructor).

The default Metano lowering for a record struct produces a full TypeScript
class with `equals()`, `hashCode()`, `with()`, and a constructor. For a
struct wrapping a single `string`, that is pure overhead: the class box
is heavier than the primitive, `equals` has to compare fields, `with`
allocates, and consumers end up paying for machinery they never wanted.

Worse, the brand is lost at the TS boundary: a `UserId` and an `IssueId`
both serialize to the same JSON string, and a stray `string` would
silently satisfy either type — exactly the bug the C# struct was
introduced to prevent.

## Decision

Introduce `[InlineWrapper]` on the `Metano` project. Structs decorated
with the attribute and containing **exactly one primitive field** lower
to a **branded type alias** plus a **companion namespace** holding the
static factory + helper methods. The struct's constructor becomes
`T.create`, static methods become functions in the namespace, and
instance accessors that just read the wrapped value collapse at the call
site.

```csharp
[Transpile, InlineWrapper]
public readonly struct UserId
{
    public string Value { get; }
    public UserId(string value) { Value = value; }
    public static UserId New() => new(Guid.NewGuid().ToString("N"));
}
```

becomes:

```ts
export type UserId = string & { readonly __brand: "UserId" };

export namespace UserId {
    export function create(value: string): UserId { return value as UserId; }
    export function new_(): UserId { return crypto.randomUUID().replace(/-/g, "") as UserId; }
}
```

Non-string primitives (e.g. wrapping an `int`) get an auto-generated
`toString()` helper so runtime type checks and dispatcher discriminators
can still interrogate the value. `instanceof` checks in overload
dispatchers become `typeof value === "string"` / `=== "number"` for
primitive-wrapper structs — the branded type has no runtime prototype.

Fallback: any struct with more than one field, or a decorated struct
where the single field is a transpilable type (not a primitive), stays
as a normal class. Falling back is load-bearing — `Money(long Cents,
Currency Currency)` can't be branded because it genuinely needs two
runtime fields.

JS reserved words collide on companion-namespace methods: `UserId.New()`
would map to `UserId.new(...)` which is a syntax error. The
`ToCamelCase` helper escapes reserved words by appending `_`
(`new_`, `delete_`, `class_`), reusing the same escaping logic the
field-name lowering already applies.

The same pattern is applied to the BCL `Guid` type. `Guid` maps to the
branded `UUID` type from `metano-runtime/system/uuid`, with `Guid.NewGuid()`
→ `UUID.newUuid()`, `Guid.Parse(s)` → `UUID.create(s)`, `Guid.Empty` →
`UUID.empty`. The same zero-cost branding that protects user-defined IDs
also protects BCL identifiers that cross the TS boundary.

## Consequences

- (+) Zero runtime overhead. The branded type is erased at emit —
  `UserId` is just a `string` at runtime. The namespace is a bag of
  tree-shakeable static functions.
- (+) Type safety at the TS boundary. `UserId`, `IssueId`, and `string`
  are three different types in the TS type system; passing one where
  another is expected is a compile-time error in the consumer's tsgo.
- (+) Idiomatic TS. Branded types are the accepted pattern for nominal
  typing over primitives — reviewers recognize the shape immediately.
- (+) Extends naturally to BCL types. `Guid` uses the same machinery via
  a runtime-provided `UUID` module, keeping the pattern uniform.
- (+) Serialization support. The JSON serializer's `branded` descriptor
  kind knows to emit/consume the underlying primitive while preserving
  the brand via `T.create` on deserialize.
- (−) JS reserved-word collisions require escaping logic on method names
  coming from user structs. Mitigated by centralizing the escape in
  `ToCamelCase`.
- (−) Consumers who want to log or inspect a branded value see the
  primitive (a raw string) in devtools, not an object with a recognizable
  type name. Acceptable trade-off — users who want a nominal object can
  opt out of `[InlineWrapper]` and ship a real class.

## Alternatives considered

- **Emit as class with `equals/hashCode/with`**: rejected. Wasteful for a
  single-field wrapper, and defeats the entire purpose of introducing a
  value type in C#.
- **Type alias only, no companion namespace**: rejected. Loses the static
  factory methods (`UserId.New()`, `UUID.newUuid()`), so consumers would
  construct values with bare casts everywhere — exactly the API ergonomics
  branded types are supposed to improve.
- **`unique symbol`-tagged nominal type**: rejected. More elaborate, no
  practical advantage over the brand property trick, and less idiomatic
  in current TS ecosystems.

## References

- `src/Metano/Annotations/InlineWrapperAttribute.cs`
- `src/Metano.Compiler.TypeScript/Transformation/InlineWrapperTransformer.cs`
- `src/Metano.Compiler.TypeScript/Transformation/TypeMapper.cs` — `Guid`
  → `UUID` mapping
- `targets/js/metano-runtime/src/system/uuid.ts` — runtime-provided branded type
- `samples/SampleIssueTracker` — `UserId`, `IssueId` as inline wrappers
- `tests/Metano.Tests/InlineWrapperTranspileTests.cs`
- Related: [ADR-0003](0003-declarative-bcl-mappings.md) — how the
  `Guid` → `UUID` mapping is declared.
