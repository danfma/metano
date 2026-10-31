# Feature Specification: System.Text.Json-faithful JSON contracts (polymorphism, validating branded factories, non-object roots)

**Feature Branch**: `spec/008-polymorphic-json` (spec directory `009-polymorphic-json`; `008` is
already taken by [`008-exports-subpath-root`](../008-exports-subpath-root/spec.md), see OPEN-1)

**Created**: 2026-10-31

**Status**: Draft

**Input**: GitHub issues [danfma/metano#268](https://github.com/danfma/metano/issues/268)
("Support polymorphic JSON for record hierarchies in the generated SerializerContext"),
[#269](https://github.com/danfma/metano/issues/269) ("Let branded types choose the validating
factory used by JSON deserialization") and [#270](https://github.com/danfma/metano/issues/270)
("Support collection and primitive roots in the generated SerializerContext"). All three are
requested by video-studio (clip and anchor hierarchies, validated value objects, list endpoints).

**Traceability**: Spec change request that **extends the baseline serialization requirements
`FR-032`–`FR-034`** (feature-support-matrix "Serialization | JsonSerializerContext" row) and the
inline-wrapper requirement **`FR-018`** (matrix "Types | Inline wrappers" row). Builds on merged
work: #250 / PR #285 (contract closure and the single getter-name helper), #238 (abstract-base
factory throw, constructor arguments taken from the whole hierarchy, derived positional records),
#273 / PR #301 (package.json exports on full cache reuse). Requirement identifiers below are
local to this feature (`009 FR-NNN`), following the convention of every feature spec since 002;
the baseline `FR-001`–`FR-048` space is not renumbered.

## Overview

Metano's generated `SerializerContext` already mirrors a C# `JsonSerializerContext` for plain
object graphs: JSON names are computed at transpile time and every reachable object type gets a
lazy `TypeSpec` getter (#250). Three gaps make it unusable as the TypeScript side of a real .NET
API:

1. **No polymorphism.** `[JsonPolymorphic]` / `[JsonDerivedType]` are ignored. A `Clip`
   hierarchy serialized by .NET as `{"kind":"image","Path":"a.png","Seconds":1.5}` cannot be read
   back as an `ImageClip` in TypeScript, and TypeScript writes `{"Seconds":1.5}` (base contract
   only, no discriminator) for the same value.
2. **Validation is bypassed at the wire boundary.** A `[Branded]` value is always rebuilt with the
   identity cast `Title.create(raw)`, so `"   "` becomes a `Title` although the C# `Title.From`
   rejects it. The validating TS `Title.from` is emitted but unreachable from deserialization.
3. **Only object roots work.** `[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]`
   produces `TypeSpec<IReadOnlyList>` with `factory: () => new IReadOnlyList()` (does not
   compile), `typeof(EpisodeSummary[])` is silently dropped, and primitive / enum / branded roots
   are emitted as bogus class specs.

This feature closes all three by **mirroring System.Text.Json (STJ)**: what .NET writes, the
generated TypeScript reads; what TypeScript writes, .NET reads; and for every case covered by the
conformance fixtures the two writers produce **byte-identical** JSON. Behaviors were measured on
.NET 10 (SDK 10.0.401), reflection and source generation alike (research.md R1–R4).

### Decisions already taken by the maintainer

- Mirror STJ: discriminator written first, derived-first property order, case-sensitive
  discriminator matching, default discriminator name `$type`, string **and** integer tags,
  unknown/missing-tag errors with STJ-like messages.
- `[JsonFactory]` is an explicit opt-in on one static method (no `From` naming convention).
- One specification covers #268, #269 and #270.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Round-trip a polymorphic record hierarchy with .NET (Priority: P1)

A developer shares `Clip` (abstract record with `[JsonPolymorphic(TypeDiscriminatorPropertyName =
"kind")]` and `[JsonDerivedType(typeof(ImageClip), "image")]`, `[JsonDerivedType(typeof(SilenceClip),
"silence")]`) between an ASP.NET API and a TypeScript client. The client deserializes what the API
sends into the right subclass, and sends back JSON the API deserializes without custom code.

**Why this priority**: It is the issue that blocks video-studio F2 and the largest fidelity gap.

**Independent Test**: Serialize fixed `Clip` values with the real STJ source-generated context,
check the output into fixtures, then in Bun deserialize each fixture through the generated context,
assert `instanceof`, and assert `JSON.stringify(serialize(value))` equals the fixture text.

**Acceptance Scenarios**:

1. **Given** `new ImageClip("a.png", 1.5)` serialized through the `clip` spec, **When** it is
   stringified, **Then** the text is exactly `{"kind":"image","Path":"a.png","Seconds":1.5}` (tag
   first, derived properties before inherited ones).
2. **Given** the same value serialized through the `imageClip` spec (static type = derived),
   **When** it is stringified, **Then** the text is `{"Path":"a.png","Seconds":1.5}` (no tag).
3. **Given** `{"kind":"silence","Seconds":2}` deserialized through `clip`, **Then** the result is a
   `SilenceClip` instance with `seconds === 2`.
4. **Given** `{"kind":"video","Seconds":2}`, **When** deserialized through `clip`, **Then** a
   `JsonException` with message `Read unrecognized type discriminator id 'video'.` is thrown.
5. **Given** `{"Seconds":2}` (no tag) or `{"Kind":"image",...}` (wrong case), **When** deserialized
   through the abstract `clip`, **Then** a `NotSupportedException` with message `The JSON payload
   for polymorphic interface or abstract type 'Clip' must specify a type discriminator.` is thrown.
6. **Given** a `Timeline(string Name, Clip Main)` and a `List<Clip>`, **When** serialized, **Then**
   every `Clip` value carries its tag (dispatch also applies through `ref`, array, map and nullable
   descriptors).
7. **Given** `[JsonDerivedType(typeof(IntA), 1)]`, **When** an `IntA` is serialized through the
   base, **Then** the tag is the JSON number `1`; **and** `{"$type":"1",...}` (string) is rejected
   as an unrecognized id.
8. **Given** a hierarchy with no `TypeDiscriminatorPropertyName`, **Then** the tag property is
   `$type`, and neither its name nor its value is changed by the context's naming policy.

---

### User Story 2 - Serialize collection, dictionary, primitive, enum and branded roots (Priority: P1)

A developer declares `[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]` (and arrays,
dictionaries, sets, primitives, enums, branded types, `int?`) on the context, exactly as in C#, and
gets a compiling, usable context member for each, named like STJ's `JsonTypeInfo` properties.

**Why this priority**: Every list endpoint needs a collection root; today the output does not
compile, so the whole context file is unusable.

**Independent Test**: Transpile a context with one root of each shape; `tsgo --strict` reports zero
errors; each getter's name matches the STJ property name camel-cased; round-trip one value per
shape in Bun.

**Acceptance Scenarios**:

1. **Given** `[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]`, **Then** the context has
   `get iReadOnlyListEpisodeSummary(): ValueSpec<EpisodeSummary[]>` whose descriptor is an `array`
   of `ref` to `episodeSummary`, and `episodeSummary` is in the context even if not listed.
2. **Given** `typeof(EpisodeSummary[])`, **Then** the getter `episodeSummaryArray` exists (no
   silent drop).
3. **Given** `typeof(Dictionary<string, EpisodeSummary>)`, `typeof(HashSet<string>)`,
   `typeof(string)`, `typeof(int?)`, a `[StringEnum]`, a `[Branded]` type and `typeof(Guid)`,
   **Then** the getters are `dictionaryStringEpisodeSummary`, `hashSetString`, `string`,
   `nullableInt32`, the enum's name, the branded type's name and `guid`, each a `ValueSpec` with the
   descriptor the same type gets as a property today.
4. **Given** `[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Names")]`, **Then**
   the getter is `names`.
5. **Given** `JsonSerializer.serialize(list, ctx.iReadOnlyListEpisodeSummary)`, **Then** the result
   is a JSON-safe array, and `deserialize` returns `EpisodeSummary` instances.
6. **Given** a `List<Clip>` root where `Clip` is polymorphic, **Then** each element is written with
   its tag, matching STJ's `[{"kind":"image",...},{"kind":"silence",...}]`.

---

### User Story 3 - Deserialize branded values through their validating factory (Priority: P2)

A developer marks `[JsonFactory] public static Title From(string value)` on a `[Branded]` record
struct. Deserializing a payload containing `"title":"   "` through the generated context throws the
same validation error the TS `Title.from` throws; `"  ok "` becomes `"ok"`.

**Why this priority**: Correctness/security at the UI boundary; small and independent.

**Independent Test**: Transpile the branded type and a context that references it; the descriptor
reads `create: Title.from`; a Bun test shows invalid input throws and valid input is normalized.

**Acceptance Scenarios**:

1. **Given** `[JsonFactory]` on `Title.From`, **Then** the descriptor is
   `{ kind: "branded", create: Title.from }`.
2. **Given** no `[JsonFactory]`, **Then** the descriptor stays `{ kind: "branded", create:
   Title.create }` (byte-identical to today).
3. **Given** `[JsonFactory, Name("parse")]`, **Then** the descriptor is `create: Title.parse`, and
   the companion namespace declares `parse`.
4. **Given** a **private** `[JsonFactory]` static method, **Then** it is accepted (private statics
   are emitted into the companion namespace).
5. **Given** the branded type in a referenced `[EmitPackage]` library, **Then** the consumer's
   context still emits `create: Title.from` and imports `Title` from the library package.
6. **Given** a `Title` nested in a nullable, array or map-value position, **Then** the factory runs
   for every element.

---

### User Story 4 - Misconfiguration is reported at transpile time (Priority: P2)

A developer who writes a polymorphic hierarchy, a factory or a root that the generated TypeScript
cannot honor faithfully gets a Metano diagnostic pointing at the attribute, not a silently wrong
wire format.

**Acceptance Scenarios**:

1. **Given** `[JsonDerivedType(typeof(NotASubtype), "x")]`, a duplicate tag, a duplicate derived
   type, an interface or `[PlainObject]` base or variant, an open generic derived type, or a data
   property whose final JSON name equals the discriminator, **Then** `MS0032` (Error) is reported.
2. **Given** a derived type registered without a tag, **Then** `MS0033` (Warning) explains it will
   not round-trip (STJ reads it back as the base type too).
3. **Given** `[JsonFactory]` on an instance method, a method with the wrong parameter or return
   type, two factories on one type, an overloaded factory, a non-emitted (`[Ignore]`, `[Emit]`,
   `[Import]`, `[Inline]`) method, or a method of a non-branded type, **Then** `MS0034` (Error).
4. **Given** an open generic, error-typed, `object`, or non-collection interface root, **Then**
   `MS0035` (Error) instead of a silent drop.
5. **Given** two roots (or a root and a reached type) that map to the same getter name, **Then**
   `MS0036` (Warning) names both and suggests `TypeInfoPropertyName`; the first one wins, mirroring
   STJ's `SYSLIB1031`.

---

### User Story 5 - Fidelity is proven against real .NET output in CI (Priority: P2)

A maintainer changing the runtime or generator learns immediately when the TypeScript output stops
matching what STJ writes, because CI compares both sides against the same checked-in fixtures.

**Acceptance Scenarios**:

1. **Given** the conformance sample's types, **When** the .NET job runs, **Then** a TUnit test
   serializes every fixture case with a real STJ source-generated context and asserts each
   fixture file is byte-identical (and every error fixture throws the recorded exception type and
   message prefix).
2. **Given** the same fixtures, **When** the JS job runs, **Then** the generated context reproduces
   them byte-for-byte and throws the same exception class name and message prefix for error cases.

### Edge Cases

- **Out-of-order tag**: `{"Path":"a","kind":"image",...}` is treated as *missing* tag (STJ
  default). With `[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]` on the
  context, the tag may appear anywhere.
- **Null / non-scalar tag**: `{"kind":null,...}` throws `JsonException` "The '$id', '$ref' or
  '$type' metadata properties must be JSON strings. Current token type is 'Null'." (the STJ text
  names `$type` whatever the configured name is).
- **Concrete base**: an instance of the base itself is written with no tag; a missing tag reads the
  base; `IgnoreUnrecognizedTypeDiscriminators = true` reads an unknown tag as the base, but on an
  abstract base an unknown tag still throws `NotSupportedException`.
- **Unregistered runtime subtype** on serialize: default `FailSerialization` throws
  `NotSupportedException` "Runtime type 'X' is not supported by polymorphic type 'B'.";
  `FallBackToBaseType` writes the base contract with no tag (also for an abstract base);
  `FallBackToNearestAncestor` writes the nearest registered ancestor's tag and contract, or the base
  contract when none is registered.
- **Intermediate static type**: with `HLeaf : HMid : HBase` and only `HBase` polymorphic, a leaf
  serialized through `hMid` writes `{"B":2,"A":1}` (no tag, no `C`); reading
  `{"t":"leaf",...}` through `hMid` ignores the tag.
- **Deserializing through a derived spec** a payload that carries the tag: the tag is ignored as an
  unknown property.
- **Duplicate tag keys** (`{"kind":"a","kind":"b"}`): STJ throws; `JSON.parse` keeps the last value
  before Metano sees it — documented, undetectable divergence.
- **Integer-like JSON property names** (`"1"`, `"42"`) in a polymorphic hierarchy: JavaScript
  objects enumerate them before every other key, so the tag can neither be written nor read first →
  rejected with `MS0032`.
- **Numbers JavaScript prints differently**: STJ writes `1E+21` and `-0`; `JSON.stringify` writes
  `1e+21` and `0`. Byte identity is not promised for them; fixtures avoid them.
- **Dispatch by constructor identity**: values copied with spread / `structuredClone`, or classes
  loaded twice from duplicated package copies, are "unregistered runtime types" and fail under the
  default handling.
- **Custom converter for kind `ref`**: context converters match by descriptor kind only, so such a
  converter bypasses polymorphic dispatch for every `ref` — documented, unchanged.
- **Null JSON root** for a `ValueSpec`: see OPEN-6.
- **Dictionary with non-string keys** (`Dictionary<int, string>` writes `{"1":"a"}`): keys are
  deserialized from strings by the key converter as today; a numeric `[Branded]` key factory
  receives the string. Pre-existing, documented, out of scope.
- **`[StringEnum]` root**: Metano writes the string member; STJ writes the string only with a
  `JsonStringEnumConverter` (otherwise the number). Pre-existing type-mapping contract, unchanged.
- **Closed generic object root** `Page<EpisodeSummary>`: getter `pageEpisodeSummary`,
  `TypeSpec<Page<EpisodeSummary>>`; two closed instantiations share one constructor, so
  `resolve(Page)` returns the last registered one (documented).
- **Same simple name in two namespaces** (`N1.Dup`, `N2.Dup`): today deduplicated silently; now
  `MS0036`.

## Requirements *(mandatory)*

### Functional Requirements

#### A. Polymorphism (#268)

- **FR-001**: The generator MUST read, from the type's **own** attributes only (both STJ attributes
  are `Inherited = false`), matched by fully qualified metadata name:
  `JsonDerivedTypeAttribute(Type)`, `(Type, string)`, `(Type, int)`; and
  `JsonPolymorphicAttribute.TypeDiscriminatorPropertyName` (default `$type`),
  `.UnknownDerivedTypeHandling` (default `FailSerialization`) and
  `.IgnoreUnrecognizedTypeDiscriminators` (default `false`). A type is **polymorphic** when it has
  at least one `[JsonDerivedType]`, with or without `[JsonPolymorphic]`.
- **FR-002**: The generator MUST read `AllowOutOfOrderMetadataProperties` from the context's
  `[JsonSourceGenerationOptions]` and apply it to every polymorphic spec of that context.
- **FR-003**: The contract closure (#250) MUST include every type named by a `[JsonDerivedType]` of
  a type already in the closure, transitively, together with its transpilable base chain and the
  types reachable from its properties — mirroring STJ, which exposes a `JsonTypeInfo` for every
  registered derived type. Discovery stays deterministic (breadth-first, attribute order).
- **FR-004**: A polymorphic type's spec MUST carry a data-only `polymorphism` descriptor: the
  discriminator name **verbatim** (never passed through the naming policy), whether the C# base is
  abstract, and the derived entries in attribute order, each with its tag (`string`, `number`, or
  absent) and a **lazy** link (`() => this.<getter>`) to its spec. Non-default options
  (`unknownDerivedType`, `ignoreUnrecognizedTags`, `allowOutOfOrderMetadata`) are emitted only when
  set. Specs of non-polymorphic types MUST NOT change.
- **FR-005**: Serializing through a polymorphic spec MUST resolve the value's runtime constructor:
  the base itself → base contract, no tag; a registered derived type → its tag (if any) **as the
  first JSON property**, then its contract; otherwise per `UnknownDerivedTypeHandling`
  (`FailSerialization` → `NotSupportedException`; `FallBackToBaseType` → base contract, no tag;
  `FallBackToNearestAncestor` → nearest registered ancestor in the prototype chain, else the base
  contract). The dispatch MUST apply wherever the spec is used: the root, `ref`, and through
  `array`, `hashSet`, `map` and `nullable` descriptors.
- **FR-006**: Serializing through a non-polymorphic spec (including a derived type's own spec and an
  intermediate type of a polymorphic hierarchy) MUST NOT write a tag and MUST NOT re-dispatch.
- **FR-007**: Deserializing through a polymorphic spec MUST: treat the tag as present only when the
  discriminator name matches **case-sensitively** and — unless `allowOutOfOrderMetadata` — it is
  the first key; reject a present tag that is neither a JSON string nor a JSON number with
  `JsonException`; match string and number tags as distinct values (`1` ≠ `"1"`); on a known tag
  use that derived contract (the tag key is otherwise ignored); on an unknown tag throw
  `JsonException` "Read unrecognized type discriminator id '<id>'." unless
  `ignoreUnrecognizedTags` is set, in which case a concrete base reads the base contract and an
  abstract base throws `NotSupportedException`; on a missing tag, an abstract base throws
  `NotSupportedException` "The JSON payload for polymorphic interface or abstract type '<Base>' must
  specify a type discriminator." and a concrete base reads the base contract.
- **FR-008**: The runtime MUST export `JsonException` and `NotSupportedException` (both extend
  `Error`, `name` set to the class name). Their messages are STJ's text **without** the
  `Path: … | LineNumber: … | BytePositionInLine: …` suffix (OPEN-5).
- **FR-009**: For **every** spec (polymorphic or not), serialization MUST write properties
  most-derived first: the spec's own properties, then its base's, recursively; the tag, when
  written, precedes all of them. Deserialization is key-order-insensitive except for the tag rule
  of FR-007. The exported `collectProperties` keeps its base-first contract; a new
  `collectWireProperties` exposes the wire order.
- **FR-010**: The `base:` link MUST be emitted under `[assembly: TranspileAssembly]` as well as
  under `[Transpile]` (today `TranspilableBaseOf` calls `IsTranspilable` without the assembly-wide
  arguments, so inherited properties are silently dropped from the wire under `TranspileAssembly`).
- **FR-011**: An abstract polymorphic base's `factory` stays the throwing factory introduced by
  #238; it is unreachable on the polymorphic path. A non-polymorphic abstract base keeps today's
  behavior.
- **FR-012**: The generator MUST report `MS0032` (`InvalidJsonPolymorphism`, Error) at the offending
  attribute when: a derived type is not a proper subtype of the base, or is not transpilable for the
  target; a tag or a derived type is registered twice on one base; the base or a derived type is an
  interface or `[PlainObject]` (no constructor identity to dispatch on, v1 — OPEN-8); a derived type
  is generic (open or closed, v1); a data property of any contract in the hierarchy has a final JSON
  name (after `[JsonPropertyName]` / policy) equal to the discriminator (STJ throws
  `InvalidOperationException` for this); a JSON property name in the hierarchy is integer-like.
- **FR-013**: The generator MUST report `MS0033` (`LossyJsonPolymorphism`, Warning) when a derived
  type is registered without a tag (serializes without a tag and reads back as the base, exactly
  as in STJ), and when a derived type is itself polymorphic (its own `[JsonDerivedType]`s are not
  re-dispatched through the outer base — OPEN-9).
- **FR-014**: The discriminator name and values MUST be emitted verbatim. Data property names keep
  today's rule (`[JsonPropertyName]`, else the context naming policy, else the C# name), with the
  Web-defaults correction of OPEN-4.

#### B. Validating branded factory (#269)

- **FR-020**: `Metano.Annotations` MUST provide `JsonFactoryAttribute`
  (`[AttributeUsage(AttributeTargets.Method)]`, sealed, no members).
- **FR-021**: When a `[Branded]` / `[InlineWrapper]` type declares exactly one valid
  `[JsonFactory]` method, every branded descriptor the generator emits for that type MUST be
  `{ kind: "branded", create: <TsType>.<tsMethodName> }`, where `tsMethodName` is computed by the
  same naming rule the branded companion uses for its static methods (`[Name]` override, else
  camelCase). Without `[JsonFactory]` the descriptor MUST stay byte-identical to today
  (`create: <TsType>.create`). `System.Guid` (`UUID.create`) is unchanged.
- **FR-022**: A `[JsonFactory]` method is valid when it is static, emitted for the target (not
  `[Ignore]`, `[Emit]`, `[Import]`, `[Inline]`), has exactly one parameter whose type is the
  wrapper's underlying primitive, returns the containing type, is the only `[JsonFactory]` of its
  type, and has no same-named static overload. Any accessibility is valid. Otherwise the generator
  MUST report `MS0034` (`InvalidJsonFactory`, Error) at the method and keep `create`.
- **FR-023**: `[JsonFactory]` on a method of a type that is not `[Branded]` / `[InlineWrapper]` MUST
  report `MS0034`.
- **FR-024**: Exceptions thrown by the factory MUST propagate unchanged from `deserialize` (no
  wrapping), at any nesting depth.
- **FR-025**: The runtime `BrandedDescriptor` contract and `brandedConverter` MUST stay unchanged;
  only the generated `create` reference changes.
- **FR-026**: The feature is TypeScript-only on the C# side: Metano ships no STJ converter that
  honors `[JsonFactory]` (the C# wire shape of a branded struct already depends on the user's
  converter). A shipped converter is a separate future FR.

#### C. Non-object roots and STJ naming (#270)

- **FR-030**: `[JsonSerializable]` roots MUST accept named types **and** array types. A root whose
  classification (the same classifier used for property types) is the object kind keeps the
  `TypeSpec` path; every other kind (`array`, `hashSet`, `map`, `primitive`, `decimal`,
  `temporal`, `branded`, `enum`, `numericEnum`, `nullable`) MUST emit a `ValueSpec` whose
  `descriptor` is exactly the descriptor that type gets as a property.
- **FR-031**: Every context member name MUST come from one naming function, used by roots, `ref`
  descriptors, `base` links, polymorphic derived links and closure deduplication: an explicit
  `TypeInfoPropertyName` wins (for that type everywhere in the context); a non-generic named type
  uses its resolved TS name (today's rule, honors `[Name]`); a closed generic type is its name
  without arity followed by the names of its type arguments (`ListEpisodeSummary`,
  `DictionaryStringEpisodeSummary`); an array is the element name + `Array`; `Nullable<T>` is
  `Nullable` + the name of `T`; BCL primitives and well-known structs use their CLR names (`String`,
  `Int32`, `Double`, `Guid`, `DateTime`, `Decimal`). The getter is the camelCase of the name; the
  backing field is `_` + getter.
- **FR-032**: `ValueSpec<T>` and `TypeSpec<T>` MUST be parameterized with the mapped TS type of the
  root (`EpisodeSummary[]`, `Map<string, EpisodeSummary>`, `HashSet<string>`, `Page<EpisodeSummary>`),
  never with a bare generic name.
- **FR-033**: The object types reachable from a non-object root (element, map value, nullable
  inner) and closed generic object roots MUST enter the contract closure under the #250 rules.
- **FR-034**: The runtime MUST add `ValueSpec<T> { descriptor: TypeDescriptor }` and
  `JsonTypeInfo<T> = TypeSpec<T> | ValueSpec<T>`. `JsonSerializer.serialize` and
  `BoundSerializer.serialize` MUST keep the `TypeSpec<T>` overload (returning
  `Record<string, unknown>`) and add a `ValueSpec<T>` overload (returning `unknown`); both
  `deserialize` methods MUST accept `JsonTypeInfo<T>`. A `ValueSpec` is converted through the same
  converter resolution as a property, so context converters apply by kind and polymorphic elements
  dispatch per FR-005/FR-007. `specFor` and the constructor registry are unchanged.
- **FR-035**: The generator MUST report `MS0035` (`InvalidJsonSerializableRoot`, Error) at the
  attribute for a root that is an open generic or type parameter, an error type, `object` /
  `dynamic`, or an interface that is not a recognized collection/dictionary/set — instead of
  dropping it or emitting an invalid spec.
- **FR-036**: The generator MUST report `MS0036` (`JsonTypeInfoNameCollision`, Warning) when two
  distinct types map to the same member name; the first one in discovery order keeps the name and
  the other is not emitted (mirrors STJ `SYSLIB1031`).
- **FR-037**: Null JSON at a `ValueSpec` root follows OPEN-6.

#### D. Cross-cutting

- **FR-040**: Contexts that use none of the new attributes and only object roots MUST produce
  byte-identical generated TypeScript, except where FR-010 adds a previously missing `base:` link.
- **FR-041**: The runtime additions MUST be additive and exported from
  `metano-runtime/system/json` (`ValueSpec`, `JsonTypeInfo`, `PolymorphismSpec`, `DerivedTypeSpec`,
  `TypeDiscriminator`, `UnknownDerivedTypeHandling`, `JsonException`, `NotSupportedException`,
  `collectWireProperties`). The generated context keeps importing only from
  `metano-runtime/system/json` (no new value import).
- **FR-042**: The Dart target is out of scope (it has no JSON context; roadmap RT-02).
- **FR-043**: Documentation MUST be updated: `docs/serialization.md` (polymorphism, roots, factory,
  wire order, error classes, the documented divergences, and the two false claims about base
  properties in factories and `new Decimal`), the attribute catalog (`[JsonFactory]`), the
  diagnostic catalog (`MS0032`–`MS0036`), and the feature-support matrix Serialization rows.
- **FR-044**: Tests MUST cover every scenario of US1–US5 and every diagnostic rule, with at least
  one golden file per area (see plan.md "Test strategy") and the STJ conformance fixtures checked
  on both the .NET and Bun sides in CI.

### Diagnostics (reserved codes, see OPEN-2)

| Code | Name | Severity | Raised for |
|---|---|---|---|
| `MS0032` | `InvalidJsonPolymorphism` | Error | FR-012 rules |
| `MS0033` | `LossyJsonPolymorphism` | Warning | FR-013 rules |
| `MS0034` | `InvalidJsonFactory` | Error | FR-022, FR-023 |
| `MS0035` | `InvalidJsonSerializableRoot` | Error | FR-035 |
| `MS0036` | `JsonTypeInfoNameCollision` | Warning | FR-036 |

### Key Entities

- **Polymorphism descriptor** — data on a base `TypeSpec`: discriminator, abstract flag, derived
  entries (tag + lazy spec), unknown-type handling, ignore flag, out-of-order flag.
- **Derived entry** — one `[JsonDerivedType]`: optional tag (`string | number`) and a thunk to the
  derived type's spec (its `type` is read from that spec, never duplicated).
- **Value spec** — a root that is not an object: a single `TypeDescriptor`.
- **JSON type info name** — the STJ-compatible member name of a context entry, produced by one
  function.
- **JSON factory** — the one static method of a branded type that deserialization calls.

## Open decisions

Every item below has a recommended default that this spec and plan assume. The maintainer
confirms or overrides each before implementation starts.

- **OPEN-1 — Directory number.** The task named `008-polymorphic-json`, but `008-exports-subpath-root`
  exists on `main`. *Recommended*: keep `009-polymorphic-json`; the branch name stays as created.
- **OPEN-2 — Diagnostic numbers.** `MS0029`/`MS0030` are used (#267) and `MS0031` is claimed by the
  in-flight `fix/278-explicit-interface-members`. *Recommended*: reserve `MS0032`–`MS0036` as above;
  re-check at merge time and renumber here if another branch lands first.
- **OPEN-3 — Option coverage in v1.** *Recommended*: all of int tags, the three
  `UnknownDerivedTypeHandling` modes, `IgnoreUnrecognizedTypeDiscriminators` and
  `AllowOutOfOrderMetadataProperties` (each measured, each a few lines). `[JsonPropertyOrder]` is
  **out** (not polymorphism-specific; it would need a `PropertySpec.order`). Alternative: report
  `MS0033` when `[JsonPropertyOrder]` appears in a polymorphic hierarchy.
- **OPEN-4 — `JsonSerializerDefaults.Web`.** `ReadNamingPolicy` only reads the named
  `PropertyNamingPolicy` argument, so `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]`
  contexts keep PascalCase in TS while .NET writes camelCase (measured: `{"kind":"image","path":"a",
  "seconds":1.5}`). *Recommended*: fix in this feature (an explicit `PropertyNamingPolicy` still
  wins), because round-trip parity is the feature's promise; ship it as its own `fix:` commit.
- **OPEN-5 — Error detail.** *Recommended*: STJ-named classes, messages without the JSON path suffix
  (tracking a path costs on every call). Alternative: track `$.a[0].b` paths for full message
  parity.
- **OPEN-6 — Null JSON at a `ValueSpec` root.** STJ returns `null` for reference-type roots
  (`List<Clip>`, `string`) and throws for value-type roots (`int`). *Recommended*: mirror STJ — the
  generator marks `ValueSpec` roots of reference types `acceptsNull: true`; `deserialize` returns
  `null` for them and throws `JsonException` otherwise. Alternative: always throw (consistent with
  `TypeSpec` roots, documented divergence).
- **OPEN-7 — Getter renames for closed generic roots.** FR-031 renames today's (non-compiling)
  `list` / `page` getters to `listEpisodeSummary` / `pageEpisodeSummary`. *Recommended*: accept
  (the old output never compiled for collections; generic object roots had no tests). Call it out
  in the changelog.
- **OPEN-8 — Interface and `[PlainObject]` hierarchies.** *Recommended*: reject with `MS0032` in v1.
  Later: dispatch through generated `[GenerateGuard]` predicates or a stored tag field.
- **OPEN-9 — A derived type that is itself polymorphic.** STJ behavior for nested polymorphism was
  not measured. *Recommended*: `MS0033` warning and no re-dispatch until a probe settles it.
- **OPEN-10 — Polymorphic bases from referenced packages.** The #250 closure only discovers
  current-assembly types. *Recommended*: v1 supports a polymorphic base listed as a root of the
  consumer context when its derived types are importable from the same package; otherwise `MS0032`.
- **OPEN-11 — Conformance sample name.** *Recommended*: `samples/SampleJsonContracts` →
  `targets/js/sample-json-contracts` (covers all three issues).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the conformance fixtures (valid cases) are byte-identical between STJ
  source-gen output and the generated TypeScript serializer, and every TS value read from a fixture
  is an instance of the expected class.
- **SC-002**: 100% of the error fixtures throw the same exception class name and message prefix on
  both sides.
- **SC-003**: A context with one root of each supported shape (≥ 15 roots) compiles under
  `tsgo --strict` with 0 errors (today: the file does not compile).
- **SC-004**: Every existing golden file and JSON test passes unchanged, except the documented
  FR-010 `base:` additions and OPEN-7 renames.
- **SC-005**: With `[JsonFactory]`, 0 invalid branded values are accepted by `deserialize` in the
  factory test matrix (blank, too long, wrong primitive); without it, output is byte-identical.
- **SC-006**: Each of the 5 diagnostics has at least one test per rule, and no rule produces an
  invalid `.ts` file instead of a diagnostic.

## Assumptions

- .NET 10 STJ behavior (measured on SDK 10.0.401) is the reference; later STJ changes are tracked
  by the conformance fixtures failing in CI.
- Generated packages that use polymorphism or value specs need the `metano-runtime` version that
  ships these additions. How the generated `package.json` pins the `metano-runtime` range was not
  re-checked for this spec; the plan verifies it before the runtime release (plan.md "Compatibility").
- Deserialization is fed by `JSON.parse` output, so lexical facts STJ sees (duplicate keys, exact
  number text) are not observable to the runtime.
- C# 15 `union` types and STJ union support (.NET 11 RC) are a separate future feature.
