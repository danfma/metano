# Data Model: System.Text.Json-faithful JSON contracts

No persistent data. The entities are the C# inputs the generator reads, the compile-time model it
builds, and the runtime shapes the generated context emits.

## 1. C# inputs (read by the generator)

| Source | Members read | Notes |
|---|---|---|
| `JsonPolymorphicAttribute` (own attributes only) | `TypeDiscriminatorPropertyName` (default `$type`), `UnknownDerivedTypeHandling` (`FailSerialization` = 0, `FallBackToBaseType` = 1, `FallBackToNearestAncestor` = 2), `IgnoreUnrecognizedTypeDiscriminators` | Optional; `Inherited = false`. |
| `JsonDerivedTypeAttribute` (own, repeatable) | ctor `(Type)`, `(Type, string)`, `(Type, int)` | ≥ 1 makes the type polymorphic, whether it is a class, a record, an interface (FR-018) or a `[PlainObject]` type (FR-019). Attribute order = entry order. |
| `JsonSourceGenerationOptionsAttribute` on the context | ctor `JsonSerializerDefaults` (`Web` sets the next three), `PropertyNamingPolicy`, `PropertyNameCaseInsensitive`, `NumberHandling` (`AllowReadingFromString` only), `AllowOutOfOrderMetadataProperties`; every other named argument is only compared with its System.Text.Json default (`MS0037` when it differs, FR-046) | A named argument overrides the Web default (FR-048). |
| `JsonPropertyOrderAttribute` on a contract member | ctor `int` | `PropertySpec.order` when non-zero; the runtime sorts the whole base chain by it, stably (FR-046, DECIDED-25). |
| `JsonIncludeAttribute` on a field or non-public property | presence | Collected when the member and its accessors are `public`, `internal` or `protected internal`; else `MS0037` (DECIDED-25). |
| `JsonIgnoreAttribute` | named `Condition` | `Always` (default) hides the member, `Never` keeps it; any other condition keeps it and reports `MS0037`. |
| `JsonConverterAttribute`, `JsonExtensionDataAttribute`, `JsonNumberHandlingAttribute`, `JsonObjectCreationHandlingAttribute`, `JsonUnmappedMemberHandlingAttribute`, `JsonRequiredAttribute`, `JsonConstructorAttribute` | presence and argument | `MS0037` when not mirrored (DECIDED-25). |
| `JsonSerializableAttribute` on the context (repeatable) | ctor `Type` (named **or rank-1 array** type), `TypeInfoPropertyName` | Rank > 1 → `MS0035`. |
| `JsonFactoryAttribute` (new, `Metano.Annotations`) | marker on a static method | `[AttributeUsage(AttributeTargets.Method)]`. |

## 2. Compile-time model (TypeScript target, `Transformation/Json/`)

```mermaid
classDiagram
    class JsonContractRoot {
        ITypeSymbol Type
        string? TypeInfoPropertyName
        Location Location
    }
    class JsonPolymorphism {
        string Discriminator
        bool IsAbstractBase
        UnknownDerivedTypeHandling Unknown
        bool IgnoreUnrecognized
        IReadOnlyList~JsonDerivedType~ Derived
    }
    class JsonDerivedType {
        INamedTypeSymbol Type
        JsonTypeTag? Tag
        Location Location
        JsonStructuralShape? Shape
    }
    class JsonStructuralShape {
        IReadOnlyList~RequiredMember~ Required
        string GuardName
        Covers(JsonStructuralShape) bool
    }
    class JsonReadOptions {
        bool PropertyNameCaseInsensitive
        bool AllowReadingFromString
        bool AllowOutOfOrderMetadataProperties
    }
    class JsonTypeTag {
        <<string | int>>
    }
    class JsonTypeInfoNames {
        Name(ITypeSymbol) string
        Getter(ITypeSymbol) string
    }
    class JsonBrandedFactory {
        IMethodSymbol Method
        string TsName
    }
    JsonPolymorphism "1" --> "*" JsonDerivedType
    JsonDerivedType --> JsonTypeTag
    JsonDerivedType --> JsonStructuralShape
```

| Entity | Responsibility | Validation (diagnostic) |
|---|---|---|
| `JsonContractRoot` | One `[JsonSerializable]` entry. | `MS0035` for open generic / type parameter / error type / `object` / non-collection interface / non-transpilable object kind / array rank > 1. |
| `JsonPolymorphism` | Read once per type by a reader (`TryRead`). `null` for non-polymorphic types. `IsAbstractBase` is true for abstract classes and interfaces. | `MS0032` / `MS0033` rules (spec FR-012, FR-013). |
| `JsonDerivedType` | One derived registration (may be the base itself). `Tag == null` → untagged. `Shape` is set only for a `[PlainObject]` variant. | Duplicate type/tag, not assignable to the base, abstract without `FallBackToNearestAncestor`, interface-typed entry (OPEN-18), generic, not transpilable in its declaring assembly → `MS0032`. |
| `JsonStructuralShape` | The write-side test of a `[PlainObject]` variant (FR-019): the guard name it references (`isCircle`, synthesized by `TypeGuardBuilder`) and its required members (TS name + coarse kind `string` / `number` / `boolean` / `array` / `object` / `present`). `Covers` drives the test order. | Two shapes that cover each other, or a cycle in the specificity order → `MS0032`. |
| `JsonReadOptions` | The context's mirrored read behavior (FR-002, FR-048), resolved from the Web default and the named options. Emitted as the `readOptions` override only when non-default. | — |
| `JsonTypeInfoNames` | The single naming function (spec FR-031) with the context's `TypeInfoPropertyName` overrides; also used for constructed generic types (FR-015, FR-033). | Collision → `MS0036` (first wins; Error when the loser is referenced or the name is a reserved `SerializerContext` member). |
| `JsonBrandedFactory` | The resolved `[JsonFactory]` of a branded type and its TS member name, computed by `IrToTsNamingPolicy.ToFunctionName` (the function the companion declares it with). | `MS0034` (incl. operators and names equal to `create` / `toString`). |
| Contract closure | Ordered, deduplicated (by `JsonTypeInfoNames.Getter`) list of context entries: roots, then BFS over properties, `base` chain and `[JsonDerivedType]` targets, including closed generic object types and types from referenced Metano packages (FR-010, FR-033, FR-047). Each entry records whether it is the target of a `ref` / `base` / derived link (drives `MS0036` severity). | — |
| Property list | Per contract, own declarations deduplicated by final JSON name against the base chain; an `override` / `new` declaration replaces the base one (FR-009). For an interface base, the interface's own and inherited interface properties; class variants of an interface do not link to it (FR-018). | `MS0037` for `[JsonInclude]`, `byte[]`, `[JsonPropertyOrder]`. |

### Naming function (FR-031)

| C# type | Name | Getter |
|---|---|---|
| any, with `TypeInfoPropertyName = "Names"` | `Names` | `names` |
| `EpisodeSummary` (non-generic; `[Name]` honored) | `EpisodeSummary` | `episodeSummary` |
| `Shape.Circle` (nested) | `Circle` | `circle` |
| `List<EpisodeSummary>` | `ListEpisodeSummary` | `listEpisodeSummary` |
| `IReadOnlyList<EpisodeSummary>` | `IReadOnlyListEpisodeSummary` | `iReadOnlyListEpisodeSummary` |
| `EpisodeSummary[]` | `EpisodeSummaryArray` | `episodeSummaryArray` |
| `Dictionary<string, EpisodeSummary>` | `DictionaryStringEpisodeSummary` | `dictionaryStringEpisodeSummary` |
| `int?` | `NullableInt32` | `nullableInt32` |
| `string` / `int` / `Guid` | `String` / `Int32` / `Guid` | `string` / `int32` / `guid` |
| `Page<EpisodeSummary>` | `PageEpisodeSummary` | `pageEpisodeSummary` |
| `GBase<int>` (closed generic polymorphic base) | `GBaseInt32` | `gBaseInt32` |
| `IShape` (interface base; resolved TS name, which may drop the `I` prefix) | `Shape` | `shape` |

## 3. Runtime shapes (`metano-runtime/system/json`)

Additive except for the `JsonSerializer` / `BoundSerializer` signatures of FR-034 (breaking).

```ts
export type TypeDiscriminator = string | number;

export type UnknownDerivedTypeHandling =
  | "failSerialization"
  | "fallBackToBaseType"
  | "fallBackToNearestAncestor";

export interface DerivedTypeSpec {
  /** Absent for an untagged registration. */
  readonly tag?: TypeDiscriminator;
  /** Lazy: base and derived getters reference each other. */
  readonly spec: () => TypeSpec;
  /** S6: an abstract registration (FallBackToNearestAncestor only); reading its tag throws (DECIDED-12). */
  readonly abstract?: boolean;
  /** [PlainObject] variants only: the synthesized guard used on write (FR-019). */
  readonly test?: (value: unknown) => boolean;
}

export interface PolymorphismSpec {
  /** Verbatim; never passed through a naming policy. */
  readonly discriminator: string;
  /** The C# base is abstract or an interface (missing / ignored tag → NotSupportedException). */
  readonly abstract?: boolean;
  /** C# simple name for messages; only when the base has no constructor (interface, [PlainObject]). */
  readonly typeName?: string;
  readonly derived: readonly DerivedTypeSpec[];
  /** Indices into `derived` in guard test order; only when it differs from attribute order. */
  readonly testOrder?: readonly number[];
  /** S6c: the guard of a concrete, non-self-registered [PlainObject] base (FR-019 rule 5). */
  readonly baseTest?: (value: unknown) => boolean;
  /** Default "failSerialization". */
  readonly unknownDerivedType?: UnknownDerivedTypeHandling;
  readonly ignoreUnrecognizedTags?: boolean;
}

/** Context-level read behavior (FR-002, FR-048); attached to each spec by createSpec / createValueSpec. */
export interface JsonReadOptions {
  readonly propertyNameCaseInsensitive?: boolean;
  readonly numberHandling?: "allowReadingFromString";
  readonly allowOutOfOrderMetadataProperties?: boolean;
}

export type NumberTypeName =
  | "System.Byte" | "System.SByte" | "System.Int16" | "System.UInt16"
  | "System.Int32" | "System.UInt32" | "System.Int64" | "System.UInt64"
  | "System.Single" | "System.Double";

// Existing descriptor with one optional member added (emitted only when the context reads
// numbers from strings, FR-048):
export type PrimitiveDescriptor = { readonly kind: "primitive"; readonly numberType?: NumberTypeName };

// S2b (DECIDED-23 f): the decimal.js `Decimal` the converter builds from the JSON number or text.
export type DecimalDescriptor = {
  readonly kind: "decimal";
  readonly type?: new (value: string | number) => unknown;
};

// S6b (FR-046, DECIDED-25): byte[] as a base64 JSON string; the TS value stays number[].
export type BytesDescriptor = { readonly kind: "bytes" };

export interface PropertySpec {
  // …existing members unchanged…
  /** S6b: a non-zero [JsonPropertyOrder]; collectWireProperties sorts the chain by it. */
  readonly order?: number;
  /** S6b: set after construction, only when the payload carries the key (DECIDED-25 k). */
  readonly assignIfPresent?: boolean;
}

export interface TypeSpec<T = unknown> {
  // …existing members unchanged…
  readonly polymorphism?: PolymorphismSpec;
  /** FR-037: a C# struct contract; JSON null at the root throws JsonException. */
  readonly valueType?: boolean;
  /** Attached by createSpec when the context has non-default read options. */
  readonly readOptions?: JsonReadOptions;
  /** S4: TS names of base properties a [JsonIgnore] override hides (DECIDED-20 a). */
  readonly ignoredProperties?: readonly string[];
}

export interface ValueSpec<T = unknown> {
  readonly descriptor: TypeDescriptor;
  /** FR-037: a C# value type (int, Guid, enum, branded, …); JSON null at the root throws.
   *  Absent for reference-type roots, whose getter is typed ValueSpec<T | null>. */
  readonly valueType?: boolean;
  /** Attached by createValueSpec when the context has non-default read options. */
  readonly readOptions?: JsonReadOptions;
  /** Phantom member that keeps T in the type; covariant like TypeSpec<T> (#270). */
  readonly __type?: () => T;
}

export type JsonTypeInfo<T = unknown> = TypeSpec<T> | ValueSpec<T>;

export class JsonException extends Error {}
export class NotSupportedException extends Error {}

/** Wire order: own properties, then base's, recursively (STJ); deduplicated by JSON name,
 *  keeping the most-derived declaration (FR-009); skips base declarations a more-derived
 *  spec lists in ignoredProperties (DECIDED-20 a); then sorted stably by `order` when any
 *  property has one (S6b, DECIDED-25). */
export function collectWireProperties(spec: TypeSpec): readonly PropertySpec[];
```

`SerializerContext` gains a protected `readOptions` getter (default: no options), which generated
contexts override when their options are not the defaults, and a protected `createValueSpec`.
`BrandedDescriptor`, `JsonConverter` and `collectProperties` are unchanged; S6b adds the 12th
descriptor kind (`bytes`) and `PropertySpec.order`.

### Dispatch index (internal)

| Field | Type | Built |
|---|---|---|
| `byCtor` | `Map<constructor, { tag?: TypeDiscriminator; spec: TypeSpec }>`; includes the base's constructor when the base registers itself | first serialize/deserialize through the base spec, never inside a getter |
| `byTag` | `Map<TypeDiscriminator, TypeSpec>` | same; `Map` keeps `1` and `"1"` distinct |
| `structural` | `{ test, tag?, spec }[]` in `testOrder` (or attribute order) | same; only entries with a `test` |

Cached in a module-level `WeakMap<PolymorphismSpec, Index>`.

### Serialize state machine (FR-005)

```mermaid
stateDiagram-v2
    [*] --> NullValue: value is null
    NullValue --> [*]: JSON null (FR-037)
    [*] --> Polymorphic: spec.polymorphism set
    [*] --> Contract: no polymorphism
    Polymorphic --> Structural: plain object (prototype Object.prototype or null)
    Polymorphic --> Tagged: class instance, byCtor has ctor (checked first, base included when self-registered)
    Polymorphic --> Contract: class instance, ctor is base, not registered (no tag)
    Polymorphic --> Unknown: class instance, ctor neither registered nor base
    Structural --> Tagged: first guard in test order that accepts the value
    Structural --> Contract: no variant guard accepts, baseTest accepts (no tag)
    Structural --> Unknown: no guard accepts (runtime type name Object)
    Unknown --> Fail: failSerialization
    Unknown --> Contract: fallBackToBaseType (base, no tag)
    Unknown --> Tagged: fallBackToNearestAncestor (ancestor found, abstract entries included)
    Unknown --> Contract: fallBackToNearestAncestor (none, base)
    Tagged --> Contract: write tag first, then the entry's contract
    Contract --> DepthCheck: own props, then base props (deduplicated)
    DepthCheck --> [*]: depth <= 64
    DepthCheck --> Cycle: depth > 64
    Cycle --> [*]: JsonException (possible object cycle, FR-016)
    Fail --> [*]: NotSupportedException
```

### Deserialize state machine (FR-007)

```mermaid
stateDiagram-v2
    [*] --> NullRoot: JSON null at the root
    NullRoot --> [*]: null (reference type) or JsonException (valueType)
    [*] --> Inspect: spec.polymorphism set
    Inspect --> Missing: no own key == discriminator (case-sensitive), or not first key (and not readOptions.allowOutOfOrderMetadataProperties)
    Inspect --> BadTag: tag neither string nor number (Null / True / False / StartObject / StartArray)
    Inspect --> Known: byTag has tag
    Inspect --> UnknownTag: byTag lacks tag
    Missing --> Abstract: abstract base
    Missing --> BaseContract: concrete base
    UnknownTag --> BaseContract: ignoreUnrecognizedTags and concrete
    UnknownTag --> Abstract: ignoreUnrecognizedTags and abstract
    UnknownTag --> Unrecognized: otherwise
    Known --> DerivedContract: tag of a derived entry
    Known --> BaseContract: tag of the base's self-registration
    Known --> AbstractEntry: tag of an abstract entry (DECIDED-12)
    AbstractEntry --> [*]: NotSupportedException (abstract type, or its own missing tag when polymorphic)
    Abstract --> [*]: NotSupportedException (must specify a type discriminator)
    Unrecognized --> [*]: JsonException (Read unrecognized type discriminator id)
    BadTag --> [*]: JsonException (metadata properties must be JSON strings)
    BaseContract --> [*]: base factory
    DerivedContract --> [*]: derived factory (object literal for a PlainObject variant)
```
