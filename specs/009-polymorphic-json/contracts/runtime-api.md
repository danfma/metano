# Contract: `metano-runtime/system/json` public API changes

All changes are additive. Existing signatures keep compiling and behaving the same, except the
serialization key order of specs that have a `base` (FR-009; JSON-semantically identical).

## New exports

| Export | Kind | Purpose |
|---|---|---|
| `TypeDiscriminator` | type | `string \| number` |
| `UnknownDerivedTypeHandling` | type | `"failSerialization" \| "fallBackToBaseType" \| "fallBackToNearestAncestor"` |
| `DerivedTypeSpec` | type | `{ tag?, spec: () => TypeSpec }` |
| `PolymorphismSpec` | type | discriminator + options + derived entries |
| `ValueSpec<T>` | type | non-object root: `{ descriptor, acceptsNull? }` |
| `JsonTypeInfo<T>` | type | `TypeSpec<T> \| ValueSpec<T>` |
| `JsonException` | class | STJ-named error for malformed / unrecognized payloads |
| `NotSupportedException` | class | STJ-named error for missing tags and unsupported runtime types |
| `collectWireProperties` | function | own-then-base property order |

## Changed signatures

```ts
class JsonSerializer {
  static serialize<T>(value: T, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): Record<string, unknown>; // unchanged
  static serialize<T>(value: T, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): unknown;               // new overload
  static deserialize<T>(data: unknown, spec: JsonTypeInfo<T>, converters?: readonly JsonConverter[]): T;            // widened
}

class BoundSerializer {
  serialize<T>(value: T, spec: TypeSpec<T>): Record<string, unknown>; // unchanged
  serialize<T>(value: T, spec: ValueSpec<T>): unknown;                // new overload
  deserialize<T>(data: unknown, spec: JsonTypeInfo<T>): T;            // widened
}

interface TypeSpec<T> {
  // …
  readonly polymorphism?: PolymorphismSpec; // new, optional
}
```

`ValueSpec` is recognized by the presence of `descriptor` (`"descriptor" in spec`).

## Behavior guarantees

1. `serializeWithSpec(value, spec)` with `spec.polymorphism` dispatches per FR-005 and returns an
   object whose first own key is the discriminator when a tag is written.
2. `deserializeWithSpec(data, spec)` with `spec.polymorphism` dispatches per FR-007 and never calls
   the base `factory` of an abstract base.
3. A derived contract reached by dispatch is never re-dispatched (FR-006).
4. Errors are instances of the exported classes; `error.name` equals the class name;
   `error.message` equals the STJ message without the path suffix.
5. The dispatch index is built lazily, outside every generated getter, and is safe whichever getter
   is accessed first.
6. `collectProperties` keeps returning base-first order.
7. Custom converters registered for `kind: "ref"` replace dispatch for every `ref` (unchanged rule:
   converters match by kind).
