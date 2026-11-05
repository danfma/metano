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
| `DerivedTypeSpec` | type | `{ tag?, spec: () => TypeSpec, test?, abstract? }` (`test` for `[PlainObject]` variants; `abstract` for an abstract registration, S6) |
| `PolymorphismSpec` | type | discriminator + options + derived entries (+ `typeName`, `testOrder`, `baseTest`) |
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
  static serialize<T>(value: T & {}, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): Record<string, unknown>;        // non-null in, record out (S3b review)
  static serialize<T>(value: T | null, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): Record<string, unknown> | null; // BREAKING (was value: T → Record<string, unknown>)
  static serialize<T>(value: T, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): unknown;                             // new overload
  static deserialize<T>(data: unknown, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): T | null;                       // BREAKING (was T)
  static deserialize<T>(data: unknown, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): T;                             // new overload
  static deserializeText<T>(text: string, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): T | null;                    // new (#457)
  static deserializeText<T>(text: string, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): T;                          // new (#457)
}

class BoundSerializer {
  serialize<T>(value: T & {}, spec: TypeSpec<T>): Record<string, unknown>;          // non-null in, record out
  serialize<T>(value: T | null, spec: TypeSpec<T>): Record<string, unknown> | null; // BREAKING
  serialize<T>(value: T, spec: ValueSpec<T>): unknown;                               // new overload
  deserialize<T>(data: unknown, spec: TypeSpec<T>): T | null;                        // BREAKING
  deserialize<T>(data: unknown, spec: ValueSpec<T>): T;                              // new overload
  deserializeText<T>(text: string, spec: TypeSpec<T>): T | null;                     // new (#457)
  deserializeText<T>(text: string, spec: ValueSpec<T>): T;                           // new (#457)
}

interface TypeSpec<T> {
  // …
  readonly polymorphism?: PolymorphismSpec; // new, optional
  readonly valueType?: boolean;             // new, optional (struct contracts)
  readonly typeName?: string;               // new, optional (S3b): CLR name next to valueType
  readonly readOptions?: JsonReadOptions;   // new, optional (attached by createSpec)
  readonly ignoredProperties?: readonly string[]; // new, optional (S4): TS names of base properties a [JsonIgnore] override hides
}

type PrimitiveDescriptor = { readonly kind: "primitive"; readonly numberType?: NumberTypeName; readonly typeName?: string }; // numberType new, optional; typeName (#479) on a bool dictionary key
// #479: TemporalDescriptor, BrandedDescriptor, EnumDescriptor and NumericEnumDescriptor also take an
// optional `typeName` (the key's CLR name), and NumericEnumDescriptor an optional
// `numberType: IntegerTypeName` (the underlying type), set by the generator on dictionary keys only.
type DecimalDescriptor = { readonly kind: "decimal"; readonly type?: new (value: string | number) => unknown }; // type new, optional (S2b)

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
   order deduplicated by JSON name (most-derived declaration wins), leaving out every base
   declaration whose `ts` name a more-derived spec lists in `ignoredProperties`. Serialization and
   deserialization both use it.
7. Custom converters registered for `kind: "ref"` replace dispatch for every `ref` (unchanged rule:
   converters match by kind).
8. `serializeContract` / `deserializeContract` (module-internal since S5; `serializeWithSpec` /
   `deserializeWithSpec` dispatch over them) and the array, hash-set and map converters count JSON
   container depth; the 65th nested container throws `JsonException` "A possible object cycle was detected. …" on serialize
   (FR-016) and "The maximum configured depth of 64 has been exceeded. Cannot read next JSON
   object." (`… JSON array.`) on read (DECIDED-15, DECIDED-20 b).
8a. The array and hash-set converters throw `JsonException` "Expected array for deserialization,
   got <kind>" for a non-array, non-null JSON value (#425), and the dictionary (`map`) converter
   throws `JsonException` "Expected object for deserialization, got <kind>" for a non-object or an
   array (DECIDED-20 c). `null` reads as `null` in all three.
8b. The `map` converter parses each JSON property name into its key type as System.Text.Json does,
   on the tree and the text paths, whatever the read options (#479): a `numberType` key by the
   FR-048 grammar; a `typeName: "System.Boolean"` key from `true` / `false` in any case (written
   `True` / `False`); a numeric enum key from a member name in any case, a comma-separated list
   of names or an integer (optional sign, within the underlying `numberType`, `System.Int32`
   without it), written as the member name (an aliased value by its first member); a `[StringEnum]` key from a member value
   in any case; a `typeName: "System.Guid"` key from the `D` format, lowercased; a `decimal` key
   by the decimal grammar (written with `toFixed()`); a temporal key through `parse`. A rejected
   name throws `JsonException` `The JSON value could not be converted to <typeName | numberType>.`
   (`Object` without one). A key without a type, a `string` key and a `[Branded]` key keep the
   value converter; a custom converter of the key's kind takes precedence. A `long` / `ulong` key
   reads as a `number` until spec 012 slice 012e.
9. JSON `null` at a root returns `null` unless the spec has `valueType: true`, which throws
   `JsonException` `The JSON value could not be converted to <Type>.`; a `null` value serializes to
   JSON `null` (FR-037).
10. A non-scalar tag throws `JsonException` naming the STJ token type (`Null`, `True`, `False`,
    `StartObject`, `StartArray`).
    A number tag that is not an `Int32` throws `JsonException` `The JSON value could not be
    converted to <Base>.` (DECIDED-21 b).
10a. A polymorphic read rejects misplaced metadata in document order: on a concrete base, a tag
    that is not the first key (the out-of-order option off) throws `JsonException` "The metadata
    property is either not supported by the type or is not the first property in the deserialized
    JSON object." (an abstract or interface base reports a missing tag instead, unless its first
    key is `$id`, `$ref` or `$values`); `$id` / `$ref`, and `$values` after the first key, throw
    the same message, `$values` as the first key its own, and any other `$`-prefixed key
    "Properties that start with '$' are not allowed in types that support metadata. …". With
    `allowOutOfOrderMetadataProperties`, the tag's JSON kind is checked where it appears, `$values`
    next to a data key gets STJ's two-reason message naming the offending key, a `$values` left
    pending at the end throws after the tag is resolved (or the leading-`$values` message without
    a tag), and the contract is chosen after the walk. Array-index keys are
    not positioned (JS enumerates them first) (DECIDED-21 h–j).
11. With `readOptions.propertyNameCaseInsensitive`, JSON keys match properties case-insensitively,
    in document order (the last match wins); the discriminator is still matched case-sensitively
    (FR-048).
12. With `readOptions.numberHandling === "allowReadingFromString"`, a `primitive` descriptor with
    `numberType` and every `decimal` descriptor accept a JSON string by the FR-048 grammar, and
    otherwise throw `JsonException` `The JSON value could not be converted to <numberType>.`.
    Writing is unchanged.
13. `deserializeText(text, spec)` returns what `deserialize(JSON.parse(text), spec)` returns,
    except that (FR-048 item 7, #457):
    - each `decimal` is built from the source digits of its JSON number, rounded as STJ rounds
      them (28 decimal places, 96 bits), and one past `MaxValue` throws `JsonException`;
    - invalid JSON text throws `JsonException` with STJ's reader message instead of the engine's
      `SyntaxError`;
    - when the text is tokenized (a spec whose closure holds a `decimal`, and a text that may hold
      a number of more than 15 digits), a text nested past 1 000 levels throws; FR-016's limit of
      64 counts the containers the contract reads, on every path.
    A spec whose closure holds no `decimal` reads through `JSON.parse` unchanged. Converters other
    than the built-in `decimal` one, custom ones included, see the `number` `JSON.parse` gives.
    `deserialize` keeps reading a parsed tree and is inexact for a `decimal` beyond double
    precision.

## Delivery status (S5)

S5 ships the polymorphism types, both exception classes, `collectWireProperties` (S4) and
guarantees 1–8, 10 and 10a. `JsonReadOptions` ships with `allowOutOfOrderMetadataProperties` only; the
other members, the `readOptions` getter and its attachment by `createSpec` arrive with S2b, and
the null-root and `valueType` rules (guarantee 9, `ValueSpec.valueType`) with S3b. See DECIDED-21
for the runtime rules S5 settled (number tags, misplaced metadata, the interim concrete
`[PlainObject]` base until S6c, FR-017's read side deferred to S6).

## Delivery status (S6)

S6 adds `DerivedTypeSpec.abstract`, a deviation from the S5 shape that DECIDED-21 f anticipated:
the descriptor needed a marker for an abstract registration, and a member on the entry keeps every
non-polymorphic `TypeSpec` unchanged. With it the runtime implements FR-017's read side
(DECIDED-12): reading the tag of an abstract entry throws `NotSupportedException` "Deserialization
of interface or abstract types is not supported. Type '<entry>'.", or the entry's own missing-tag
message when the entry's spec is polymorphic; without the out-of-order option, `$id`, `$ref` or
`$values` right after the tag throw the "metadata property … not the first property"
`JsonException` first (DECIDED-22 a). A hierarchy whose entries all lack a `tag` reads like a
non-polymorphic contract, as STJ does: the concrete base reads its own contract (`$`-prefixed keys
included) and an abstract base throws "Deserialization of interface or abstract types is not
supported. Type '<base>'."; writes still dispatch (DECIDED-22 k).

## Delivery status (S2b)

S2b completes `JsonReadOptions` (`propertyNameCaseInsensitive`, `numberHandling`), adds
`ValueSpec.readOptions`, `PrimitiveDescriptor.numberType` and the exported `NumberTypeName`, the
`SerializerContext.readOptions` getter and its attachment by `createSpec` / `createValueSpec`, and
guarantees 11 and 12. Keys match properties by `OrdinalIgnoreCase` rather than `toUpperCase()`, the
discriminator a polymorphic read consumes is never a property, a nullable number names
``System.Nullable`1[<CLR name>]`` in its error, and a `decimal` descriptor's `type` builds the value
from the JSON number or text (DECIDED-23 f).

## Delivery status (S6c)

S6c adds `PolymorphismSpec.baseTest`, the base-guard member DECIDED-21 e anticipated, and
replaces the interim rule: a plain object that matches no variant guard is written as the base
(no tag) only when `baseTest` accepts it, and otherwise follows `unknownDerivedType` with the
runtime type name `Object` (FR-019 rule 5, DECIDED-24 b). Generated contexts now emit `typeName`,
`testOrder` and the `test` guards of `[PlainObject]` entries.

## Delivery status (S6b)

S6b adds the `bytes` descriptor (`BytesDescriptor`, exported) and `PropertySpec.order`
(FR-046, DECIDED-25). The built-in `bytes` converter writes a `byte[]` (`number[]`) as padded
standard base64 without line breaks and reads System.Text.Json's accepted text: whitespace
(space, tab, CR, LF) is ignored anywhere; a length that is not a multiple of four, a character
outside the alphabet, misplaced or excess padding, non-zero unused bits and any JSON value other
than a string throw `JsonException` "The JSON value could not be converted to System.Byte[].";
a custom `JsonConverter` of kind `bytes` overrides it. `collectWireProperties` sorts the
deduplicated chain stably by `order` (absent = 0) when any property has one, so a base spec's
`order` places its properties among the derived ones as in System.Text.Json.

`TypeSpec.assignIfPresent: readonly string[]` (review rounds, DECIDED-25 k, l): for the
contract being read (not its base specs), the reader leaves a missing listed key out of the
factory's input instead of requiring it or reading `null`, and after the factory returns, the
runtime assigns every listed key the payload carried to the constructed value, in wire order,
so a missing key keeps the value the constructor set (a C# initializer or a constructor
assignment).

## Delivery status (#457)

#457 adds `deserializeText` to `JsonSerializer` and `BoundSerializer` (guarantee 13). Every
`decimal` read, on both entry points, now rounds as STJ does: a number or string text past 28
decimal places or 96 bits rounds (`1e-29` reads as `0`), and the range is decided on the rounded
value. The tokenizer's number leaf (`JsonNumberText`) is internal: only the built-in `decimal`
converter receives it, so no exported type or converter signature changes. `long` / `ulong` as
`bigint` (#450) read exact digits by joining `readsNumberText` in `exact-number-reads.ts`.

## Delivery status (S3b)

S3b ships the changed signatures above (`serialize` / `deserialize` / `deserializeText` through a
`TypeSpec` on `JsonSerializer` and `BoundSerializer`), `valueType` and `typeName` on `TypeSpec` and
`ValueSpec` (`ValueSpec<T>` is `{ descriptor, valueType?, typeName?, readOptions? }`), and
guarantee 9. The null check lives in `null-reads.ts`: at the root it runs before any converter,
and the `ref` converter runs it for a struct property, element or dictionary value (DECIDED-26 h).
Every "could not be converted" error of a spec with `typeName` names it (DECIDED-26 d). An empty or blank text
keeps the reader's "does not contain any JSON tokens" message for every root (DECIDED-26 b).
`test/system/json/typecheck/null-root-signatures.ts`, compiled by `tsgo` from a bun test, pins the
return types and the strict-mode errors of the migration.
