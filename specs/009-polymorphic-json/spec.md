# Feature Specification: System.Text.Json-faithful JSON contracts (polymorphism, validating branded factories, non-object roots)

**Feature Branch**: `spec/008-polymorphic-json` (spec directory `009-polymorphic-json`; `008` is
already taken by [`008-exports-subpath-root`](../008-exports-subpath-root/spec.md), DECIDED-1)

**Created**: 2026-10-31

**Status**: Draft. The maintainer settled the design decisions (see [Decisions](#decisions)). Two
new questions stay open: OPEN-18 and OPEN-19.

**Release impact**: **breaking** (`feat!`, next major version of the Metano release, which
versions the NuGet tools and `metano-runtime` together). `deserialize` through a `TypeSpec` now returns `T | null`, as STJ does
(FR-037, DECIDED-6). The maintainer may ship this major together with the C# unions
specification (PR #310) so that one major version carries both. See
[Migration](#migration-breaking-changes).

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
#273 / PR #301 (package.json exports on full cache reuse), #286 (cross-package imports for types
referenced only in expression bodies). Requirement identifiers below are local to this feature
(`009 FR-NNN`), following the convention of every feature spec since 002; the baseline
`FR-001`–`FR-048` space is not renumbered.

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
   are emitted as bogus class specs. Closed generic object types reached through a property
   (`record Feed(Page<EpisodeSummary> Latest)`) produce a `ref` to a getter that does not exist
   (TS2339).

This feature closes all three by **mirroring System.Text.Json (STJ)**: what .NET writes, the
generated TypeScript reads; what TypeScript writes, .NET reads; and for every case the shared
conformance manifest marks as checked on both sides, the two writers produce **byte-identical**
JSON. Every case where Metano knowingly differs is listed under
[Documented divergences](#documented-divergences) and marked as such in the manifest. Behaviors
were measured on .NET 10 (SDK 10.0.401), reflection and source generation alike (research.md
R1–R4, R8).

### Maintainer decisions (summary)

- Mirror STJ: discriminator written first, derived-first property order, case-sensitive
  discriminator matching, default discriminator name `$type`, string **and** integer tags,
  unknown/missing-tag errors with STJ-like messages.
- `[JsonFactory]` is an explicit opt-in on one static method (no `From` naming convention).
- One specification covers #268, #269 and #270.
- **v1 option set**: int and string tags, the three `UnknownDerivedTypeHandling` modes,
  `IgnoreUnrecognizedTypeDiscriminators` and `AllowOutOfOrderMetadataProperties`.
  `[JsonPropertyOrder]` is out of scope and raises `MS0037` when used (DECIDED-3).
- **Interface and `[PlainObject]` hierarchies are supported in v1.** Reading dispatches on the
  discriminator, which is authoritative. Writing dispatches on runtime type tests: `instanceof` for
  class-backed variants, and a guard that the generator synthesizes for each `[PlainObject]`
  variant. Guards are synthesized automatically, as decided for C# unions. Two variants whose
  shapes cannot be told apart on write are a build error (`MS0032`) (DECIDED-8, FR-018, FR-019).
- **Full `JsonSerializerDefaults.Web` parity**: camelCase names, case-insensitive property
  matching and numbers read from strings (DECIDED-4, FR-048).
- **Full null-root parity**: `deserialize` returns `T | null` as in STJ. This is a breaking
  public API change (DECIDED-6, FR-037, FR-049).

All decisions are listed in [Decisions](#decisions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Round-trip a polymorphic record hierarchy with .NET (Priority: P1)

A developer shares `Clip` (abstract record with `[JsonPolymorphic(TypeDiscriminatorPropertyName =
"kind")]` and `[JsonDerivedType(typeof(ImageClip), "image")]`, `[JsonDerivedType(typeof(SilenceClip),
"silence")]`) between an ASP.NET API and a TypeScript client. The client deserializes what the API
sends into the right subclass, and sends back JSON the API deserializes without custom code.

**Why this priority**: It is the issue that blocks video-studio F2 and the largest fidelity gap.

**Independent Test**: The shared manifest `cases.json` lists each case with its direction. The .NET
side builds each value and asserts STJ source-gen output equals the recorded text; the Bun side
builds the same id and asserts the generated context writes the same text (write rows), reads it
into an instance of the expected class (read rows), or both (roundtrip rows).

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
9. **Given** a concrete `SBase` that registers **itself** (`[JsonDerivedType(typeof(SBase), "b")]`),
   **When** an `SBase` is serialized through `sBase`, **Then** the text is `{"$type":"b","V":1}`;
   **and** that text reads back as an `SBase`.
10. **Given** `GDer : GBase<int>` registered on `GBase<T>` with tag `"g"`, **When** a `GDer` is
    serialized through the `gBaseInt32` spec, **Then** the text is `{"$type":"g","Extra":2,"Value":1}`
    (FR-015, DECIDED-13).
11. **Given** an interface base `IShape` with `[JsonDerivedType(typeof(Sq), "sq")]` and a record
    `Sq(int Side) : IShape`, **When** an `Sq` is serialized through the `shape` spec, **Then** the
    text is `{"$type":"sq","Side":2}`; **and** that text reads back as an `Sq` instance; **and**
    `{"Side":2}` throws `NotSupportedException` "The JSON payload for polymorphic interface or
    abstract type 'IShape' must specify a type discriminator." (FR-018).
12. **Given** a `[PlainObject]` base `Shape` with `[PlainObject]` variants `Circle(double Radius)`
    and `Rect(double W, double H)` tagged `"circle"` and `"rect"`, **When** the plain object
    `{ w: 1, h: 2 }` is serialized through `shape`, **Then** the synthesized `isRect` guard selects
    `Rect` and the text is `{"$type":"rect","W":1,"H":2}`; **and** `{"$type":"circle","Radius":1}`
    reads back as a plain object `{ radius: 1 }` (FR-019).
13. **Given** `[PlainObject]` variants `Point(double X)` and `Point3(double X, double Y, double Z)`
    where `Point3` derives from `Point`, **When** `{ x: 1, y: 2, z: 3 }` is serialized through the
    base, **Then** `Point3` is selected, because a variant whose guard checks more required members
    is tested first (FR-019).
14. **Given** two `[PlainObject]` variants with the same required members and the same member kinds
    (`A(string Name)` and `B(string Name)`), **When** transpiled, **Then** `MS0032` reports that
    their shapes cannot be told apart on write (FR-019).

---

### User Story 2 - Serialize collection, dictionary, primitive, enum and branded roots (Priority: P1)

A developer declares `[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]` (and arrays,
dictionaries, sets, primitives, enums, branded types, `int?`) on the context, exactly as in C#, and
gets a compiling, usable context member for each, named like STJ's `JsonTypeInfo` properties.

**Why this priority**: Every list endpoint needs a collection root; today the output does not
compile, so the whole context file is unusable.

**Independent Test**: Transpile a context with one root of each shape; `tsgo --strict` reports zero
errors; each getter's name matches the STJ property name camel-cased; one manifest case per
value-root kind runs on both sides (or is marked divergent, see Documented divergences).

**Acceptance Scenarios**:

1. **Given** `[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]`, **Then** the context has
   `get iReadOnlyListEpisodeSummary(): ValueSpec<EpisodeSummary[] | null>` whose descriptor is an
   `array` of `ref` to `episodeSummary`, and `episodeSummary` is in the context even if not listed.
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
7. **Given** an `IEnumerable<Clip>` root (TS `Iterable<Clip>`) and a `Set` or generator value,
   **When** serialized, **Then** the result is a JSON array (no `TypeError`), and the same holds
   for an `IEnumerable<T>` property (FR-038).
8. **Given** `record Feed(Page<EpisodeSummary> Latest)` as the only root, **Then** the context has
   `pageEpisodeSummary` and the `ref` points at it; the file compiles under `tsgo --strict`
   (FR-033).
9. **Given** the JSON text `null`, **When** it is deserialized through `clip` (a class root),
   `listClip` or `string`, **Then** the result is `null`, as in STJ, and
   `JsonSerializer.deserialize(data, ctx.clip)` is typed `Clip | null` (FR-037, breaking).
10. **Given** the JSON text `null`, **When** it is deserialized through `int32` or through the
    spec of a C# `struct` contract (`record struct SPoint`), **Then** a `JsonException` is thrown
    (`The JSON value could not be converted to SPoint.` for the struct), as in STJ.
11. **Given** a `null` value, **When** it is serialized through `clip`, **Then** the result is
    JSON `null`, as in STJ.

---

### User Story 3 - Deserialize branded values through their validating factory (Priority: P2)

A developer marks `[JsonFactory] public static Title From(string value)` on a `[Branded]` record
struct. Deserializing a payload containing `"title":"   "` through the generated context throws the
same validation error the TS `Title.from` throws; `"  ok "` becomes `"ok"`.

**Why this priority**: Correctness/security at the UI boundary; small and independent.

**Independent Test**: Transpile the branded type and a context that references it; the descriptor
reads `create: Title.from`; a Bun test shows invalid input throws and valid input is normalized.
This story is **TypeScript-only** (FR-026): its manifest rows run on the Bun side only.

**Acceptance Scenarios**:

1. **Given** `[JsonFactory]` on `Title.From`, **Then** the descriptor is
   `{ kind: "branded", create: Title.from }`.
2. **Given** no `[JsonFactory]`, **Then** the descriptor stays `{ kind: "branded", create:
   Title.create }` (byte-identical to today).
3. **Given** `[JsonFactory, Name("parse")]`, **Then** the descriptor is `create: Title.parse`, and
   the companion namespace declares `parse`.
4. **Given** a **private** `[JsonFactory]` static method, **Then** it is accepted (private statics
   are emitted into the companion namespace), except in an `[EmitPackage]` library, where it is
   `MS0034` (consumers compiled against the library's metadata cannot see it).
5. **Given** the branded type in a referenced `[EmitPackage]` library, **Then** the consumer's
   context still emits `create: Title.from` and imports `Title` from the library package.
6. **Given** a `Title` nested in a nullable, array, map-value or **map-key** position, **Then** the
   factory runs for every element / key.
7. **Given** a branded type nested in another type (`Episode.Title`), **Then** the descriptor uses
   the qualified TS reference (`create: Episode.Title.from`).

---

### User Story 4 - Misconfiguration is reported at transpile time (Priority: P2)

A developer who writes a polymorphic hierarchy, a factory or a root that the generated TypeScript
cannot honor faithfully gets a Metano diagnostic pointing at the attribute, not a silently wrong
wire format.

**Acceptance Scenarios**:

1. **Given** `[JsonDerivedType(typeof(NotASubtype), "x")]` (not assignable to the base), a
   duplicate tag, a duplicate derived type, an abstract derived type without
   `FallBackToNearestAncestor`, a derived entry that is itself an interface (OPEN-18), two
   `[PlainObject]` variants whose shapes cannot be told apart on write (FR-019), a generic derived
   type, or a data property whose final JSON name equals the discriminator, **Then** `MS0032`
   (Error) is reported. Registering the base type itself is **not** an error, and neither is an
   interface or `[PlainObject]` base (FR-018, FR-019).
2. **Given** a derived type registered without a tag, **Then** `MS0033` (Warning) explains it will
   not round-trip (STJ reads it back as the base type too).
3. **Given** `[JsonFactory]` on an instance method, a method with the wrong parameter or return
   type, two factories on one type, an overloaded factory, a non-emitted (`[Ignore]`, `[Emit]`,
   `[Import]`, `[Inline]`) method, a conversion operator, a factory whose TS name equals a
   synthesized companion member (`create`, `toString`), or a method of a non-branded type, **Then**
   `MS0034` (Error).
4. **Given** an open generic, error-typed, `object`, non-collection interface, non-transpilable
   object-kind (e.g. `KeyValuePair<,>`, `ValueTuple`, a `[NoTranspile]` type, a type from a
   non-Metano assembly) or multi-dimensional array root, **Then** `MS0035` (Error) instead of a
   silent drop or a bogus spec.
5. **Given** two types that map to the same getter name, **Then** `MS0036` names both and suggests
   `TypeInfoPropertyName`; it is an **Error** when the losing type is the target of any `ref`,
   `base` or derived link, and a Warning only when the loser is an unreferenced root (FR-036,
   DECIDED-14). A getter name equal to a `SerializerContext` member is always an Error.
6. **Given** a context whose `[JsonSourceGenerationOptions]` sets a wire-affecting option the
   generator does not mirror (e.g. `DefaultIgnoreCondition`, `NumberHandling = WriteAsString`,
   `ReferenceHandler`, `Converters`), a root / property of type `byte[]`, or a contract member
   with `[JsonPropertyOrder]`, **Then** `MS0037` (Warning) names the option or member (FR-046).
   `PropertyNameCaseInsensitive` and `NumberHandling = AllowReadingFromString` are mirrored
   (FR-048) and are not reported.

---

### User Story 5 - Fidelity is proven against real .NET output in CI (Priority: P2)

A maintainer changing the runtime or generator learns immediately when the TypeScript output stops
matching what STJ writes, because CI checks both sides against the same checked-in manifest.

**Acceptance Scenarios**:

1. **Given** the conformance sample's types and `cases.json`, **When** the .NET job runs, **Then** a
   TUnit test builds every case with `sides` including `stj`, serializes write/roundtrip rows with
   a real STJ source-generated context and asserts byte equality, deserializes read/roundtrip rows
   and asserts the runtime type, and asserts every error row throws the recorded exception type and
   message prefix.
2. **Given** the same manifest, **When** the JS job runs, **Then** the generated context does the
   same for every row with `sides` including `ts`, asserting `instanceof` only on read and
   roundtrip rows.
3. **Given** a manifest id without a builder on either side, **Then** that side's test fails (the
   two sides cannot drift by omission).

---

### User Story 6 - Read what a `JsonSerializerDefaults.Web` API sends (Priority: P2)

A developer declares the context with `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]`,
like most ASP.NET Core APIs. The generated TypeScript context writes camelCase names, and it reads
everything the .NET side accepts under Web defaults: property names in any letter case, and
numbers sent as JSON strings.

**Why this priority**: ASP.NET Core minimal APIs and MVC use Web defaults. Without this, every
payload written by .NET has different names from what the TypeScript context expects.

**Independent Test**: The manifest has Web-context rows (`sides` both) for camelCase writes,
case-insensitive reads and numbers read from strings, plus error rows for the strings STJ rejects.

**Acceptance Scenarios**:

1. **Given** a Web context and `ImageClip("a", 1.5)`, **When** it is serialized through `clip`,
   **Then** the text is `{"kind":"image","path":"a","seconds":1.5}`; the tag name and value are not
   renamed.
2. **Given** a Web context, **When** `{"kind":"image","PATH":"a","Seconds":1}` is deserialized
   through `clip`, **Then** the result is `ImageClip("a", 1)`; **and** when two keys differ only
   in case (`{"path":"a","Path":"b",…}`), the last one wins (`"b"`), as in STJ.
3. **Given** a Web context, **When** `{"Kind":"image",…}` is deserialized through `clip`, **Then**
   the tag is treated as missing (`NotSupportedException`): the discriminator stays case-sensitive.
4. **Given** a Web context and an `int` property, **When** the JSON value is `"5"`, `"+5"` or
   `"007"`, **Then** it reads as `5`; **and** `"5.0"`, `" 5"`, `"5 "`, `""`, `"1e2"` and `"0x10"`
   throw `JsonException` "The JSON value could not be converted to System.Int32." (FR-048). STJ
   names `System.Int32` for an `int` root, element, dictionary value or settable property, and
   the declaring type for a constructor-bound property; Metano always names `System.Int32`
   (documented divergence, DECIDED-23 c).
5. **Given** a Web context and a `double` property, **When** the JSON value is `"2.5"`, `"1e2"`,
   `"+1.5"`, `"NaN"`, `"Infinity"` or `"-Infinity"`, **Then** it reads as the number; `"nan"` and
   `" 1.5"` throw `JsonException`.
6. **Given** a Web context, **When** a number arrives as a string inside a `List<int>`, as a
   `Dictionary<string, int>` value or at an `int` root, **Then** it is read; **and** an enum
   property given `"1"`, an `int?` property given `"null"` and an int tag given `"1"` are still
   rejected, as in STJ.
7. **Given** `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNameCaseInsensitive =
   false)]`, **Then** reads are case-sensitive again: an explicit option overrides the Web default,
   as it does in STJ.

### Edge Cases

- **Out-of-order tag**: on an abstract or interface base, `{"Path":"a","kind":"image",...}` is
  treated as a *missing* tag (`NotSupportedException`, STJ default). On a concrete base the later
  tag is rejected: `{"V":1,"$type":"d","W":2}` throws `JsonException` "The metadata property is
  either not supported by the type or is not the first property in the deserialized JSON
  object." (measured; also for an unknown tag, under `IgnoreUnrecognizedTypeDiscriminators`, for
  a self-registered base, for a custom discriminator and when an unknown key comes first). With
  `[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]` on the context, the tag
  may appear anywhere (FR-002).
- **`$`-prefixed keys** in a polymorphic payload (measured): `$id` / `$ref` throw the
  "metadata property … not the first property" `JsonException`; `$values` throws "A '$values'
  metadata property must always be preceded by other metadata properties, such as '$id' or
  '$type'."; any other key that starts with `$` (`$x`, `$Type` beside a `$type` discriminator,
  `$type` beside a custom one) throws `JsonException` "Properties that start with '$' are not
  allowed in types that support metadata. Either replace the character or disable reference
  preservation and polymorphic deserialization.". In a later position `$values` throws the
  "metadata property … not the first property" message too. An abstract base whose first key is
  `$id`, `$ref` or `$values` throws those `JsonException`s; with any other first key that is not
  the tag it reports the missing tag first. With `allowOutOfOrderMetadataProperties` the keys are
  checked in document order (DECIDED-21 j). A spec without `polymorphism` reads past such keys.
- **Null / non-scalar tag**: a tag value that is neither a JSON string nor a JSON number throws
  `JsonException` "The '$id', '$ref' or '$type' metadata properties must be JSON strings. Current
  token type is '<Token>'." where `<Token>` follows the FR-007 mapping (`Null`, `True`, `False`,
  `StartObject`, `StartArray`). The STJ text names `$type` whatever the configured name is.
- **Base registered as its own derived type**: allowed (STJ accepts it). A base instance writes the
  base's tag first; that tag reads back as the base contract.
- **Concrete base, not self-registered**: an instance of the base itself is written with no tag; a
  missing tag reads the base; `IgnoreUnrecognizedTypeDiscriminators = true` reads an unknown tag as
  the base, but on an abstract base an unknown tag still throws `NotSupportedException`.
- **Unregistered runtime subtype** on serialize: default `FailSerialization` throws
  `NotSupportedException` "Runtime type 'X' is not supported by polymorphic type 'B'.";
  `FallBackToBaseType` writes the base contract with no tag (also for an abstract base);
  `FallBackToNearestAncestor` writes the nearest registered ancestor's tag and contract, or the base
  contract when none is registered. These rows are **write-only**: what they write does not read
  back as the original class (W12 reads as the ancestor; W13 through an abstract base throws).
- **Only untagged registrations** (measured): STJ reads no metadata at all — a concrete base reads
  `{"$type":"x","V":1}`, `{"V":1,"$x":1}` and `{"$x":1,"V":1}` as itself, and an abstract base
  throws `NotSupportedException` "Deserialization of interface or abstract types is not supported.
  Type 'UAB'." for any payload. Writes still dispatch (`{"W":2,"V":1}`).
- **Abstract derived type**: rejected (`MS0032`) unless the base uses `FallBackToNearestAncestor`,
  where it serves only as a nearest-ancestor target; reading its tag throws
  `NotSupportedException` (FR-017, DECIDED-12).
- **Nested polymorphism** (measured, research.md R1): with `NMid : NBase` both polymorphic, an
  `NMid` through `NBase` writes only the outer tag (`{"t":"mid","B":2,"A":1}`); an `NLeaf`
  registered only on `NMid` fails through `NBase` with `NotSupportedException`; the inner tag is
  ignored on read. Metano mirrors this exactly (no re-dispatch).
- **Intermediate static type**: with `HLeaf : HMid : HBase` and only `HBase` polymorphic, a leaf
  serialized through `hMid` writes `{"B":2,"A":1}` (no tag, no `C`); reading
  `{"t":"leaf",...}` through `hMid` ignores the tag.
- **Deserializing through a derived spec** a payload that carries the tag: the tag is ignored as an
  unknown property.
- **Overridden / `new`-hidden properties**: written once, at the most-derived declaration's
  position (FR-009).
- **Duplicate tag keys**, **integer-like property names**, **number text**, **`1.0` int tags**: see
  Documented divergences.
- **Dispatch by constructor identity**: values of class-backed variants copied with spread /
  `structuredClone`, or classes loaded twice from duplicated package copies, are "unregistered
  runtime types" and fail under the default handling. A spread copy is a plain object, so it is
  only matched by the guard of a `[PlainObject]` variant (FR-019), never by a class variant.
- **Interface base** (measured, research.md R8): a C# interface has no TS constructor, so the base
  spec has no `type`; it behaves as an abstract base (missing tag → `NotSupportedException` naming
  the interface). Class variants dispatch by constructor. Under `FallBackToBaseType` an unregistered
  implementation writes only the interface's properties (`{"Area":3}`).
- **`[PlainObject]` variants carry no runtime type.** A plain object that matches no variant guard
  is an "unregistered runtime type": it fails under `FailSerialization` with the type name
  `Object`, and the base guard (concrete base) or the handling mode applies otherwise. A value of a
  C# subtype that is not registered, but whose shape satisfies a registered variant's guard, is
  written as that variant (documented divergence: STJ knows the real runtime type).
- **Mixed hierarchies**: class-backed and `[PlainObject]` variants may share one base. A class
  instance is dispatched by constructor only, and a plain object (prototype `Object.prototype` or
  `null`) by guards only, so the two families never overlap.
- **Case-insensitive reads** (FR-048): when two JSON keys match one property case-insensitively,
  the last one wins; the discriminator is always matched case-sensitively; an exact-case match is
  not preferred over an earlier case-insensitive one (STJ applies them in document order). The
  discriminator a polymorphic read consumes is metadata and never fills a property that matches
  it case-insensitively (measured, DECIDED-23 b).
- **Numbers read from strings** (FR-048): applies to numeric properties, collection elements,
  dictionary values and numeric roots; never to enums, `Nullable<T>` given `"null"`, dictionary
  keys (already strings) or int tags.
- **Custom converter for kind `ref`**: context converters match by descriptor kind only, so such a
  converter bypasses polymorphic dispatch for every `ref` — documented, unchanged.
- **Null JSON root**: returns `null` for every reference-type root (object, polymorphic, collection,
  `string`), throws `JsonException` for value-type roots (FR-037).
- **Object cycles** (`Group(IReadOnlyList<Clip>) : Clip` containing itself): serialize throws
  `JsonException` at STJ's max depth (FR-016).
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
  `[JsonSourceGenerationOptions]` and emit it in the context's read options (`JsonReadOptions`,
  FR-048), which apply to every spec of that context.
- **FR-003**: The contract closure (#250) MUST include every type named by a `[JsonDerivedType]` of
  a type already in the closure, transitively, together with its transpilable base chain and the
  types reachable from its properties — mirroring STJ, which exposes a `JsonTypeInfo` for every
  registered derived type. Discovery stays deterministic (breadth-first, attribute order).
- **FR-004**: A polymorphic type's spec MUST carry a data-only `polymorphism` descriptor: the
  discriminator name **verbatim** (never passed through the naming policy), whether the C# base is
  abstract **or an interface** (both set `abstract: true`), and the derived entries in attribute
  order. Each entry has its tag (`string`, `number`, or absent), a **lazy** link
  (`() => this.<getter>`) to its spec and, for a `[PlainObject]` variant only, its write-side
  `test` (the synthesized guard, FR-019). A self-registration of the base is an entry whose link
  points at the base's own getter. Non-default options (`unknownDerivedType`,
  `ignoreUnrecognizedTags`) are emitted only when set. When the base has no runtime constructor (an
  interface or a `[PlainObject]` base), the descriptor also carries `typeName`, the C# simple name
  used in error messages; a concrete `[PlainObject]` base that does not register itself also
  carries its synthesized guard as `baseTest` (FR-019 rule 5, DECIDED-24 b). Specs of
  non-polymorphic types MUST NOT change.
- **FR-005**: Serializing through a polymorphic spec MUST resolve the value's runtime constructor
  **first against the registered entries** (`byCtor`, which includes the base itself when it is
  self-registered): a registered type → its tag (if any) **as the first JSON property**, then its
  contract; otherwise, the base itself → base contract, no tag; otherwise per
  `UnknownDerivedTypeHandling` (`FailSerialization` → `NotSupportedException`; `FallBackToBaseType`
  → base contract, no tag; `FallBackToNearestAncestor` → nearest registered ancestor in the
  prototype chain, abstract entries included, else the base contract). This constructor path
  applies to class instances. A plain object (prototype `Object.prototype` or `null`) is resolved
  by the `[PlainObject]` variant guards instead (FR-019). The dispatch MUST apply wherever the spec
  is used: the root, `ref`, and through `array`, `hashSet`, `map` and `nullable` descriptors.
- **FR-006**: Serializing through a non-polymorphic spec (including a derived type's own spec and an
  intermediate type of a polymorphic hierarchy) MUST NOT write a tag and MUST NOT re-dispatch. A
  derived type that is itself polymorphic is not re-dispatched when reached through an outer base
  either — this is STJ's behavior (research.md R1, nested polymorphism rows).
- **FR-007**: Deserializing through a polymorphic spec MUST: treat the tag as present only when the
  discriminator name matches **case-sensitively** (also when the context reads property names
  case-insensitively, FR-048) and — unless the context's `allowOutOfOrderMetadataProperties` read
  option is set — it is the first key; reject a present tag that is neither a JSON string nor a JSON number with
  `JsonException` "The '$id', '$ref' or '$type' metadata properties must be JSON strings. Current
  token type is '<Token>'.", where `<Token>` is `Null` for `null`, `True` / `False` for booleans,
  `StartObject` for an object and `StartArray` for an array; match string and number tags as
  distinct values (`1` ≠ `"1"`); on a known tag use that entry's contract — the base contract when
  the tag belongs to the base's self-registration (the tag key is otherwise ignored); on an unknown
  tag throw `JsonException` "Read unrecognized type discriminator id '<id>'." unless
  `ignoreUnrecognizedTags` is set, in which case a concrete base reads the base contract and an
  abstract base throws `NotSupportedException`; on a missing tag, an abstract base throws
  `NotSupportedException` "The JSON payload for polymorphic interface or abstract type '<Base>' must
  specify a type discriminator." and a concrete base reads the base contract. A tag present as a
  later key (the out-of-order option off) is a missing tag for an abstract or interface base, and
  on a concrete base throws `JsonException` "The metadata property is either not supported by the
  type or is not the first property in the deserialized JSON object."; `$`-prefixed data keys are
  rejected as in the Edge Cases (measured, DECIDED-21 h–j).
- **FR-008**: The runtime MUST export `JsonException` and `NotSupportedException` (both extend
  `Error`, `name` set to the class name). Their messages are STJ's text **without** the
  `Path: … | LineNumber: … | BytePositionInLine: …` suffix (DECIDED-5). The two pre-existing plain
  `Error`s of `deserializeWithSpec` ("Expected object for deserialization…" and "Missing required
  property…") MUST become `JsonException` in this feature (FR-045).
- **FR-009**: For **every** spec (polymorphic or not), serialization MUST write properties
  most-derived first: the spec's own properties, then its base's, recursively; the tag, when
  written, precedes all of them. A property declared again in a derived type (`override` or `new`)
  MUST appear **once**, at the most-derived declaration's position, with the most-derived
  declaration's type: the generator dedupes the emitted `PropertySpec` lists by final JSON name, and
  `collectWireProperties` dedupes by JSON name as a guard. Deserialization is key-order-insensitive
  except for the tag rule of FR-007, and reads each JSON key once. The exported `collectProperties`
  keeps its base-first contract; a new `collectWireProperties` exposes the wire order.
- **FR-010**: Transpilability of a base, a closure member and a derived-type target MUST be
  evaluated against the **declaring assembly** of that type (`type.ContainingAssembly`): a type is
  transpilable when it is `[Transpile]`, or when its declaring assembly carries
  `[assembly: TranspileAssembly]` and the type passes that assembly's opt-in rule. This fixes two
  bugs: today `TranspilableBaseOf` calls `IsTranspilable` without the assembly-wide arguments (so
  under `TranspileAssembly` inherited properties vanish from the wire), and passing the *current*
  assembly would still drop a base declared in a referenced `[TranspileAssembly]` +
  `[EmitPackage]` library (FR-047).
- **FR-011**: An abstract polymorphic base's `factory` stays a throwing factory (unreachable on the
  polymorphic path), with the message reworded to `Cannot deserialize abstract type 'Clip'
  directly; deserialize it through a polymorphic spec.` (the #238 text "polymorphic JSON
  deserialization is not supported yet" becomes false with this feature). A non-polymorphic
  abstract base keeps today's message.
- **FR-012**: The generator MUST report `MS0032` (`InvalidJsonPolymorphism`, Error) at the offending
  attribute when: a derived type is **not assignable to the base** (the base itself is allowed) or
  is not transpilable for the target; a tag or a derived type is registered twice on one base; a
  derived type is **abstract** and the base's `UnknownDerivedTypeHandling` is not
  `FallBackToNearestAncestor` (STJ throws `InvalidOperationException` "…cannot be abstract classes
  or interfaces unless 'JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor' is specified");
  a derived entry is itself an interface (it has neither a TS constructor nor a guard that STJ's
  nearest-ancestor rules could use; v1, OPEN-18); two `[PlainObject]` variants of one base cannot
  be told apart on write (FR-019); a derived type is generic (open or closed, v1 — a known parity
  gap: STJ accepts a closed generic registration such as `typeof(GenD<int>)` and writes
  `{"$type":"g","X":1}`, see Documented divergences); a derived type is not assignable to a
  constructed polymorphic base the context dispatches through (FR-015); a data property of a
  contract written with a tag (a tagged registration, inherited properties included) has a final
  JSON name (after `[JsonPropertyName]` / policy) equal to the discriminator (STJ throws
  `InvalidOperationException` "The type 'KBBD' contains property 'kind' that conflicts with an
  existing metadata property name."), or such a JSON property name is integer-like — untagged
  registrations, and hierarchies without any tag, never conflict (STJ writes them, measured);
  the base is **sealed** (STJ: "Specified type 'SealedB' does not support polymorphism.
  Polymorphic types cannot be structs, sealed types, generic types or System.Object."; a struct
  cannot carry the attributes, CS0592); `[JsonPolymorphic]` is present without any
  `[JsonDerivedType]` (STJ: "Polymorphic configuration for type 'PolyOnly' should specify at least
  one derived type."); the `TypeDiscriminatorPropertyName` is `$id`, `$ref` or `$values` (STJ:
  "The metadata property names '$id', '$ref', and '$values' are reserved and cannot be used as
  custom type discriminator property names."). The three base rules and the property rules leave
  the base without `polymorphism`; the registration rules leave out that registration only.
- **FR-013**: The generator MUST report `MS0033` (`LossyJsonPolymorphism`, Warning) when a derived
  type is registered without a tag (serializes without a tag and reads back as a concrete base, or
  cannot be read through an abstract base, exactly as in STJ), and when a derived type is itself
  polymorphic (informational: its own `[JsonDerivedType]`s apply only through its own spec, as in
  STJ). Slice S6 also reported an interface or `[PlainObject]` hierarchy that carries the
  attributes, emitted without a discriminator (DECIDED-22 b); S6c removed that warning.
- **FR-014**: The discriminator name and values MUST be emitted verbatim. Data property names keep
  today's rule (`[JsonPropertyName]`, else the context naming policy, else the C# name). The naming
  policy also comes from `JsonSerializerDefaults.Web` (camelCase) when the context passes it, and an
  explicit `PropertyNamingPolicy` wins over it (FR-048).
- **FR-015**: A **closed generic polymorphic base** (`GDer : GBase<int>` with
  `[JsonDerivedType(typeof(GDer), "g")]` on `GBase<T>`) MUST be supported (DECIDED-13): the attributes
  are read from the original definition; every constructed base the closure reaches gets its own
  spec named by FR-031 (`gBaseInt32`, `TypeSpec<GBase<number>>`); its `base` link, derived links
  and getter all use the constructed name. A constructed base is polymorphic — gets
  `polymorphism`, adds its registrations and is validated — only when the context dispatches
  through it: as a root, a `ref` target (a property, element or value type) or a registered type,
  never when it is reached only through a derived type's `base` link (STJ builds no polymorphism
  for such a base, DECIDED-22 j). For each such constructed base, `MS0032` reports every
  registration whose type is not assignable to that construction (STJ throws only when such a
  registration is reached through that closing, e.g. `GBase<string>`).
- **FR-016**: `serializeContract` and `deserializeContract` MUST track the JSON container depth and
  throw `JsonException` "A possible object cycle was detected. This can either be due to a cycle or
  if the object depth is larger than the maximum allowed depth of 64. Consider using
  ReferenceHandler.Preserve on JsonSerializerOptions to support cycles." on serialize when the
  depth exceeds 64, instead of overflowing the stack. The read side MUST enforce the same limit
  (DECIDED-15): `deserializeContract` counts container depth on the parsed value and throws
  `JsonException` "The maximum configured depth of 64 has been exceeded. Cannot read next JSON
  object." (`… JSON array.` for an array). Both boundaries were measured on .NET 10 in S4
  (DECIDED-20 b): 64 nested containers are written and read, the 65th throws; S8 pins them with a
  manifest row. Only containers count: the write side follows the source-generated fast path's
  boundary, while its exception and message are STJ's metadata / reflection mode's, which already
  throws for a non-null scalar written inside the 64th container (a hybrid, DECIDED-20 b). The
  read limit counts the containers the contract reads, whichever path built the tree:
  `deserializeText` (FR-048 item 7) applies it the same way whether `JSON.parse` or the
  runtime's tokenizer read the text. The tokenizer only bounds its own recursion, at 1 000
  levels, a third of what Node's default stack held (measured).
- **FR-017**: Under `FallBackToNearestAncestor`, an abstract derived entry MUST be accepted as a
  nearest-ancestor target on serialize (its tag, if any, is written, then the runtime value's
  nearest-registered-ancestor contract). Reading its tag MUST throw what STJ throws (probed in S6,
  DECIDED-12): `NotSupportedException` "Deserialization of interface or abstract types is not
  supported. Type 'AMid'." for `{"$type":"m","M":2,"A":1}` through `AB`, whose abstract `AMid` is
  registered as `"m"` — also on a concrete base and under `IgnoreUnrecognizedTypeDiscriminators`.
  An abstract entry that is itself polymorphic reports its own missing tag instead
  (`NotSupportedException` "The JSON payload for polymorphic interface or abstract type 'PMid'
  must specify a type discriminator."). Without the out-of-order option, `$id`, `$ref` or
  `$values` right after the tag throw the "metadata property … not the first property"
  `JsonException` first; with it, the key walk of DECIDED-21 j runs first. The generator marks the
  entry `abstract: true` (`DerivedTypeSpec.abstract`). S8 adds the manifest row.
- **FR-018**: An **interface base** MUST be supported. `[JsonDerivedType]` on a C# interface makes
  it polymorphic as for a class. Its spec has no `type` (the TS interface has no constructor), a
  throwing `factory` with the FR-011 message (`Cannot deserialize interface type 'IShape'
  directly; deserialize it through a polymorphic spec.`), and the interface's own properties
  (written under `FallBackToBaseType`, measured `{"area":3}` under Web). Its `polymorphism`
  descriptor sets `abstract: true` and `typeName`. Class-backed variants implementing the
  interface are dispatched by constructor (FR-005); they have no `base:` link to the interface
  spec, because a class does not inherit interface members in its prototype chain, and their own
  spec already lists every serialized member (measured `{"$type":"sq","side":2}`). Reading follows
  FR-007 unchanged; a missing tag names the interface (measured). A closed generic interface base
  follows FR-015. `[PlainObject]` variants of an interface base follow FR-019.
- **FR-019**: **`[PlainObject]` variants** MUST be supported, under a class, interface or
  `[PlainObject]` base. Reading is unchanged: the discriminator selects the entry, which is
  authoritative, and the entry's factory builds the object literal. Writing uses runtime type tests:
  1. **Guards are synthesized.** For every `[PlainObject]` variant reached by a polymorphic spec,
     the generator emits the variant's `is<Name>` guard in the variant's own file, built by the
     same guard builder `[GenerateGuard]` uses (`TypeGuardBuilder`), whether or not the type carries
     `[GenerateGuard]`. This matches the guard-synthesis decision for C# union cases (C# unions
     specification, PR #310). The derived entry references it as `test: is<Name>`, a value import
     from the variant's module (FR-041). A concrete `[PlainObject]` base that is in the dispatch
     set (self-registered, or the fallback for unmatched plain objects) gets its guard the same way.
  2. **Which values use guards.** Guards are evaluated only for plain objects (prototype
     `Object.prototype` or `null`). Class instances never reach a guard, and plain objects never
     match a class variant by constructor, so mixed hierarchies cannot misroute a value.
  3. **Order.** The generator computes, for each structural variant, its *required members*: the
     TS members the guard rejects when absent (not optional, not nullable), each with a coarse
     test kind (`string`, `number`, `boolean`, `array`, `object`, `present` for a presence-only
     check). Variant `A` *covers* variant `B` when every required member of `A` is a member of `B`
     with a compatible kind (equal, or either is `present`). If `A` covers `B` and `B` does not
     cover `A`, `B` is more specific and is tested first. Otherwise attribute order applies. The
     emitted `derived` array keeps attribute order; the runtime test order is a separate,
     generator-computed `testOrder` index list emitted only when it differs from attribute order.
  4. **Indistinguishable variants → `MS0032`.** Two variants that cover each other (for example the
     same required members with the same kinds, or two variants with no required member) cannot be
     told apart on write. The generator reports `MS0032` naming both and their required members. A
     cycle in the "more specific" relation is reported the same way. The base guard of rule 5
     counts too: when the base writes its own values through its guard (a concrete,
     non-self-registered `[PlainObject]` base), a variant whose guard covers the base would capture
     every base value, since the variant guards run first, and is reported (DECIDED-24 e). A
     registered `[PlainObject]` type that gets no guard (`[Import]`, `[ExportedAsModule]`,
     `[NoContainer]`, an exception or a type with extension members) is `MS0032` as well.
  5. **Unmatched plain objects.** A plain object that matches no variant guard is the base itself
     when the base is a concrete `[PlainObject]` type whose guard matches (base contract, its tag
     only when self-registered); otherwise it is an unregistered runtime type and follows
     `UnknownDerivedTypeHandling`, with `Object` as the runtime type name in the
     `NotSupportedException` message. Under `FallBackToNearestAncestor` the most specific matching
     guard already is the nearest ancestor, so the mode adds nothing for plain objects.

#### B. Validating branded factory (#269)

- **FR-020**: `Metano.Annotations` MUST provide `JsonFactoryAttribute`
  (`[AttributeUsage(AttributeTargets.Method)]`, sealed, no members).
- **FR-021**: When a `[Branded]` / `[InlineWrapper]` type declares exactly one valid
  `[JsonFactory]` method, every branded descriptor the generator emits for that type MUST be
  `{ kind: "branded", create: <TsTypeRef>.<tsMethodName> }`, where `<TsTypeRef>` is the qualified
  TS reference of the type (nested types included, `Episode.Title`) and `tsMethodName` is computed
  by **the same function the branded companion uses to declare the method**
  (`IrToTsNamingPolicy.ToFunctionName`, honoring `[Name]`), never by a parallel rule. Without
  `[JsonFactory]` the descriptor MUST stay byte-identical to today (`create: <TsType>.create`).
  `System.Guid` (`UUID.create`) is unchanged. This applies to branded map keys as well.
- **FR-022**: A `[JsonFactory]` method is valid when it is an ordinary static method (not an
  operator or conversion operator), emitted for the target (not `[Ignore]`, `[Emit]`, `[Import]`,
  `[Inline]`), is not generic, has exactly one parameter whose type is the wrapper's underlying
  primitive, returns the containing type, is the only `[JsonFactory]` of its type, has no
  same-named method and no other method of the type that the companion declares under the same TS
  name (for example a `[Name("from")]` rename), and has a TS name that differs from every member
  the companion synthesizes (`create`, `toString`). Any accessibility is valid, except in an
  `[EmitPackage]` library, where the method MUST be `public`: a consumer compiled against the
  library's metadata cannot see a non-public method, so its descriptor would silently fall back to
  `create`. Otherwise the generator MUST report `MS0034`
  (`InvalidJsonFactory`, Error) at the method and keep `create`.
- **FR-023**: `[JsonFactory]` on a method of a type that is not `[Branded]` / `[InlineWrapper]` MUST
  report `MS0034`.
- **FR-024**: Exceptions thrown by the factory MUST propagate unchanged from `deserialize` (no
  wrapping), at any nesting depth.
- **FR-025**: The runtime `BrandedDescriptor` contract and `brandedConverter` MUST stay unchanged;
  only the generated `create` reference changes.
- **FR-026**: The feature is TypeScript-only on the C# side: Metano ships no STJ converter that
  honors `[JsonFactory]` (STJ writes a branded record struct as an object such as
  `{"Value":"ok"}` unless the user supplies a converter). Branded manifest rows are therefore
  `sides: ["ts"]`, and the STJ-side conformance sample declares no branded member without a
  `JsonConverter`. A shipped converter is a separate future FR.

#### C. Non-object roots and STJ naming (#270)

- **FR-030**: `[JsonSerializable]` roots MUST accept named types **and** array types of rank 1. A
  root whose classification (the same classifier used for property types) is the object kind keeps
  the `TypeSpec` path; every other kind (`array`, `hashSet`, `map`, `primitive`, `decimal`,
  `temporal`, `branded`, `enum`, `numericEnum`, `nullable`) MUST emit a `ValueSpec` whose
  `descriptor` is exactly the descriptor that type gets as a property.
- **FR-031**: Every context member name MUST come from one naming function, used by roots, `ref`
  descriptors, `base` links, polymorphic derived links and closure deduplication: an explicit
  `TypeInfoPropertyName` wins (for that type everywhere in the context); a non-generic named type
  uses its resolved TS name (today's rule, honors `[Name]`); a closed generic type is its name
  without arity followed by the names of its type arguments (`ListEpisodeSummary`,
  `DictionaryStringEpisodeSummary`, `GBaseInt32`); an array is the element name + `Array`;
  `Nullable<T>` is `Nullable` + the name of `T`; BCL primitives and well-known structs use their
  CLR names (`String`, `Int32`, `Double`, `Guid`, `DateTime`, `Decimal`). The getter is the
  camelCase of the name; the backing field is `_` + getter.
- **FR-032**: `ValueSpec<T>` and `TypeSpec<T>` MUST be parameterized with the mapped TS type of the
  root (`EpisodeSummary[]`, `Map<string, EpisodeSummary>`, `HashSet<string>`, `Page<EpisodeSummary>`),
  never with a bare generic name; a reference-type value root adds `| null` (FR-037). A `TypeSpec`
  type argument never contains `| null`: the null of an object root is expressed by the
  `deserialize` return type instead (FR-034).
- **FR-033**: The contract closure MUST admit **closed generic object types** wherever they are
  reached — roots, properties (directly or through nullable / array / map / hash-set), `base` links
  and derived links — keyed by their FR-031 name, as well as the object types reachable from a
  non-object root (element, map value, nullable inner). `BuildRefDescriptor` and the getter name
  helper already route through the FR-031 name, so every emitted `ref` resolves to an emitted
  getter. A golden for `record Feed(Page<EpisodeSummary> Latest)` MUST compile under
  `tsgo --strict`.
- **FR-034**: The runtime MUST add `ValueSpec<T> { descriptor: TypeDescriptor }` and
  `JsonTypeInfo<T> = TypeSpec<T> | ValueSpec<T>`. `JsonSerializer` and `BoundSerializer` MUST
  expose these overloads (FR-037, **breaking**):
  - `serialize(value: T | null, spec: TypeSpec<T>): Record<string, unknown> | null` (was
    `(value: T, …): Record<string, unknown>`);
  - `serialize(value: T, spec: ValueSpec<T>): unknown` (new);
  - `deserialize(data: unknown, spec: TypeSpec<T>): T | null` (was `T`);
  - `deserialize(data: unknown, spec: ValueSpec<T>): T` (new; `T` already contains `null` for a
    reference-type value root).

  A `ValueSpec` is converted through the same converter resolution as a property, so context
  converters apply by kind and polymorphic elements dispatch per FR-005/FR-007. `specFor` and the
  constructor registry are unchanged. Generated contexts create value specs through a new
  protected `createValueSpec` so that the context's read options (FR-048) reach them.
- **FR-035**: The generator MUST report `MS0035` (`InvalidJsonSerializableRoot`, Error) at the
  attribute for a root that is an open generic or type parameter, an error type, `object` /
  `dynamic`, an interface that is not a recognized collection/dictionary/set, an **object-kind type
  that is not a transpilable class/struct** (BCL types such as `KeyValuePair<,>`, `ValueTuple`,
  `[NoTranspile]` / `[Ignore]` / `[External]` types, types from assemblies that are neither the
  current one nor a Metano package), or an **array of rank > 1** (`int[,]`; STJ does not support
  it) — instead of dropping it or emitting an invalid spec.
- **FR-036**: The generator MUST report `MS0036` (`JsonTypeInfoNameCollision`) when two distinct
  types map to the same member name, or when a member name (getter or `_` field) equals a
  `SerializerContext` instance member (`options`, `converters`, `createSpec`, `createValueSpec`,
  `readOptions`, `resolve`, `_registry`, `constructor`). Severity: **Error** when the losing type is the target of any `ref`,
  `base` or derived link (otherwise every such link would silently serialize the winner's
  contract), and always for a reserved-member collision; **Warning** only when the losing type is
  an unreferenced root, which is then not emitted (mirrors STJ `SYSLIB1031`). The first type in
  discovery order keeps the name (DECIDED-14).
- **FR-037**: Null at a root MUST behave as in STJ (DECIDED-6, measured in research.md R1 and R8):
  - **Reference-type roots** (classes and records, polymorphic and interface bases included,
    `[PlainObject]` types, collections, dictionaries, sets, `string`): JSON `null` deserializes to
    `null`, and a `null` value serializes to JSON `null`. `deserialize` through a `TypeSpec<T>` is
    therefore typed `T | null` (FR-034), and a reference-type `ValueSpec` root is typed
    `ValueSpec<T | null>`.
  - **Value-type roots** (C# `struct` / `record struct` object contracts, `int`, `Guid`, enums,
    branded types, `decimal`, temporal structs): JSON `null` throws `JsonException`
    `The JSON value could not be converted to <Type>.` (measured for `SPoint`; the `Int32` text is
    pinned by manifest row E10 in S8). Their
    spec carries `valueType: true` (both `TypeSpec` and `ValueSpec`); the generator emits it for
    every C# value type except `Nullable<T>`, which already accepts `null` through its `nullable`
    descriptor. A struct `TypeSpec` root is still typed `T | null` by the shared overload; the
    runtime check makes the null impossible in practice (documented in `docs/serialization.md`).
  - This is a breaking change of the public TS API and of generated call sites' types (FR-049).
- **FR-038**: `arrayConverter` (and the hash-set path) MUST serialize through
  `Array.from(value as Iterable<unknown>, (x) => elemConv.serialize(x))`, so `Iterable<T>` values
  produced for `IEnumerable<T>`, `IReadOnlyCollection<T>` and `IQueryable<T>` roots and properties
  (a `Set`, a generator, a LINQ pipe) serialize as JSON arrays instead of throwing `TypeError`.

#### D. Cross-cutting

- **FR-040**: Contexts that use none of the new attributes and only object roots MUST produce
  byte-identical generated TypeScript, except where FR-010 adds a previously missing `base:` link,
  FR-033 / FR-047 add the getters (and their imports and `package.json` dependency) of closed
  generic and referenced-package types that were previously dangling `ref` / inlined bases,
  FR-009 removes a duplicated overridden property, FR-037 adds `valueType: true` to struct
  contracts, FR-048 changes contexts declared with `JsonSerializerDefaults.Web` (camelCase
  names, read options, number hints), and every `decimal` descriptor names its type
  (`{ kind: "decimal", type: Decimal }`, DECIDED-23 f). The types of call sites change with FR-034 (breaking,
  FR-049).
- **FR-041**: The runtime additions MUST be additive and exported from
  `metano-runtime/system/json` (`ValueSpec`, `JsonTypeInfo`, `PolymorphismSpec`, `DerivedTypeSpec`,
  `TypeDiscriminator`, `UnknownDerivedTypeHandling`, `JsonReadOptions`, `NumberTypeName`,
  `JsonException`, `NotSupportedException`, `collectWireProperties`). Apart from the FR-034
  signature changes, they are additive. The generated context adds **no new runtime value
  import**. Its only new value imports are the synthesized guards of `[PlainObject]` variants
  (`isCircle` from the variant's module, FR-019), resolved like the variant's own import (local or
  through `OriginResolver` for a library variant). Types that appear
  only inside `ValueSpec<…>` / `TypeSpec<…>` type arguments (`HashSet`, `Temporal`, `Decimal`,
  `UUID`, cross-package element types) are imported **type-only** (`import type` or a per-name
  `type` qualifier), and those imports MUST be produced by the context transformer itself, since
  the context bypasses the IR runtime-requirement scanner.
- **FR-042**: The Dart target is out of scope (it has no JSON context; roadmap RT-02).
- **FR-043**: Documentation MUST be updated: `docs/serialization.md` (polymorphism, roots, factory,
  wire order, error classes, interface and `[PlainObject]` hierarchies, Web defaults, null roots,
  the documented divergences, the minimum `metano-runtime` version, and the two false claims about
  base properties in factories and `new Decimal`), the attribute catalog (`[JsonFactory]`), the
  diagnostic catalog (move `MS0032`–`MS0037` from "Reserved codes" to the stable table), the
  feature-support matrix Serialization rows, and the changelog / release notes (breaking change
  and migration note of FR-049, minimum runtime version, DECIDED-7 renames).
- **FR-044**: Tests MUST cover every scenario of US1–US5 and every diagnostic rule, with at least
  one golden file per area (see plan.md "Test strategy") and the shared conformance manifest checked
  on both the .NET and Bun sides in CI.
- **FR-045**: Every error thrown by `deserializeWithSpec` / `deserializeContract` for malformed
  input MUST be a `JsonException`: non-object input for an object contract ("Expected object…" →
  STJ-style `The JSON value could not be converted to <Type>.`) and a missing non-optional property.
  The **semantics** of a missing property stay as today (Metano throws; STJ uses the default value
  unless the member is `required`) and are listed as a documented divergence.
- **FR-046**: The generator MUST report `MS0037` (`UnmirroredJsonOption`, Warning) at the context
  when its `[JsonSourceGenerationOptions]` sets an option that changes the wire and that the
  generator does not mirror: `DefaultIgnoreCondition`, `IgnoreReadOnlyProperties`,
  `IgnoreReadOnlyFields`, `IncludeFields`, `NumberHandling` with any flag other than
  `AllowReadingFromString` (`WriteAsString`, `AllowNamedFloatingPointLiterals`),
  `UseStringEnumConverter`, `Converters`, `ReferenceHandler`, `DictionaryKeyPolicy`,
  `ReadCommentHandling`, `AllowTrailingCommas`, `UnmappedMemberHandling`, `RespectNullableAnnotations`,
  `RespectRequiredConstructorParameters`. Formatting-only options (`WriteIndented`, `Encoder`) are
  not reported: `JSON.stringify` formatting belongs to the caller. It MUST also report `MS0037` at
  the member for a `[JsonInclude]` field / non-public property (not collected), for a `byte[]` root
  or property (STJ writes base64; Metano writes a number array), and for a contract member with
  `[JsonPropertyOrder]` (STJ reorders the properties, measured `{"b":2,"a":1}`; the generated
  context keeps declaration order, DECIDED-3). Mirrored options: `PropertyNamingPolicy`,
  `PropertyNameCaseInsensitive`, `NumberHandling = AllowReadingFromString`, the
  `JsonSerializerDefaults.Web` constructor argument that sets those three (FR-048), and
  `AllowOutOfOrderMetadataProperties` (FR-002).
- **FR-047**: Cross-package contracts (DECIDED-10): the closure MUST admit object types declared in a
  referenced `[TranspileAssembly]` + `[EmitPackage]` library (FR-010 rule), with imports resolved
  through the existing `OriginResolver` path and the package added to `package.json#dependencies`
  as for any cross-package import. This covers: a polymorphic base and its variants declared in the
  library with the context in the consumer; a consumer type deriving from a library base (base link
  and inherited properties present); a consumer property typed with a library record. A
  `[JsonDerivedType]` target that is not importable (not transpilable in its declaring assembly)
  is `MS0032`.
- **FR-048**: **`JsonSerializerDefaults.Web` parity** (DECIDED-4). The measured Web defaults on
  .NET 10 are exactly `PropertyNamingPolicy = CamelCase`, `PropertyNameCaseInsensitive = true` and
  `NumberHandling = AllowReadingFromString` (research.md R8 enumerated every public option). The
  generator MUST read the `JsonSerializerDefaults` constructor argument of
  `[JsonSourceGenerationOptions]` and the three named options; a named option overrides the Web
  default. Then:
  1. **Names**: camelCase names are computed at transpile time, as for an explicit
     `PropertyNamingPolicy` (the discriminator stays verbatim, FR-014). Shipped as its own `fix:`
     slice (S2).
  2. **Read options**: the context emits non-default read options once, as a protected
     `readOptions` getter override returning a `JsonReadOptions` literal
     (`propertyNameCaseInsensitive`, `numberHandling: "allowReadingFromString"`,
     `allowOutOfOrderMetadataProperties`). `createSpec` and `createValueSpec` attach them to every
     spec of the context, so the static `JsonSerializer` sees them too. A context with default
     options emits no override and stays byte-identical.
  3. **Case-insensitive property matching**: `deserializeContract` matches each JSON key to a
     property by comparing both names as STJ's `OrdinalIgnoreCase` does: each code point's simple
     uppercase mapping, which is `toUpperCase()` where it yields one code point, plus .NET's
     exceptions (`ı`, `ſ` and the Greek letters with ypogegrammeni; DECIDED-23 a); keys are
     applied in the parsed document order, so the last match wins (measured). The discriminator is
     always matched case-sensitively (measured).
  4. **Numbers read from strings**: when the option is set, the generator adds
     `numberType: "<CLR name>"` (for example `"System.Int32"`, `"System.Double"`) to every
     `primitive` descriptor of a C# numeric type, and the `decimal` converter reads strings too. A
     JSON string is accepted by STJ's measured grammar: signed integer types accept `[+-]?[0-9]+`
     and unsigned ones `[0-9]+` only (`"+5"` and `"-0"` are rejected), leading zeros allowed,
     within the type's range; `Single` / `Double` accept an optional sign, digits with an optional
     `.` (`"1."`, `".5"`, `"01.5"`) and an optional exponent, and the exact texts `NaN`,
     `Infinity` and `-Infinity`, rejecting a value that overflows the type; `Decimal` accepts the
     same text without the named literals, within its range after rounding; no whitespace, hex,
     empty string or other spelling. A rejected string throws `JsonException`
     `The JSON value could not be converted to <CLR name>.`, ``System.Nullable`1[<CLR name>]`` for a
     nullable number (measured). The option applies to properties, collection elements,
     dictionary values and roots (measured), and never to enums, `Nullable<T>` given `"null"`,
     dictionary keys or int discriminators (measured). The full grammar table is in research.md R8
     (S2b probe).
  5. Writing is unaffected: `AllowReadingFromString` changes reads only (measured: numbers are
     still written as JSON numbers).
  6. **Decimal writes** (#458, DECIDED-23 f): under any options, the `decimal` converter writes a
     decimal.js-like value (one with `toFixed`) as `JSON.rawJSON(value.toFixed())`, so
     `JSON.stringify` prints the exact digits in plain notation, as STJ does (measured on .NET
     10.0.12: `decimal.MaxValue` → `79228162514264337593543950335`, `0.0000001m` →
     `0.0000001`, `decimal.Negate(0m)` → `0`). The tree `serialize` returns therefore holds a
     `rawJSON` leaf, not a `number`, for each decimal (breaking, see
     [Migration](#migration-breaking-changes)). The scale is not kept: decimal.js holds `1.10m` as
     `1.1` (documented divergence). The engine's `JSON.rawJSON` is detected once when the runtime
     loads; without it the converter writes `toNumber()`, as before. A value without `toFixed`
     is written with `toNumber()`, and a raw JSON number or string read by a descriptor without
     `type` is written back as is. `deserialize` reads a `rawJSON` leaf from its exact text, so a
     tree passed from `serialize` straight to `deserialize` round-trips. The writer is shared
     (`exact-json-number.ts`), so `long` / `ulong` as `bigint` (#450) write their digits the same
     way. **Domain check** (on both paths, before any text is built): a value no `System.Decimal`
     holds (NaN, ±Infinity, a magnitude beyond `MaxValue`) throws `JsonException` "The value
     <value> cannot be written as System.Decimal: it is outside the System.Decimal range."
     (FR-045). The check reads decimal.js's exponent `e` (`NaN` for NaN and ±Infinity), which
     converts no digits: above 28 is outside, and exactly 28 (`1e28` up to `MaxValue` and past it)
     is decided by the exact `toFixed()` text with the read grammar of item 4, so every leaf
     written is one `deserialize` accepts (`…335.4` is written, STJ reads it as `MaxValue`;
     `…335.5` and `…336` throw). A value without `e` is compared as a double (`|toNumber()|`
     against 2^96, the text deciding at 2^96 itself). Without the check, decimal.js NaN /
     Infinity threw a bare `SyntaxError` from `JSON.rawJSON` (`null` without it), `1e30` was
     written in full, and `1e400000000` built a 400 MB string. The error prints the value in plain
     notation (`100000000000000000000000000000` for `1e29`) unless its exponent exceeds 64. A value
     below `1e-29` (exponent under -29, or a double under `1e-29` for a value without `e`) is
     written `0` on every path, with or without `JSON.rawJSON`, as STJ reads `1e-29`, `1e-40` and `1e-400` as `0` (measured),
     instead of printing its exponent's worth of zeros (`1e-400000000`).
     **Cost** (`bun run bench:json`, 1 MB `Order[]` with decimal.js prices, medians): writes take
     5.5–5.7 ms against 4.3 ms with `toNumber()` on Bun 1.4.2, and 8.8–9.1 ms against 8.8–9.1 ms
     on Node 24.11.
  7. **Decimal reads** (#457, DECIDED-23 f): `JsonSerializer.deserializeText(text, spec,
     converters?)` and `BoundSerializer.deserializeText(text, spec)` read JSON text as STJ's
     `Deserialize(string, JsonTypeInfo<T>)` does, and build each `decimal` from its source
     digits. `deserialize(data, spec)` keeps reading the tree `JSON.parse` built, where a number
     with more than 15 significant digits has already been rounded to a double (documented
     divergence).
     - **Path per spec.** On its first text read, the closure of the spec (properties and bases,
       derived contracts, collection elements, dictionary values, nullables, refs) is searched
       once for a descriptor whose built-in converter reads a number from its exact text: a
       `decimal` (`long` / `ulong` as `bigint` join it with #450). A spec without one reads
       `JSON.parse(text)`, exactly as before. A spec with one reads `JSON.parse(text)` too when no
       run of `[0-9.]` in the text is 16 characters long, since every number then has at most 15
       significant digits, which a double holds exactly (the check probes every 16th character:
       about 5 ms on Node and 4 ms on Bun for 10 MB). A digit run inside a string
       (`"card 4111111111111111"`) can send a text without such a number to the tokenizer, which
       then builds the tree `JSON.parse` would: only the time differs. Otherwise the runtime's
       tokenizer builds the
       tree and keeps as its source text each number whose mantissa has more than 15 digits past
       a zero integer part (leading fraction zeros count, since the decimal rounding below
       depends on them; the 16-character run covers them too). Only
       the built-in `decimal` converter reads that text; every other converter, custom ones
       included, gets the `number` `JSON.parse` gives, so a `double` or `long` property reads as
       before. A custom converter gets its whole subtree that way (a custom `array`, `map`,
       `hashSet`, `ref` or `nullable` converter reads the elements and members itself): the
       containers that hold a leaf are copied with the numbers in place. A tokenized string is copied out of the text, so the tree never keeps the text
       alive (on JavaScriptCore a slice of any length does).
     - **Decimal rounding.** The `decimal` converter reads every number text (a long number, a
       `rawJSON` leaf, a JSON string under `AllowReadingFromString`) as STJ does: a 96-bit integer
       and a scale of at most 28. Past them, the digits round half to even, except a tie (a 5
       followed only by zeros) at the 30th digit of the text or later, which rounds up; digits
       count from the first non-zero integer digit, or, when the integer part is zero, from the
       first fraction digit, so leading fraction zeros count
       (`0.0000000000000000000000000000025e2` reads as `3e-28`). `1e-29` and smaller read as
       `0`; a value that
       rounds past `MaxValue` throws `The JSON value could not be converted to System.Decimal.`.
       A JSON number already read as a double is rounded the same way from its shortest text when
       it is non-zero and below `1e-11` (before #457, `1e-29` read as `1e-29`). Measured on .NET
       10.0.12: the rows of DECIDED-23 f, about 20 000 ties, 30 000 random texts (5 000 of them as JSON strings under
       Web) and the 20 019 values of the #458 probe all agree on Node 24.11 and Bun 1.4.2.
     - **Text errors.** Invalid JSON text throws `JsonException` with STJ's reader message,
       without the `Path` suffix (DECIDED-5), on every path: when `JSON.parse` throws its
       `SyntaxError`, the tokenizer reads the text again for the message (222 measured
       messages, e.g. `'}' is an invalid start of a value.`, `Expected depth to be zero at the end
       of the JSON payload. There is an open JSON object or array that should be closed.`, and
       non-ASCII characters printed as their first UTF-8 byte, `'0xC3'`). The tokenizer accepts
       exactly what `JSON.parse` accepts and builds the same tree: the last duplicate key wins at
       the first key's position, `__proto__` is an own property, and lone surrogates are
       accepted, which STJ rejects (documented divergence). The text is read whole before the
       contract, so a syntax error is reported before any contract error, where STJ reports the
       first in document order. A `/` right after `[`, `{` or `:` reports STJ's comment message,
       `'/' is invalid after a value. Expected either ',', '}', or ']'.` (measured).
     - **Cost** (`bun run bench:json`, 10 MB `Order[]`, medians, Node 24.11 / Bun 1.4.2): a spec
       without decimals reads in the same time through `deserializeText` as through
       `JSON.parse` + `deserialize` (76.0 / 54.8 ms against 79.9 / 54.0 ms). The `Order` spec with
       decimal prices of two decimal places reads in 123.5 / 83.9 ms against 114.7 / 78.1 ms, the
       cost of the check. With prices of 20 significant digits or more, the exact read takes
       237.9 / 139.4 ms against 124.0 / 84.1 ms for the inexact tree path. Peak memory of one
       10 MB read is unchanged for the two-decimal prices (266 / 161 MB) and 316 / 220 MB for the
       exact read. The alternative, a `JSON.parse` reviver keeping `context.source`, took
       344 / 192 ms and peaked at 373 / 259 MB on the same payload, and still needed the tokenizer
       for STJ's messages.
- **FR-049**: **Breaking change and versioning** (DECIDED-6). FR-034 changes public signatures of
  `metano-runtime/system/json`, so the feature MUST ship as a breaking release: the commit that
  changes the signatures uses `feat!:` with a `BREAKING CHANGE:` footer. `.releaserc.json` maps a
  breaking commit to a `major` release, and one `v${version}` tag versions both the NuGet tools and
  `metano-runtime`, so the whole release moves to the next major version. The maintainer may hold this
  slice so that one major also carries the breaking changes of the C# unions specification (PR
  #310). The release notes MUST contain the migration note of
  [Migration](#migration-breaking-changes).

### Diagnostics (reserved codes, DECIDED-2)

This spec owns `MS0032`–`MS0037`. The C# unions specification (PR #310) owns `MS0038`–`MS0043`.
This spec lists its range under "Reserved codes" in the baseline
[diagnostic catalog](../001-project-baseline-evolution/baseline/diagnostic-catalog.md).

| Code | Name | Severity | Raised for |
|---|---|---|---|
| `MS0032` | `InvalidJsonPolymorphism` | Error | FR-012, FR-015, FR-019, FR-047 rules |
| `MS0033` | `LossyJsonPolymorphism` | Warning | FR-013 rules |
| `MS0034` | `InvalidJsonFactory` | Error | FR-022, FR-023 |
| `MS0035` | `InvalidJsonSerializableRoot` | Error | FR-035 |
| `MS0036` | `JsonTypeInfoNameCollision` | Error / Warning | FR-036 |
| `MS0037` | `UnmirroredJsonOption` | Warning | FR-046 (including `[JsonPropertyOrder]`) |

### Documented divergences

Each item is listed in `docs/serialization.md` and, where a fixture exists, marked in the manifest
(`sides` restricted, or `divergent` with both texts recorded).

| Area | STJ | Metano | Status |
|---|---|---|---|
| Duplicate tag keys `{"kind":"a","kind":"b"}` | `JsonException` | `JSON.parse` keeps the last value | Undetectable after `JSON.parse` |
| Duplicate exact data keys around a case variant, case-insensitive context `{"count":1,"COUNT":2,"count":3}` | the last key in the text wins (`3`) | `JSON.parse` keeps the last value at the first key's position, so `COUNT` comes last and wins (`2`) | Undetectable after `JSON.parse` (DECIDED-23 a) |
| Int tag written `1.0` / `1e0` | `JsonException` "could not be converted" | `JSON.parse` yields `1`, accepted | Undetectable after `JSON.parse` |
| Number text `1E+21`, `-0` | as shown | `1e+21`, `0` | Fixtures avoid them |
| Writing `NaN` / `±Infinity` without `AllowNamedFloatingPointLiterals` | `ArgumentException` (measured) | `JSON.stringify` writes `null` | Pre-existing; reachable now that Web contexts read `"NaN"` |
| Integer-like property names in a polymorphic hierarchy | allowed | JS key order puts them first | Rejected, `MS0032` |
| Array-index payload key (`"0"`) written **before** the tag | `JsonException` "The metadata property … not the first property …" (measured) | the tag is read as first (JS enumerates index keys first, so their position is lost) | Undetectable after `JSON.parse`; an index key after the tag reads as in STJ (DECIDED-21 i) |
| `byte[]` | base64 string | number array | `MS0037`; follow-up issue (DECIDED-16) |
| Writing a `decimal` with trailing zeros (its scale) | `1.10m` → `1.10`, `1.0m` → `1.0`, `0.00m` → `0.00` | `1.1`, `1`, `0` (decimal.js normalizes trailing zeros; every other digit is exact, FR-048 item 6) | Documented (DECIDED-23 f, #458); keeping it needs a decimal type that keeps the scale |
| Writing a `decimal` on an engine without `JSON.rawJSON` | its exact digits | `toNumber()` (`7.922816251426434e+28` for `MaxValue`) | Engine-dependent (FR-048 item 6); Node 24 and Bun 1.4.2 have it |
| JSON number just past `decimal.MaxValue` (`79228162514264337593543950335.5`, `…336`) | `JsonException` | `deserializeText` throws the same; through `deserialize`, `JSON.parse` rounds it to the double of `MaxValue`, accepted | Undetectable after `JSON.parse`; exact through `deserializeText` (DECIDED-23 f, #457) |
| Reading a `decimal` of more than 15 significant digits (`12345678901234567890.123456789`) | its exact digits, rounded to 28 decimal places | exact through `deserializeText`; `JSON.parse` rounds it to a double before `deserialize` sees it (`12345678901234567000`) | Documented (FR-048 item 7, #457) |
| Lone surrogates in JSON text (`"\ud800"` unescaped, or `"\uD800"` read as a string) | `ArgumentException` / `JsonException` | accepted, as `JSON.parse` accepts them | Documented (FR-048 item 7) |
| A syntax error after a contract error in the text | the first error in document order | the syntax error: the text is read whole before the contract | Documented (FR-048 item 7) |
| A container nested past 64 levels in a member no contract reads | `JsonException` (the reader's limit) | accepted on every path: only the containers the contract reads count | Documented (FR-016) |
| A text nested past 1 000 levels, read by `deserializeText`'s tokenizer | read up to the limit of 64 | `JsonException` "The JSON text nests containers deeper than 1000 levels, …" (`JSON.parse` reads it) | Documented (FR-016, FR-048 item 7) |
| An invalid text that ends inside more than 64 open containers | the depth-limit message | the open-container message (`Expected depth to be zero …`) | Documented (FR-048 item 7) |
| Numeric string rejected for a constructor-bound property (Web) | `The JSON value could not be converted to <Namespace.Type>.` (declaring type) | names the numeric type (`System.Int32`) | Documented (DECIDED-23 c); manifest rows compare the prefix |
| Two properties whose JSON names differ only in case, case-insensitive context | `InvalidOperationException` "The JSON property name for 'Twins.A' collides with another property." at first use | both kept; a key fills the property listed last | Not reported; follow-up with the naming-collision issue (DECIDED-23 e) |
| Case-insensitive names with characters whose casing differs between the JS engine's and .NET's Unicode version | .NET's tables | the engine's tables (Node 24: U+A7CF, U+A7D3, U+A7D5) | Engine-dependent (DECIDED-23 a) |
| `TimeSpan` | `"01:02:03"` | `Temporal.Duration` string `PT1H2M3S` | Manifest row `divergent`; follow-up issue (DECIDED-16) |
| `DateTimeOffset` offset text | `+00:00` | per temporal converter | Manifest row; `divergent` if the bytes differ (DECIDED-16) |
| `Dictionary<int, …>` key order | insertion order | JS enumerates integer-like keys first | Manifest row `divergent` |
| `Stack<T>` root/property | written top-first, read back reversed (STJ pushes in order) | written and read in array order | DECIDED-18 (d); no code |
| `[Name]` inside a generic member name | `ListUserDto` (CLR name) | `ListUser` (TS name) | DECIDED-18 (a) |
| `[StringEnum]` root/property | number unless `JsonStringEnumConverter` | string | Pre-existing type-mapping contract, unchanged |
| `[JsonPropertyOrder]` | reorders properties | declaration order | `MS0037` (DECIDED-3) |
| Missing non-required property | default value | `JsonException` | FR-045 |
| Closed generic registration `[JsonDerivedType(typeof(GenD<int>), "g")]` | accepted (`{"$type":"g","X":1}`, measured) | `MS0032` | Known gap of v1 (FR-012) |
| Unregistered C# subtype of a `[PlainObject]` hierarchy whose shape satisfies a registered variant's guard | `NotSupportedException` (knows the runtime type) | written as the matching variant | FR-019; plain objects carry no runtime type |
| Unregistered C# subtype of a concrete `[PlainObject]` base whose shape passes the base's `baseTest` | `NotSupportedException` (measured: `Cat4` through `Animal4`) | written as the base (`{"Name":"c"}`) | FR-019 rule 5, DECIDED-24 b |
| Runtime type name of a plain object in `Runtime type '…' is not supported` | the C# type name | `Object` | FR-019 |
| Case-insensitive matching of non-ASCII names | `OrdinalIgnoreCase` | `toUpperCase()` | Not measured; fixtures use ASCII names; probed in S8 |
| Read depth limit | the reader counts every container of the document | counts only the containers the contract visits (a deep value under an undeclared key is skipped) | DECIDED-20 (b) |
| Write depth limit | fast path: 64 containers, `InvalidOperationException` "CurrentDepth (64) is equal to or larger than …" at the 65th; metadata / reflection: `JsonException` "A possible object cycle …", also for a non-null scalar inside the 64th container | 64 containers (scalars never count), `JsonException` "A possible object cycle …" at the 65th: the fast path's boundary with the metadata mode's error | DECIDED-20 (b) |
| Depth inside a custom converter | `JsonSerializer.Serialize(…)` of a separate root inside a converter starts a fresh writer at depth 0 | the root inherits the outer depth (one process-global counter) | DECIDED-20 (b) |
| Non-object / non-array input type name | the CLR type (`System.Collections.Generic.List`1[Arr]`) | the constructor name; `Object` for a `[PlainObject]` contract; `Expected array for deserialization, got <kind>` for arrays and sets, `Expected object for deserialization, got <kind>` for dictionaries | DECIDED-20 (c) |
| Unknown numeric enum value | read as is (`7` reads as `(Color)7`) | `JsonException` "Invalid numeric enum value: …" (pre-existing) | DECIDED-20 (c) |
| Unknown string enum value | `JsonException` "The JSON value could not be converted to Color." | `JsonException` "Invalid enum value: …" (pre-existing message) | DECIDED-20 (c) |
| Constructor parameter bound to an ignored property | `default(T)` | `undefined` (the factory reads a key the payload never carries) | DECIDED-20 (a) |
| `[JsonIgnore] new` (non-override) redeclaration | writes the base declaration from the base's own storage, at the redeclaration's position (measured: `{"Name":"base","X":2}` when the derived type declares the ignored `Name` before `X`) | writes the base declaration from the single TS member, which holds the derived value, at the base's position (`{"X":2,"Name":"derived"}`) | DECIDED-20 (a) |
| Error messages | with a `Path`, `LineNumber`, `BytePositionInLine` suffix | without | DECIDED-5 |

### Key Entities

- **Polymorphism descriptor** — data on a base `TypeSpec`: discriminator, abstract flag (also set
  for interface bases), derived entries, unknown-type handling, ignore flag, optional test order and
  the C# type name of a base without a constructor.
- **Derived entry** — one `[JsonDerivedType]`: optional tag (`string | number`), a thunk to the
  registered type's spec (its `type` is read from that spec, never duplicated) and, for a
  `[PlainObject]` variant, the synthesized guard used on write; may point at the base itself.
- **Read options** — the context-level reading behavior mirrored from
  `[JsonSourceGenerationOptions]`: case-insensitive names, numbers from strings, out-of-order
  metadata.
- **Value spec** — a root that is not an object: a single `TypeDescriptor`.
- **JSON type info name** — the STJ-compatible member name of a context entry, produced by one
  function.
- **JSON factory** — the one static method of a branded type that deserialization calls.
- **Conformance case** — one manifest row: id, context, getter, direction, sides, expected class,
  JSON text or error.

## Migration (breaking changes)

The release that ships FR-034 / FR-037 is a major version (FR-049). Its release notes MUST carry
this note:

> **`deserialize` through a `TypeSpec` now returns `T | null`.** The JSON text `null` used to
> throw; it now returns `null`, as `System.Text.Json` does for reference types. Code such as
> `const order: Order = JsonSerializer.deserialize(data, ctx.order);` no longer compiles under
> `strict`. Handle the null (`if (order === null) …`), or assert it when the payload can never be
> `null` (`JsonSerializer.deserialize(data, ctx.order)!`). `serialize` through a `TypeSpec` now
> accepts `null` and returns `null` for it. `BoundSerializer` changes the same way. Struct
> contracts (`record struct`) still throw `JsonException` for `null`. Value roots
> (`ValueSpec`) are new in this release and already carry `| null` in their type argument.

> **Decimals are written with their exact digits (#458).** `JsonSerializer.serialize` now returns
> a `JSON.rawJSON` leaf instead of a `number` for each `decimal` value, so `JSON.stringify` writes
> every digit, as `System.Text.Json` does (`79228162514264337593543950335`, not
> `7.922816251426434e+28`). Code that passes the tree to `JSON.stringify`, or back to
> `JsonSerializer.deserialize`, works unchanged. Code that inspects the tree and expects a
> `number` (`typeof json.price === "number"`, arithmetic on it) sees a frozen object whose
> `rawJSON` property holds the text: use `JSON.isRawJSON(leaf)` and `Number(leaf.rawJSON)` (lossy)
> or `new Decimal(leaf.rawJSON)` (exact), or register a custom `decimal` converter. Only
> `JSON.stringify` treats the leaf as a number: `structuredClone` and `postMessage` throw
> `DataCloneError` on a tree that holds one, and `{ ...leaf }` is `{"rawJSON":"2.5"}` (measured
> on Node 24 and Bun 1.4.2); stringify the tree before cloning or posting it. Trailing zeros
> are still dropped (`1.10m` → `1.10` in .NET, `1.1` here). Engines without `JSON.rawJSON` keep
> writing `toNumber()`.

The same release notes also list these changes, which do not break semantic JSON consumers: the
derived-first key order (FR-009), the `JsonException` class of the existing errors (FR-045), the
generic root getter renames (DECIDED-7), camelCase names and lenient reads for Web contexts
(FR-048), `deserializeText` with exact decimal reads and decimals rounded to STJ's 28 decimal
places on read (FR-048 item 7), and the minimum `metano-runtime` version (DECIDED-17).

## Decisions

The maintainer settled the open questions of the first draft. Each `DECIDED-N` keeps the number of
the former `OPEN-N`.

- **DECIDED-1 — Directory number.** `specs/009-polymorphic-json` (`008` is
  `008-exports-subpath-root`). The branch keeps its name, `spec/008-polymorphic-json`.
- **DECIDED-2 — Diagnostic numbers.** This spec owns `MS0032`–`MS0037`; the C# unions
  specification (PR #310) owns `MS0038`–`MS0043`. `MS0031` is the last code on `main`. Each
  implementation slice re-checks the numbers before it merges.
- **DECIDED-3 — Option coverage in v1.** All of: int and string tags, the three
  `UnknownDerivedTypeHandling` modes, `IgnoreUnrecognizedTypeDiscriminators` and
  `AllowOutOfOrderMetadataProperties`. `[JsonPropertyOrder]` is out of scope (it would need a
  `PropertySpec.order`) and is reported with `MS0037` on the member (FR-046).
- **DECIDED-4 — `JsonSerializerDefaults.Web`.** Full parity: camelCase names, case-insensitive
  property matching and numbers read from strings (FR-048). Research R8 shows these three are the
  complete Web set on .NET 10.
- **DECIDED-5 — Error detail.** STJ-named classes; messages without the JSON path suffix (tracking a
  path costs on every call). Manifest rows compare messages as prefixes.
- **DECIDED-6 — Null JSON at a root.** Full parity: reference-type roots read and write `null`,
  value-type roots throw (FR-037). `deserialize` through a `TypeSpec` returns `T | null`, a
  breaking change (FR-034, FR-049) with the [migration note](#migration-breaking-changes). The
  maintainer may batch it with the C# unions major.
- **DECIDED-7 — Getter renames for closed generic roots.** Accepted: FR-031 renames today's
  (non-compiling) `list` / `page` getters to `listEpisodeSummary` / `pageEpisodeSummary`; the
  changelog calls it out.
- **DECIDED-8 — Interface and `[PlainObject]` hierarchies.** Supported in v1 (FR-018, FR-019). Reads
  dispatch on the discriminator, which is authoritative. Writes dispatch on runtime type tests:
  `instanceof` for class-backed variants, synthesized guards for `[PlainObject]` variants (the same
  guard-synthesis decision as C# union cases). Variants whose shapes cannot be told apart on write
  are `MS0032`.
- **DECIDED-9 — Nested polymorphism.** Closed by measurement (research.md R1). STJ does not
  re-dispatch through an outer base; FR-006 mirrors it and `MS0033` states parity rather than a
  limitation.
- **DECIDED-10 — Types from referenced packages.** Admitted in the closure, base links and derived
  links (FR-010, FR-047). The consumer context emits its own getters for library types, as STJ
  does, instead of reusing a library context.
- **DECIDED-11 — Conformance sample name.** `samples/SampleJsonContracts` →
  `targets/js/sample-json-contracts`.
- **DECIDED-12 — Reading the tag of an abstract derived entry** (`FallBackToNearestAncestor` only).
  Probed in S6 (.NET SDK 10.0.401, source generation and reflection alike):
  `{"$type":"m","M":2,"A":1}` and `{"$type":"m"}` through the abstract `AB` throw
  `NotSupportedException` "Deserialization of interface or abstract types is not supported. Type
  'AMid'."; so does the same tag through a concrete base (`CB`) and under
  `IgnoreUnrecognizedTypeDiscriminators` (`IB`). `{"$type":"m","$x":1}` and a data key before a
  later tag throw it too, while `{"$type":"m","$id":"1"}` and `{"$type":"m","$values":[]}` throw the
  "metadata property … not the first property" `JsonException`. With
  `AllowOutOfOrderMetadataProperties`, `{"A":1,"$type":"m"}` throws the `NotSupportedException` and
  `{"$type":"m","$x":1}` the "Properties that start with '$'" `JsonException`. An abstract entry
  `QMid` that registers its own variants (discriminator `t`) throws "The JSON payload for
  polymorphic interface or abstract type 'QMid' must specify a type discriminator." for
  `{"$type":"m","M":2}` and for `{"$type":"m","t":"x","M":2}`, while `{"$type":"x",…}` (its variant
  registered on the outer base too) reads `QLeaf`. Writing an unregistered `AOther : AMid` through
  `AB` writes `{"$type":"m","M":2,"A":1}`. FR-017 states the rule.
- **DECIDED-13 — Closed generic polymorphic bases.** Supported (FR-015).
- **DECIDED-14 — Name-collision strategy.** FR-036 as written: STJ names kept; Error when the loser
  is referenced or the name is a reserved `SerializerContext` member, Warning otherwise.
- **DECIDED-15 — Depth limit on read.** Enforced with the serialize-side counter; the reader message
  and boundary are pinned by an S8 manifest row (FR-016). S4 measured both on .NET 10 and enforces
  the limit already (DECIDED-20 b).
- **DECIDED-16 — Value-root wire mismatches** (`byte[]`, `TimeSpan`, `DateTimeOffset`). Recorded as
  `divergent` manifest rows and documented divergences; `MS0037` for `byte[]`; each converter is
  fixed in its own follow-up issue.
- **DECIDED-17 — Runtime version skew.** The minimum `metano-runtime` version is stated in
  `docs/serialization.md` and the changelog only. FR-049 makes the release a major version, so a
  consumer that upgrades the tools also sees the runtime's major bump.

- **DECIDED-18 — Slice S3a decisions (#270).** Taken while implementing the value roots ahead of
  S1 / S3b / S4:
  - (a) A type argument is named with the same FR-031 function, recursively, so `[Name("User")]` on
    `UserDto` makes `List<UserDto>` → `ListUser` (STJ says `ListUserDto`; a documented divergence)
    and a `TypeInfoPropertyName` override of `Leaf` makes `List<Leaf>` → `List<override>`.
  - (b) `TypeInfoPropertyName` names its type context-wide before the closure is deduplicated, so
    `ref` descriptors and `base` links use it. When one type is listed more than once, the first
    `TypeInfoPropertyName` written for it wins; a later, different one is an `MS0036` Warning at
    that attribute. A repeated root without an override is emitted once, silently.
  - (c) `ImmutableDictionary<K,V>` / `IImmutableDictionary<K,V>` are dictionary-like (a `map`
    descriptor and a TS `Map<K,V>`), matched by name up to a generic boundary (`<` or `` ` ``).
    `SortedDictionary`, `ReadOnlyDictionary` and `FrozenDictionary` are not dictionary-like yet;
    as roots they are `MS0035` (not a transpiled class or struct).
  - (d) `Stack<T>` round-trips in reverse order, as a documented divergence, with no code change.
  - (e) Getters use the member camelCase, with no reserved-word escape: they sit in property
    position, where a JS reserved word is a valid name. A type named `Delete` maps to `delete`
    (it was `delete_` before #270, a getter rename listed in `docs/serialization.md`). The static
    `default` / `_default` members of the generated context are **not** reserved: an instance
    `get default()` / `_default` field does not clash with them (checked with `tsgo --strict` and
    Bun), so a type named `Default` maps to `default` and compiles. `constructor` and the
    `SerializerContext` instance members of FR-036 stay reserved.
  - Until S4 fixes dictionary key conversion, a value root whose descriptor contains a `map` with a
    non-`string` key is `MS0035`. A value root whose element / value / nullable inner object type
    has no object contract (an interface, or a type that is not transpiled in its declaring
    assembly) is `MS0035` instead of a dangling `ref`. S1 lifted the interim S3a rules for closed
    generic object roots and for referenced package types that were not listed as their own root
    (FR-033, FR-047).
  - S3a emits no `valueType` flag and keeps the `TypeSpec` overloads typed `T`; both land with S3b.

- **DECIDED-19 — Slice S1 decisions (#268).** Taken while implementing FR-009 (generator half),
  FR-010, FR-033 and FR-047:
  - (a) One admission rule decides an object contract for roots, the closure and `base` links: a
    class or struct (closed generics included) transpiled in its declaring assembly — this
    project's `[Transpile]` / `[assembly: TranspileAssembly]` rule, or a type the frontend resolved
    to a referenced `[TranspileAssembly]` + `[EmitPackage]` library. A base outside that rule
    (`[Ignore]`, BCL) keeps having its properties inlined. The current-assembly half of FR-010
    (`base:` under `TranspileAssembly`) was already fixed by #253; S1 adds library bases.
  - (b) A closed generic contract passes its mapped type to `createSpec<…>` explicitly, because
    `type: Page` alone infers `Page<unknown>` (checked with `tsgo --strict`).
  - (c) FR-009 generator half, for an inlined (unlinked) property list, following System.Text.Json's
    hierarchy rules: a redeclaration (`override` / `new`, the same CLR name) under the same JSON
    name is declared once, with the most-derived declaration at the base-most position; a
    redeclaration under a different JSON name is written under both names (measured on .NET 10: an
    override renamed `n` writes `{"X":2,"n":"d","Name":"d","Base":1}`); a `[JsonIgnore]`
    redeclaration hides every base declaration of that CLR name (STJ writes none).
  - (d) Distinct CLR properties that share a JSON name (STJ throws `InvalidOperationException`) are
    all kept, never merged: no diagnostic code covers a JSON-name collision inside a contract yet,
    so the generator does not silently drop one of them. A diagnostic for it is a follow-up.
  - (e) Linked chains: the base spec keeps its own declaration for its own instances, so an
    override appears once in each spec of the chain. There is no wire duplicate today
    (`serializeWithSpec` writes by key); what remains is double work and base-first order, which
    S4's `collectWireProperties` fixes. A `[JsonIgnore]` override over a linked base still writes
    the base declaration (STJ writes nothing): the derived spec has no way to suppress a base
    property yet, which needs an "ignored" marker in the derived spec and a runtime rule (open gap,
    S4). The FR-009 phrase "appears only in the most-derived spec" applies to the wire, not to the
    emitted lists. **Resolved in S4** (DECIDED-20 a).
  - (f) S4 must also reorder the generator's inlined lists derived-first: the runtime sees an
    inlined list as one flat spec and has no hierarchy information to reorder it. **Resolved in
    S4**: the emitted `properties` list follows the wire order; the factory keeps reading the
    properties in declaration order (DECIDED-20 a).
  - (g) A closed generic object contract is `MS0035` as a root (directly or as the element of a
    value root) when a type argument has no contract, with the nested value-root rules
    (`Page<System.Version>`, `Page<IShape>`); otherwise its properties would `ref` a missing
    getter. The getter's `TypeSpec<…>` maps type arguments without their nullable annotation
    (`Page<Leaf?>` and `Page<Leaf>` share one contract), so it does not depend on discovery order.

- **DECIDED-20 — Slice S4 decisions (#268, #425).** Taken while implementing FR-009 (runtime half
  and the inlined order), FR-016 and FR-045, measured against a source-generated
  `JsonSerializerContext` on .NET 10:
  - (a) Wire order. STJ writes `class Mid : Derived { int Y; override string Name }` over
    `class Derived : Root { int X; override string Name }` and `class Root { int Base; virtual string
    Name }` as `{"Y":3,"Name":"d","X":2,"Base":1}`, a `new int Base` as `{"X":2,"Base":1,"Name":""}`
    and an override renamed `n` as `{"X":2,"n":"d","Base":1,"Name":"d"}` (`Root` declaring `Base`
    first). The runtime's `collectWireProperties` walks the spec, then its bases, keeps the first
    (most-derived) property of each JSON name and is used for both writing and reading; the
    generator emits an inlined list in the same order. `[JsonIgnore]` on an override over a linked
    base is recorded on the derived spec as `ignoredProperties: ["<ts name>", …]` (the TS names of
    every overridden declaration, after `properties`, only when non-empty); `collectWireProperties`
    then skips base declarations with those `ts` names. A more-derived redeclaration of the property
    is written again, as in STJ. `TypeSpec.ignoredProperties` is a new optional, additive member.
    A constructor parameter bound to an ignored property still gets a factory argument, which is
    `undefined` because the payload carries no key for it, where STJ passes `default(T)`; the
    generator would need a per-type default table (`0`, `false`, `Guid.Empty`, temporal and branded
    defaults) to mirror it, and an own `[JsonIgnore]` member already behaves the same way, so it is
    a documented divergence. A `[JsonIgnore] new` (non-virtual) redeclaration does not hide the base
    declaration (FR-009), and TS has one member slot for both, so the base declaration is written
    with the derived value and at the base's position, where STJ writes the base value at the
    redeclaration's position (documented divergence).
  - (b) Depth. STJ reads 64 nested containers (objects, arrays, dictionaries) and throws on the
    65th; scalars do not count. Read message: `The maximum configured depth of 64 has been exceeded.
    Cannot read next JSON object.` / `… JSON array.`. Write: a source-generated context in its
    default fast-path mode writes 64 nested containers, a scalar inside the 64th included, and
    throws `InvalidOperationException` `CurrentDepth (64) is equal to or larger than the maximum
    allowed depth of 64. Cannot write the next JSON object or array.` at the 65th; in metadata mode,
    and through reflection, it throws FR-016's `JsonException` "A possible object cycle was
    detected. …", already for a non-null scalar written inside the 64th container
    (`class VNode { int Value; VNode? Next }`: 64 nodes throw). Metano is a hybrid: the fast path's
    boundary (only containers count) with the metadata mode's `JsonException` and message
    (documented divergence for both modes). The counter is one process-global count of open
    containers, safe only because no serialize or deserialize call yields; on read it counts only
    the containers the contract visits, and a custom converter that serializes a separate root
    inherits the outer depth (STJ starts that root at depth 0).
  - (c) Errors. Non-object input for an object contract (now including a JSON array) throws
    `JsonException` `The JSON value could not be converted to <constructor name>.`, with `Object`
    for a `[PlainObject]` contract that has no constructor (S5 adds `polymorphism.typeName` for
    polymorphic bases only). The array and hash-set converters reject non-array input with
    `JsonException` `Expected array for deserialization, got <object|string|number|boolean>` (#425;
    STJ names the CLR collection type, which the descriptor does not carry), and the dictionary
    converter rejects a non-object or an array with `Expected object for deserialization, got
    <array|string|number|boolean>` (it used to read `[1,2]` as `Map {"0" => 1, …}`); `null` still
    reads as `null`. The enum converters' existing errors become `JsonException` with unchanged
    messages; rejecting an unknown numeric value and the string-enum message are pre-existing
    divergences from STJ.
    `NotSupportedException` lands with the polymorphism runtime (S5).

- **DECIDED-21 — Slice S5 decisions (#268).** Taken while implementing the polymorphism runtime
  (FR-005–FR-008, FR-017 write side, FR-018 and FR-019 runtime halves) with hand-written specs,
  measured against a source-generated `JsonSerializerContext` on .NET 10:
  - (a) `serializeWithSpec` / `deserializeWithSpec` dispatch over the module-internal
    `serializeContract` / `deserializeContract` in `converters.ts`; the dispatch index and the
    rules live in `polymorphism.ts`. Error type names come from `contractNameOf`
    (`constructor.name`, then `polymorphism.typeName`, then `Object`), which also names a
    polymorphic base in the existing non-object error.
  - (b) Number tags. STJ reads a number tag as an `Int32`: `{"$type":2}` and `{"$type":-1}` throw
    `Read unrecognized type discriminator id '2'.` / `'-1'`, a number tag on a base with only
    string tags is an unrecognized id (`'1'`), and a number that is not an `Int32` (`1.5`,
    `3000000000`) throws `JsonException` `The JSON value could not be converted to <Base>.`
    (measured). The runtime mirrors all of them; `1.0` / `1e0` stay a documented divergence (they
    parse to `1`).
  - (c) `FallBackToNearestAncestor` reaches the base when the base registers itself: an
    unregistered `NSelfZ : NSelf` writes `{"$type":"b","V":1}` (measured). Without a registered
    ancestor the base contract is written with no tag (`{"V":1}`, measured).
  - (d) Untagged registrations write their own contract with no tag (`{"A":2,"V":1}`) and read back
    as the base (measured).
  - (e) A concrete `[PlainObject]` base that does not register itself has no slot for its guard in
    `PolymorphismSpec`. **Interim** (until S6c): the runtime writes **every** plain object that
    matches no variant guard as the base (no tag) when the base is concrete and has no
    constructor, instead of testing the base guard first and otherwise following
    `unknownDerivedType` (FR-019 rule 5). A self-registered base is an entry with a `test` and is
    checked like any variant. S6c adds a base-guard member to `PolymorphismSpec` and the rule 5
    check (plan.md, S6c row), which removes the interim behavior. **Resolved in S6c**
    (DECIDED-24 b): the member is `baseTest`.
  - (f) FR-017 read side (the tag of an abstract derived entry) is **not** implemented: no member
    of the descriptor marks an entry as abstract, so its tag reads through the entry's throwing
    factory (a plain `Error`). Measured STJ text, both modes: `NotSupportedException`
    "Deserialization of interface or abstract types is not supported. Type 'AMid'." S6 adds the
    marker and this message (it replaces FR-017's interim rule).
  - (g) `JsonReadOptions` lands with `allowOutOfOrderMetadataProperties` only, read from the
    polymorphic spec's `readOptions`; the other two members, the `SerializerContext.readOptions`
    getter and `createSpec` attaching it stay in S2b (FR-002 plumbing). Null roots through a
    polymorphic spec keep today's behavior until S3b (FR-037).
  - (h) Tag not first on a concrete base (measured): `{"V":1,"$type":"d","W":2}` throws
    `JsonException` "The metadata property is either not supported by the type or is not the first
    property in the deserialized JSON object." — also for an unknown later tag, under
    `IgnoreUnrecognizedTypeDiscriminators`, for a self-registered base, with a custom discriminator
    and when an unknown key comes first. Before this, the runtime read the base and dropped the
    derived members (`W`). An abstract or interface base keeps the `NotSupportedException` of a
    missing tag.
  - (i) Array-index keys (`"0"`, `"1"`, …) are enumerated before every other key of a parsed
    object, so the "first key" is the first key that is not an array index. An index key after the
    tag reads as in STJ (measured: `{"$type":"d","0":5,…}` reads `CDer`); one written before the
    tag is undetectable (documented divergence).
  - (j) Polymorphic payloads only (the base and the contract reached by dispatch), all measured.
    Default options: a first key `$id` / `$ref` throws the "metadata property … not the first
    property" `JsonException` and a first key `$values` throws "A '$values' metadata property must
    always be preceded by other metadata properties, such as '$id' or '$type'.", both before an
    abstract base reports a missing tag; any other first key (`$x` included) leaves the
    missing-tag `NotSupportedException` of an abstract base first. A leading tag is then resolved
    (its JSON kind, an unknown id), and the remaining keys are checked in document order: a later
    discriminator, `$id`, `$ref` or `$values` → "metadata property … not the first property"; any
    other `$`-prefixed key → "Properties that start with '$' are not allowed in types that support
    metadata. …". With `allowOutOfOrderMetadataProperties`, every key is checked in document
    order and the contract is chosen after the walk (measured rows): the tag's JSON kind where it
    appears (`{"V":1,"$type":null,"$x":1}` → the `Null` token message); `$id` / `$ref` anywhere →
    "metadata property … not the first property"; `{"$type":"zz","$x":1}` → the `$` message.
    `$values` makes STJ expect a preserved array, and its two-reason message is "Deserialization
    failed for one of these reasons:\n1. A JSON object containing metadata for a nested array
    includes a non-metadata property '<key>'.\n2. The JSON value could not be converted to
    <Base>.": a data key before `$values` names `$values` (`{"V":1,"$values":[]}`,
    `{"$type":"d","V":1,"$values":[]}`, `{"Seconds":1,"$values":[]}`,
    `{"V":1,"$values":[],"$type":null}`); a non-metadata key after it names that key
    (`{"$values":[],"V":1}` → `V`, `{"$values":[],"$x":1}` → `$x`, `{"$values":[],"Seconds":1}` →
    `Seconds`, `{"$type":"d","$values":[],"V":1}` and `{"$values":[],"$type":"d","V":1}` → `V`).
    After `$values`, `$id` → "not the first property" and a `null` tag → the `Null` token message.
    An object that ends with `$values` pending resolves its tag first (`{"$values":[],"$type":"zz"}`
    and `{"$type":"zz","$values":[]}` → "Read unrecognized type discriminator id 'zz'.") and then
    throws "not the first property" (`{"$type":"d","$values":[]}`, `{"$values":[],"$type":"d"}`,
    `{"kind":"image","$values":[]}`, `{"$values":[],"kind":"image"}` on `Clip`); without a tag
    it throws the leading-`$values` message, on an abstract base too, ahead of the missing tag
    (`{"$values":[]}`). Under `IgnoreUnrecognizedTypeDiscriminators`, a pending `$values` with an
    unknown tag throws "not the first property" on a concrete and on an abstract base alike
    (`{"$values":[],"$type":"zz"}`; measured, the abstract base does not report the missing tag).

- **DECIDED-22 — Slice S6 decisions (#268).** Taken while implementing the generator half of
  polymorphism for class hierarchies (FR-001, FR-003–FR-006, FR-011–FR-015, FR-017):
  - (a) `DerivedTypeSpec` gains `abstract?: boolean` (contracts/runtime-api.md), the marker
    DECIDED-21 f asked for: it is emitted on an abstract registration (valid only under
    `FallBackToNearestAncestor`) and makes the runtime throw the DECIDED-12 exceptions when the
    tag is read. A marker on the entry, rather than on `TypeSpec`, keeps non-polymorphic specs
    unchanged (FR-004).
  - (b) Interface bases (FR-018) and hierarchies with a `[PlainObject]` base or variant (FR-019)
    are slice S6c. Until it lands the generator treats them as non-polymorphic: no
    `polymorphism` and no closure additions, so their contexts are unchanged, but a temporary
    `MS0033` warning says so ("Polymorphism for interface and [PlainObject] types is not
    supported yet; 'Shape' is emitted without a discriminator."); S6c removes it. An interface
    registered on a **class** base is not assignable to it and is `MS0032` now.
  - (c) An invalid registration (`MS0032`) is left out of `derived`; a repeated type or tag keeps
    its first registration. A hierarchy-wide `MS0032` (a JSON property named like the
    discriminator, compared ordinally after the naming policy, or integer-like) leaves the base
    without `polymorphism`, because no descriptor would match System.Text.Json. The rules are
    evaluated per constructed base the closure reaches (FR-015), once per context.
  - (d) The property rules read every contract written with a tag: each valid tagged
    registration (the base too when it registers itself with a tag), inherited properties
    included (their linked `base` specs or inlined ancestors). The diagnostic is located at the
    property (revised in review, (l)).
  - (e) Closure order: per entry, its `base` link, then the `ref` targets of its properties, then
    its registrations in attribute order (a self-registration adds nothing). A derived link counts
    as a link for `MS0036` severity, like `ref` and `base`.
  - (f) The `polymorphism` member keys follow `PolymorphismSpec`: `discriminator`, `abstract`,
    `derived`, `unknownDerivedType`, `ignoreUnrecognizedTags`; an entry's are `tag`, `spec`,
    `abstract`. `typeName` and `testOrder` stay with S6c (class bases have a constructor).
  - (g) An abstract registration's own spec is not polymorphic, so its factory keeps the #238
    message (FR-011); the runtime never calls it, as reading its tag throws first.
  - (h) FR-002 (`AllowOutOfOrderMetadataProperties` read from the context) stays with S2b, which
    adds the `readOptions` plumbing; S6 emits no read options.
  - (i) End-to-end evidence: the TypeScript generated for the S6 golden
    (`tests/Metano.Tests/Expected/json-polymorphic-context.ts`) compiles under `tsgo --strict` and
    reproduces every probe row its test header quotes (bundled with `bun build`, run under Node and
    Bun). The permanent cross-runtime check is the S8 manifest.
  - (j) Polymorphism is read only for the closure's dispatch targets: roots, `ref` targets and
    registered types. A type reached only through a `base` link is never dispatched through, and
    STJ never builds its polymorphism, so it gets no `polymorphism`, no registrations and no
    diagnostics; a type that becomes a dispatch target after the walk reached it through `base`
    is expanded then (FR-015).
  - (k) A hierarchy whose registrations are all untagged reads like a non-polymorphic contract
    (measured, see Edge Cases). The runtime decides it (`resolveDeserializeTarget` returns the base,
    or throws STJ's abstract-type `NotSupportedException` for an abstract base, when no entry has a
    `tag`), so the descriptor needs no extra member and hand-written specs behave the same; writes
    still dispatch by constructor.
  - (l) Review round of S6 (STJ probe, .NET 10): a sealed base, `[JsonPolymorphic]` without
    registrations and a reserved discriminator (`$id`, `$ref`, `$values`) are `MS0032` at the
    `[JsonPolymorphic]` attribute (else the first `[JsonDerivedType]`); the property rules check
    only the contracts written with a tag; a diagnostic shared by several constructions of a
    generic base is reported once (messages name the definition, `GBase<T>`). An empty
    discriminator (`""`) is valid in STJ (`{"":"d","V":1}`) and is emitted as is.

- **DECIDED-23 — Slice S2b decisions (#268).** Taken while implementing the context read options
  (FR-002, FR-048 items 2–5), measured with source-generated contexts on .NET 10 (SDK 10.0.401,
  runtime 10.0.12):
  - (a) `OrdinalIgnoreCase`, not `toUpperCase()`. A comparison over every BMP character showed
    that `toUpperCase()` differs from .NET's ordinal casing for `ı` → `I`, `ſ` → `S` (.NET keeps
    both), multi-character full mappings (`ß` → `SS`; .NET keeps `ß`) and the Greek letters with
    ypogegrammeni (U+1F80–U+1FF3; .NET maps them to their titlecase letter). The runtime
    (`ordinal-ignore-case.ts`) maps each code point with `toUpperCase()` when it yields one code
    point and lists those exceptions; what is left differs only where the engine's Unicode version
    differs from .NET's (Node 24: U+A7CF, U+A7D3, U+A7D5). Measured: `ÉCOLE` matches `école`,
    `ςigma` matches `Σigma`; `STRASSE`, `ıd`, `İD`, `ſize` and `\u212Aey` match nothing. "Last
    match wins" follows the order of the parsed object's keys: when the text repeats an exact key
    around a case variant (`{"count":1,"COUNT":2,"count":3}`), `JSON.parse` keeps the repeated
    key at its first position with its last value, so the runtime reads `2` where STJ reads `3`
    (documented divergence, undetectable after `JSON.parse`).
  - (b) The discriminator a tagged polymorphic read consumes is never a property, even when a
    property matches it case-insensitively: with `PropertyNameCaseInsensitive = true`, a `kind`
    discriminator and a `KindDer(string Kind)` variant, `{"kind":"k","Kind":"x"}` reads
    `Kind = "x"`, `{"kind":"k"}` leaves `Kind` unset and `{"Kind":"x","kind":"k"}` throws the
    "metadata property … not the first property" `JsonException` (measured).
  - (c) The error of a rejected numeric string names the CLR numeric type (`System.Int32`,
    ``System.Nullable`1[System.Int32]``, `System.Decimal`). STJ names it so for roots, collection
    elements, dictionary values and settable properties, but names the declaring type with its
    namespace for a property bound to a constructor parameter (`TestApp.Stats`, measured), which
    a `PropertySpec` cannot tell apart without a new member. Documented divergence; the S8
    manifest compares the prefix `The JSON value could not be converted to `.
  - (d) Read options travel with each spec: a contract reads its own properties with its own
    `readOptions`, a `ref` reads with its target spec's, and a value spec with its own. Generated
    contexts attach the same options to every spec, so this equals STJ's per-call options.
    `createSpec` / `createValueSpec` attach the context's options under any the spec declares
    itself, and leave the spec unchanged when both are empty, so default contexts are untouched.
  - (e) Not mirrored: two properties whose JSON names differ only in case under a case-insensitive
    context (STJ throws `InvalidOperationException` at first use; Metano keeps both); dictionary
    keys (STJ parses `{"+5":1}` as key `5` for `Dictionary<int,int>` under any options; the runtime
    keeps string keys, unchanged); `[JsonNumberHandling]` on members or types; and the
    `WriteAsString` / `AllowNamedFloatingPointLiterals` flags (FR-046, slice S6b). A
    `NumberHandling` that combines `AllowReadingFromString` with them still mirrors the reading
    flag.
  - (f) Decimals (revised in review). Before S2b the built-in decimal converter returned the raw
    JSON value, so a property typed `Decimal` held a `number` (and threw `toNumber is not a
    function` when written back). The `decimal` descriptor now carries `type` (the mapped
    decimal.js `Decimal`, `{ kind: "decimal", type: Decimal }`, as a temporal descriptor carries
    `parse`), and the converter builds `new Decimal(value)` from a JSON number, or from the JSON
    text when the context reads numbers from strings, so a string keeps its significant digits;
    trailing zeros (the scale) are lost on read and on write (`new Decimal("1.10")` prints `1.1`).
    A JSON number is already a double after `JSON.parse`, so its range is checked as a double
    (`|value| <= Number("79228162514264337593543950335")`, which is 2^96): STJ reads the numbers
    `79228162514264337593543950335`, `…335.4` and `7.9228162514264337593543950335e28` as
    `MaxValue` and so does the runtime, while `…335.5` and `…336`, which STJ rejects, parse to the
    same double and read (undetectable after `JSON.parse`); the next double
    (`79228162514264355185729994752`) is rejected on both sides (measured). A JSON
    number outside the decimal range (`1e29`), a string without the option (measured under
    `General`: `"1"` throws) and any other JSON value throw `The JSON value could not be converted
    to System.Decimal.`; a hand-written descriptor without `type` keeps the JSON value. Writing
    stayed `toNumber()` in S2b: STJ writes the decimal's exact digits (measured: `1.10m` → `1.10`, `1.0m`
    → `1.0`, `0.00m` → `0.00`, `79228162514264337593543950335m` and
    `1.2345678901234567890123456789m` in full, `1e2` read → `100`), which a JS number cannot
    carry through `JSON.stringify`. **Revised by #458 (3.0 major):** the converter writes
    `JSON.rawJSON(value.toFixed())` (FR-048 item 6), which changes what `serialize` returns. STJ
    measured on .NET 10.0.12 (System.Text.Json 10.0.0.0, source-generated context) and matched
    byte for byte on Node 24.11 and Bun 1.4.2, except the scale:

    | C# value | STJ writes | Metano writes |
    |---|---|---|
    | `0m`, `1m`, `100m`, `-42m`, `19.99m`, `-0.5m` | `0`, `1`, `100`, `-42`, `19.99`, `-0.5` | same |
    | `decimal.MaxValue` / `MinValue` | `79228162514264337593543950335` / `-79228162514264337593543950335` | same (`toNumber()` gave `7.922816251426434e+28`) |
    | `12345678901234567890.123456789m` | `12345678901234567890.123456789` | same (`toNumber()` gave `12345678901234567000`) |
    | `1.2345678901234567890123456789m` | `1.2345678901234567890123456789` | same |
    | `1000000000000000000000m`, `10000000000000000000000000000m` | plain digits | same (decimal.js `toString()` prints `1e+21`, `1e+28`; `toFixed()` does not) |
    | `0.0000001m`, `±0.0000000000000000000000000001m` | plain digits | same (decimal.js `toString()` prints `1e-7`, `1e-28`) |
    | `decimal.Parse("1.5e3")` | `1500` | same |
    | `decimal.Negate(0m)` (sign bit set) | `0` | `0` (decimal.js `toFixed()` of `-0` prints `0`) |
    | `1.10m`, `1.0m`, `0.00m`, `1000.000m`, `decimal.Parse("-0.00")` | `1.10`, `1.0`, `0.00`, `1000.000`, `0.00` | `1.1`, `1`, `0`, `1000`, `0` (the scale) |

    Trailing zeros remain a documented divergence. Values outside the `System.Decimal` domain
    (NaN, ±Infinity, beyond `MaxValue`) throw `JsonException` on write (FR-048 item 6). A
    `Single` read from a string
    keeps double precision, as one read from a JSON number does; only overflow is checked at
    single precision, against the exact text: the threshold `2^128 − 2^103` is itself a double,
    so a text that parses to it reads only when its exact value is below it (measured:
    `"3.4028235677973366e38"` reads, `"340282356779733661637539395458142568448"` throws). `numberType` is emitted on every numeric `primitive` descriptor, dictionary
    keys included, and the runtime ignores it there.

    **Revised by #457 (reads):** `JsonSerializer.deserializeText` reads a `decimal` from its
    source digits (FR-048 item 7), which resolves the read side of the range divergence above
    for text reads. STJ measured on .NET 10.0.12 (System.Text.Json 10.0.0.0, source-generated
    context, `Deserialize(string)` then `Serialize`), matched on Node 24.11 and Bun 1.4.2:

    | JSON text | STJ writes back | `deserializeText` then `serialize` |
    |---|---|---|
    | `12345678901234567890.123456789`, `79228162514264337593543950335`, `9007199254740993` | the same digits | same (the tree path read `12345678901234567000`, `79228162514264340000000000000`, which is past `MaxValue`, and `9007199254740992`) |
    | `79228162514264337593543950335.4`, `7.9228162514264337593543950335e28` | `79228162514264337593543950335` | same |
    | `79228162514264337593543950335.5`, `79228162514264337593543950336`, `1e29` | `JsonException` "could not be converted to System.Decimal." | same (the tree path accepted the first two) |
    | `0.33333333333333333333333333333333`, `0.66666666666666666666666666666666` | `0.3333333333333333333333333333`, `0.6666666666666666666666666667` | same |
    | `1e-29`, `1.5e-29`, `5e-29`, `1e-400` | `0.0000000000000000000000000000` | `0` (the scale) |
    | `9.9e-29`, `1.5e-28`, `2.5e-28`, `3.5e-28` | `1e-28`, `2e-28`, `2e-28`, `4e-28` in plain digits (half to even) | same |
    | `0.12345678901234567890123456785`, `…775` | `0.1234567890123456789012345678` for both (half to even) | same |
    | `1.00000000000000000000000000025`, `12345678901234567890123456788.5` | `1.0000000000000000000000000003`, `12345678901234567890123456789` (a tie at the 30th digit of the text rounds up) | same |
    | `0.0123456789012345678901234567850e1`, `0.0000000000000000000000000000025e2` | `0.1234567890123456789012345679`, `0.0000000000000000000000000003` (leading fraction zeros count toward the 30th digit) | same |
    | `"12345678901234567890.123456789"`, `"0.33333333333333333333333333333333"` (Web) | the same digits, `0.3333333333333333333333333333` | same |
    | `"1e-29"` (Web) | `0.0000000000000000000000000000` | `0` (the scale) |
    | `"1.5"` (General), `"79228162514264337593543950335.5"`, `" 1"`, `""`, `"NaN"` (Web) | `JsonException` | same |

    The 20 019 values of the #458 probe, read from STJ's text and written back, match STJ byte for
    byte (17 855) or differ only in the scale (2 164); through `JSON.parse` + `deserialize`, 8 696
    differed. A leading byte order mark is rejected by both (`'0xEF' is an invalid start of a
    value.`).
  - (g) End-to-end evidence: the TypeScript generated for the Web golden
    (`tests/Metano.Tests/Expected/json-web-context.ts`) was bundled with `bun build` and run under
    Node and Bun against 37 STJ rows (8 writes, 29 reads): every write matched byte for byte and
    every read agreed, except the type names of (c) and the namespace in the missing-tag message
    of a namespaced base (`'TestApp.Clip'`; the runtime names `'Clip'`, which predates S2b).

- **DECIDED-24 — Slice S6c decisions (#268).** Taken while implementing interface bases (FR-018)
  and `[PlainObject]` hierarchies (FR-019), probed against a source-generated context on .NET SDK
  10.0.401 (rows quoted in `tests/Metano.Tests/JsonStructuralPolymorphismTests.cs`):
  - (a) **Guard trigger.** A `[PlainObject]` type gets its synthesized `isT` when a
    `[JsonDerivedType]` of its compilation registers it, or when it is a concrete, non-generic
    polymorphic `[PlainObject]` type (its base or self-registration guard), read from the
    attributes rather than from the contract closure of a context (plan.md planned the closure).
    A registered type always lives in its base's assembly (the base names it with `typeof`, and
    it derives from the base), so a library emits the guards a consumer's context imports
    without knowing that context (FR-047). A registration no context reaches still gets its
    guard, so every registered `[PlainObject]` type now exports `isT` from its module (a visible
    change of the generated API). The context references the guard as `isPoint` (a module export of the variant's
    file), `Shape.isCircle` (a nested type's companion namespace) or `isPoint` with the package
    origin (a library type).
  - (b) **`baseTest`.** The base-guard member DECIDED-21 e asked for is
    `PolymorphismSpec.baseTest?: (value: unknown) => boolean`, emitted after `testOrder` for a
    concrete, non-self-registered, non-generic `[PlainObject]` base. The runtime tests it after
    every variant guard and writes the base (no tag) only when it accepts the value; otherwise
    the value follows `unknownDerivedType` with the runtime type name `Object`. A hand-written
    spec without `baseTest` no longer writes every unmatched plain object as the base. An
    unregistered C# subtype of the base whose shape passes `baseTest` is written as the base
    (`Cat4(string Name, int Meow) : Animal4` writes `{"Name":"c"}`; STJ throws
    `NotSupportedException` "Runtime type 'Cat4' is not supported by polymorphic type
    'Animal4'.", measured): plain objects carry no runtime type (documented divergence). Known
    gap: a generic concrete `[PlainObject]` base gets no guard, hence no `baseTest`; generic
    `[PlainObject]` contracts already fail `tsgo`, so the gap is moot today.
  - (c) A synthesized guard is the predicate only (no `assertT`); a type that also declares
    `[GenerateGuard]` keeps both.
  - (d) **Guard member tests.** A guard calls `isN` for a member only when `N` has a guard of
    this compilation (`[GenerateGuard]` or synthesized); any other named type is checked for
    presence (`v.from != null`), as cross-package types already were. Before S6c a
    `[GenerateGuard]` guard called an `isN` nothing declared or imported (an invalid module).
    The coarse kinds come from the same switch that emits the checks: `typeof` checks give
    `string` / `number` / `boolean` / `bigint`, `Array.isArray` and tuples `array`,
    `instanceof Map` / `Set` / `Temporal.*` `object`, a guard call or a presence check
    `present`; a nullable member is not required and keeps its inner kind; an unchecked member
    (`any`) is never required.
  - (e) **Order and rejection.** The test order is a stable topological sort of the "more
    specific" relation (attribute order among unrelated variants); `testOrder` lists only the
    structural entries' indices. Variants that cover each other are reported at the later
    registration, naming the union of both variants' required members (`none` when neither
    requires one); a cycle names the variants left unordered; with a `baseTest`, each variant
    whose guard covers the base guard is reported at its registration (review: `Animal2(string
    Name)` with `Dog2(string Name, int? Bark)` would write `{name: "a"}` as
    `{"$type":"dog","Bark":null,"Name":"a"}`, where STJ writes `{"Name":"a"}`). Each leaves the
    base without `polymorphism`. A `[Discriminator]` member is compared with the variant's own
    literal, and two different literals never cover each other (review: `KA(Kind Kind, double X)`
    and `KB(Kind Kind, double X)` are distinguishable). A registered `[PlainObject]` type without
    a guard is `MS0032` and left out of `derived`; a base whose `baseTest` would have no guard
    is `MS0032` too.
  - (f) **Interface properties** follow the source generator's topological sort (Kahn's
    algorithm) over each interface's `AllInterfaces`: `ID : IB, IC, IE` with `IB : IA` and
    `IE : IA` writes `{"D":4,"E":5,"B":2,"A":1,"C":3}`, and `IP0 : IQ0, IRr0` with `IQ0 : IS0` and
    `IRr0 : IT0, IS0` writes `{"P0":5,"Rr0":4,"Q0":3,"S0":1,"T0":2}` (measured; the direct
    interfaces alone order `T0` before `S0`, a review finding).
  - (g) An interface base's factory names the C# interface (`Cannot deserialize interface type
    'IShape' directly; …`); `typeName` is the C# simple name of the definition (`IBox` for
    `IBox<int>`). STJ's messages name the type with its namespace (`'P.IShape'`); the runtime
    names it as for class bases (DECIDED-5 compares prefixes).
  - (h) Only interfaces with `[JsonDerivedType]` are object contracts; any other non-collection
    interface stays `MS0035` as a root and out of the closure. Interface entries are excluded
    from the FR-049 constructor checks (no `type:`).
  - (i) An abstract polymorphic `[PlainObject]` base gets the throwing FR-011 factory
    (contracts/context-codegen.md §1b); a `[PlainObject]` factory literal now carries the
    inherited members of its linked base specs (it dropped them before, so `point3` read
    `{ y, z }` without `x`), except the base declarations a `[JsonIgnore]` override hides.
  - (j) End-to-end evidence: the TypeScript of the S6c golden
    (`tests/Metano.Tests/Expected/json-structural-polymorphic-context.ts`) compiles under
    `tsgo --strict` and, bundled with `bun build` and run under Node, writes every probe row
    byte-identically and reads each tag back into the expected class or plain object. A read of
    `{"$type":"sq","Side":2}` (no `Area`) throws the FR-045 missing-property `JsonException`,
    while STJ ignores the getter-only `Area` on read (the pre-existing FR-045 divergence).

## Open questions

These came up while applying the decisions. Each has a recommended default that the spec assumes
until the maintainer decides.

- **OPEN-18 — Interface-typed derived entries.** STJ accepts an interface as a derived entry under
  `FallBackToNearestAncestor` and resolves the nearest ancestor through interface inheritance
  (diamonds throw). A TS value carries no interface identity, and a guard for an interface entry
  would overlap with the guards and constructors of its implementations. *Recommended*: reject
  interface-typed derived entries with `MS0032` in v1 (FR-012); interface **bases** are supported
  (FR-018). Alternative: allow them under `FallBackToNearestAncestor` only, resolving the nearest
  interface ancestor at transpile time from the implementations in the closure, after probing STJ's
  diamond rules.
- **OPEN-19 — Structural distinguishability rule versus C# unions.** The unions specification
  (PR #310) treats any two structural (guarded) cases, or a structural case next to a class case, as
  overlapping (its rule 4, OD-15). Polymorphism needs a finer rule: a hierarchy of `[PlainObject]`
  records normally has several variants, and the coarse rule would reject almost all of them.
  FR-019 instead orders guards by specificity, rejects only variants that cover each other, and
  evaluates guards only for plain objects, so class and structural variants cannot overlap.
  *Recommended*: keep the FR-019 rule for polymorphism. Both specs build the guard with the same
  builder and the same runtime-type-test classification; only the overlap rule differs, because a
  polymorphic hierarchy is closed and the discriminator is nominal on read. Alternative: adopt one
  shared rule in both specs (the FR-019 analysis, if the unions spec relaxes OD-15).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of manifest rows with `sides` including both `stj` and `ts` agree: write and
  roundtrip rows are byte-identical between STJ source-gen output and the generated TypeScript
  serializer, and every read and roundtrip row yields an instance of the expected class on both
  sides. Rows restricted to one side or marked `divergent` are each traced to an entry under
  Documented divergences.
- **SC-002**: 100% of the error rows throw the same exception class name and message prefix on
  every side they list.
- **SC-003**: A context with one root of each supported shape (≥ 15 roots) compiles under
  `tsgo --strict` with 0 errors (today: the file does not compile), and each value-root kind has a
  manifest row.
- **SC-004**: Every existing golden file and JSON test passes unchanged, except the documented
  FR-010 `base:` additions, FR-009 dedupes, FR-037 `valueType` flags on struct contracts, FR-048
  Web-context changes and DECIDED-7 renames. Hand-written TS tests that use the result of
  `deserialize` through a `TypeSpec` as `T` are updated for the `T | null` return type (FR-034),
  which is the migration of FR-049 applied to this repository.
- **SC-005**: With `[JsonFactory]`, 0 invalid branded values are accepted by `deserialize` in the
  factory test matrix (blank, too long, wrong primitive); without it, output is byte-identical.
- **SC-006**: Each of the 6 diagnostics has at least one test per rule, and no rule produces an
  invalid `.ts` file instead of a diagnostic.
- **SC-007**: Every Web-context manifest row (camelCase write, case-insensitive read, each accepted
  and rejected number string of FR-048) agrees on both sides.
- **SC-008**: Interface and `[PlainObject]` hierarchies round-trip on both sides for every variant
  in the sample, and a write of each `[PlainObject]` variant selects that variant (the FR-019
  order), with 0 misrouted values in the manifest.

## Assumptions

- .NET 10 STJ behavior (measured on SDK 10.0.401) is the reference; later STJ changes are tracked
  by the conformance manifest failing in CI.
- Generated packages that use polymorphism or value specs need the `metano-runtime` version that
  ships these additions. Since #411 `PackageJsonWriter` declares `metano-runtime` as a
  `peerDependency` of every generated package that imports it, with the compiler's own version
  range (a consumer's own declaration wins), so the requirement is also communicated through docs
  and the changelog (DECIDED-17). The major version of FR-049 makes the upgrade visible.
- Deserialization is fed by `JSON.parse` output, so lexical facts STJ sees (duplicate keys, exact
  number text) are not observable to the runtime.
- C# 15 `union` types and STJ union support (.NET 11 RC) are specified separately (C# unions
  specification, PR #310, diagnostics `MS0038`–`MS0043`). Both specs synthesize guards for
  `[PlainObject]` / interface shapes with the same builder; OPEN-19 tracks the one rule where they
  differ.
