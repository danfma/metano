# Contract: `metano-runtime/system/json` public API changes

This release is **breaking** (FR-049, DECIDED-6): `JsonSerializer` and `BoundSerializer` change the
`TypeSpec` overloads so that `null` is accepted and returned, as in STJ (FR-034, FR-037). Every
other change is additive. Behavior also changes in these cases:

- the serialization key order of specs that have a `base` (FR-009; JSON-semantically identical);
- the error **class** of the two existing `deserializeWithSpec` failures (non-object input, missing
  non-optional property): `Error` → `JsonException` (FR-045). `JsonException` extends `Error`, so
  `catch (e)` and `instanceof Error` callers are unaffected; message text changes for the
  non-object case;
- JSON `null` at a `TypeSpec` root: it used to throw; it now returns `null`, except for struct
  contracts (`valueType: true`), which throw `JsonException` (FR-037);
- `arrayConverter` / hash-set serialization accept any `Iterable` (FR-038; previously a non-array
  iterable threw `TypeError`).

## New exports

| Export | Kind | Purpose |
|---|---|---|
| `TypeDiscriminator` | type | `string \| number` |
| `UnknownDerivedTypeHandling` | type | `"failSerialization" \| "fallBackToBaseType" \| "fallBackToNearestAncestor"` |
| `DerivedTypeSpec` | type | `{ tag?, spec: () => TypeSpec, test? }` (`test` for `[PlainObject]` variants) |
| `PolymorphismSpec` | type | discriminator + options + derived entries (+ `typeName`, `testOrder`) |
| `JsonReadOptions` | type | `{ propertyNameCaseInsensitive?, numberHandling?: "allowReadingFromString", allowOutOfOrderMetadataProperties? }` |
| `NumberTypeName` | type | CLR names of numeric primitives (`"System.Int32"`, …) |
| `ValueSpec<T>` | type | non-object root: `{ descriptor, valueType?, readOptions? }` |
| `JsonTypeInfo<T>` | type | `TypeSpec<T> \| ValueSpec<T>` |
| `JsonException` | class | STJ-named error for malformed / unrecognized payloads |
| `NotSupportedException` | class | STJ-named error for missing tags and unsupported runtime types |
| `collectWireProperties` | function | own-then-base property order |

## Changed signatures

```ts
class JsonSerializer {
  static serialize<T>(value: T | null, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): Record<string, unknown> | null; // BREAKING (was value: T → Record<string, unknown>)
  static serialize<T>(value: T, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): unknown;                             // new overload
  static deserialize<T>(data: unknown, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): T | null;                       // BREAKING (was T)
  static deserialize<T>(data: unknown, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): T;                             // new overload
}

class BoundSerializer {
  serialize<T>(value: T | null, spec: TypeSpec<T>): Record<string, unknown> | null; // BREAKING
  serialize<T>(value: T, spec: ValueSpec<T>): unknown;                               // new overload
  deserialize<T>(data: unknown, spec: TypeSpec<T>): T | null;                        // BREAKING
  deserialize<T>(data: unknown, spec: ValueSpec<T>): T;                              // new overload
}

interface TypeSpec<T> {
  // …
  readonly polymorphism?: PolymorphismSpec; // new, optional
  readonly valueType?: boolean;             // new, optional (struct contracts)
  readonly readOptions?: JsonReadOptions;   // new, optional (attached by createSpec)
}

type PrimitiveDescriptor = { readonly kind: "primitive"; readonly numberType?: NumberTypeName }; // numberType new, optional

abstract class SerializerContext {
  protected get readOptions(): JsonReadOptions;                  // new; default {}
  protected createSpec<T>(spec: TypeSpec<T>): TypeSpec<T>;       // unchanged signature; now attaches readOptions
  protected createValueSpec<T>(spec: ValueSpec<T>): ValueSpec<T>; // new
}
```

`ValueSpec` is recognized by the presence of `descriptor` (`"descriptor" in spec`).

## Behavior guarantees

1. `serializeWithSpec(value, spec)` with `spec.polymorphism` dispatches per FR-005 (class
   instances by constructor) and FR-019 (plain objects by guards in `testOrder`), and returns an
   object whose first own key is the discriminator when a tag is written.
2. `deserializeWithSpec(data, spec)` with `spec.polymorphism` dispatches per FR-007 and never calls
   the base `factory` of an abstract or interface base.
3. A derived contract reached by dispatch is never re-dispatched (FR-006).
4. Errors are instances of the exported classes; `error.name` equals the class name;
   `error.message` equals the STJ message without the path suffix (DECIDED-5). Type names come from
   `constructor.name`, from `polymorphism.typeName` for a base without a constructor, and are
   `Object` for a plain object that matches no guard.
5. The dispatch index is built lazily, outside every generated getter, and is safe whichever getter
   is accessed first.
6. `collectProperties` keeps returning base-first order; `collectWireProperties` returns own-then-base
   order deduplicated by JSON name (most-derived declaration wins).
7. Custom converters registered for `kind: "ref"` replace dispatch for every `ref` (unchanged rule:
   converters match by kind).
8. `serializeContract` / `deserializeContract` count JSON container depth; beyond 64 they throw
   `JsonException` "A possible object cycle was detected. …" on serialize (FR-016) and the STJ
   reader message pinned in S8 on read (DECIDED-15).
9. JSON `null` at a root returns `null` unless the spec has `valueType: true`, which throws
   `JsonException` `The JSON value could not be converted to <Type>.`; a `null` value serializes to
   JSON `null` (FR-037).
10. A non-scalar tag throws `JsonException` naming the STJ token type (`Null`, `True`, `False`,
    `StartObject`, `StartArray`).
11. With `readOptions.propertyNameCaseInsensitive`, JSON keys match properties case-insensitively,
    in document order (the last match wins); the discriminator is still matched case-sensitively
    (FR-048).
12. With `readOptions.numberHandling === "allowReadingFromString"`, a `primitive` descriptor with
    `numberType` and every `decimal` descriptor accept a JSON string by the FR-048 grammar, and
    otherwise throw `JsonException` `The JSON value could not be converted to <numberType>.`.
    Writing is unchanged.
