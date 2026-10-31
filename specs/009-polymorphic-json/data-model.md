# Data Model: System.Text.Json-faithful JSON contracts

No persistent data. The entities are the C# inputs the generator reads, the compile-time model it
builds, and the runtime shapes the generated context emits.

## 1. C# inputs (read by the generator)

| Source | Members read | Notes |
|---|---|---|
| `JsonPolymorphicAttribute` (own attributes only) | `TypeDiscriminatorPropertyName` (default `$type`), `UnknownDerivedTypeHandling` (`FailSerialization` = 0, `FallBackToBaseType` = 1, `FallBackToNearestAncestor` = 2), `IgnoreUnrecognizedTypeDiscriminators` | Optional; `Inherited = false`. |
| `JsonDerivedTypeAttribute` (own, repeatable) | ctor `(Type)`, `(Type, string)`, `(Type, int)` | ≥ 1 makes the type polymorphic. Attribute order = entry order. |
| `JsonSourceGenerationOptionsAttribute` on the context | `PropertyNamingPolicy`, `AllowOutOfOrderMetadataProperties`, ctor `JsonSerializerDefaults` (OPEN-4); every other named argument is only checked against the FR-046 list (`MS0037`) | |
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
```

| Entity | Responsibility | Validation (diagnostic) |
|---|---|---|
| `JsonContractRoot` | One `[JsonSerializable]` entry. | `MS0035` for open generic / type parameter / error type / `object` / non-collection interface / non-transpilable object kind / array rank > 1. |
| `JsonPolymorphism` | Read once per type by a reader (`TryRead`). `null` for non-polymorphic types. | `MS0032` / `MS0033` rules (spec FR-012, FR-013). |
| `JsonDerivedType` | One derived registration (may be the base itself). `Tag == null` → untagged. | Duplicate type/tag, not assignable to the base, abstract without `FallBackToNearestAncestor`, generic, interface/`[PlainObject]`, not transpilable in its declaring assembly → `MS0032`. |
| `JsonTypeInfoNames` | The single naming function (spec FR-031) with the context's `TypeInfoPropertyName` overrides; also used for constructed generic types (FR-015, FR-033). | Collision → `MS0036` (first wins; Error when the loser is referenced or the name is a reserved `SerializerContext` member). |
| `JsonBrandedFactory` | The resolved `[JsonFactory]` of a branded type and its TS member name, computed by `IrToTsNamingPolicy.ToFunctionName` (the function the companion declares it with). | `MS0034` (incl. operators and names equal to `create` / `toString`). |
| Contract closure | Ordered, deduplicated (by `JsonTypeInfoNames.Getter`) list of context entries: roots, then BFS over properties, `base` chain and `[JsonDerivedType]` targets, including closed generic object types and types from referenced Metano packages (FR-010, FR-033, FR-047). Each entry records whether it is the target of a `ref` / `base` / derived link (drives `MS0036` severity). | — |
| Property list | Per contract, own declarations deduplicated by final JSON name against the base chain; an `override` / `new` declaration replaces the base one (FR-009). | — |

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

## 3. Runtime shapes (`metano-runtime/system/json`, all additive)

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
}

export interface PolymorphismSpec {
  /** Verbatim; never passed through a naming policy. */
  readonly discriminator: string;
  /** The C# base is abstract (missing / ignored tag → NotSupportedException). */
  readonly abstract?: boolean;
  readonly derived: readonly DerivedTypeSpec[];
  /** Default "failSerialization". */
  readonly unknownDerivedType?: UnknownDerivedTypeHandling;
  readonly ignoreUnrecognizedTags?: boolean;
  readonly allowOutOfOrderMetadata?: boolean;
}

export interface TypeSpec<T = unknown> {
  // …existing members unchanged…
  readonly polymorphism?: PolymorphismSpec;
}

export interface ValueSpec<T = unknown> {
  readonly descriptor: TypeDescriptor;
  /** FR-037: reference-type roots read JSON null as null; the generator then types the
   *  getter `ValueSpec<T | null>`, so T already includes null. */
  readonly acceptsNull?: boolean;
  /** Phantom member that keeps T in the type. */
  readonly __type?: (value: T) => void;
}

export type JsonTypeInfo<T = unknown> = TypeSpec<T> | ValueSpec<T>;

export class JsonException extends Error {}
export class NotSupportedException extends Error {}

/** Wire order: own properties, then base's, recursively (STJ); deduplicated by JSON name,
 *  keeping the most-derived declaration (FR-009). */
export function collectWireProperties(spec: TypeSpec): readonly PropertySpec[];
```

`BrandedDescriptor`, `PropertySpec`, the 11 descriptor kinds, `JsonConverter`, `SerializerContext`
and `collectProperties` are unchanged.

### Dispatch index (internal)

| Field | Type | Built |
|---|---|---|
| `byCtor` | `Map<constructor, { tag?: TypeDiscriminator; spec: TypeSpec }>`; includes the base's constructor when the base registers itself | first serialize/deserialize through the base spec, never inside a getter |
| `byTag` | `Map<TypeDiscriminator, TypeSpec>` | same; `Map` keeps `1` and `"1"` distinct |

Cached in a module-level `WeakMap<PolymorphismSpec, Index>`.

### Serialize state machine (FR-005)

```mermaid
stateDiagram-v2
    [*] --> Polymorphic: spec.polymorphism set
    [*] --> Contract: no polymorphism
    Polymorphic --> Tagged: byCtor has ctor (checked first, base included when self-registered)
    Polymorphic --> Contract: ctor is base, not registered (no tag)
    Polymorphic --> Unknown: ctor neither registered nor base
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
    [*] --> Inspect: spec.polymorphism set
    Inspect --> Missing: no own key == discriminator, or not first key (and not allowOutOfOrder)
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
    Known --> AbstractEntry: tag of an abstract entry (OPEN-12)
    AbstractEntry --> [*]: pending probe
    Abstract --> [*]: NotSupportedException (must specify a type discriminator)
    Unrecognized --> [*]: JsonException (Read unrecognized type discriminator id)
    BadTag --> [*]: JsonException (metadata properties must be JSON strings)
    BaseContract --> [*]: base factory
    DerivedContract --> [*]: derived factory
```
