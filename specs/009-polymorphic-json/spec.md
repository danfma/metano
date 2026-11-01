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
   are emitted into the companion namespace).
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
   throw `JsonException` "The JSON value could not be converted to System.Int32." (FR-048).
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

- **Out-of-order tag**: `{"Path":"a","kind":"image",...}` is treated as *missing* tag (STJ
  default). With `[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]` on the
  context, the tag may appear anywhere (FR-002).
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
- **Abstract derived type**: rejected (`MS0032`) unless the base uses `FallBackToNearestAncestor`,
  where it serves only as a nearest-ancestor target; reading its tag follows the S6 probe
  (DECIDED-12).
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
  not preferred over an earlier case-insensitive one (STJ applies them in document order).
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
  used in error messages. Specs of non-polymorphic types MUST NOT change.
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
  specify a type discriminator." and a concrete base reads the base contract.
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
  be told apart on write (FR-019); a derived type is generic (open or closed, v1); a derived type is
  not assignable to a constructed polymorphic base reached by the closure (FR-015); a data
  property of any contract in the hierarchy has a final JSON name (after `[JsonPropertyName]` /
  policy) equal to the discriminator (STJ throws `InvalidOperationException` for this); a JSON
  property name in the hierarchy is integer-like.
- **FR-013**: The generator MUST report `MS0033` (`LossyJsonPolymorphism`, Warning) when a derived
  type is registered without a tag (serializes without a tag and reads back as the base, exactly
  as in STJ), and when a derived type is itself polymorphic (informational: its own
  `[JsonDerivedType]`s apply only through its own spec, as in STJ).
- **FR-014**: The discriminator name and values MUST be emitted verbatim. Data property names keep
  today's rule (`[JsonPropertyName]`, else the context naming policy, else the C# name). The naming
  policy also comes from `JsonSerializerDefaults.Web` (camelCase) when the context passes it, and an
  explicit `PropertyNamingPolicy` wins over it (FR-048).
- **FR-015**: A **closed generic polymorphic base** (`GDer : GBase<int>` with
  `[JsonDerivedType(typeof(GDer), "g")]` on `GBase<T>`) MUST be supported (DECIDED-13): the attributes
  are read from the original definition; every constructed base the closure reaches gets its own
  spec named by FR-031 (`gBaseInt32`, `TypeSpec<GBase<number>>`); its `base` link, derived links
  and getter all use the constructed name. For each constructed base, `MS0032` reports every
  registration whose type is not assignable to that construction (STJ throws only when such a
  registration is reached through that closing, e.g. `GBase<string>`).
- **FR-016**: `serializeContract` and `deserializeContract` MUST track the JSON container depth and
  throw `JsonException` "A possible object cycle was detected. This can either be due to a cycle or
  if the object depth is larger than the maximum allowed depth of 64. Consider using
  ReferenceHandler.Preserve on JsonSerializerOptions to support cycles." on serialize when the
  depth exceeds 64, instead of overflowing the stack. The exact boundary (depth 64 vs 65) is pinned
  by a manifest row measured against STJ. The read side MUST enforce the same limit
  (DECIDED-15): `deserializeContract` counts container depth on the parsed value and throws
  `JsonException` with STJ's reader message once S8 has measured that message and boundary. Until
  that row exists, the read-side limit is listed as a documented divergence.
- **FR-017**: Under `FallBackToNearestAncestor`, an abstract derived entry MUST be accepted as a
  nearest-ancestor target on serialize (its tag, if any, is written, then the runtime value's
  nearest-registered-ancestor contract). Reading its tag MUST be probed against STJ before S6 is
  implemented (DECIDED-12). Until the probe, the runtime treats such a tag as an abstract base with
  a missing tag (`NotSupportedException` "…must specify a type discriminator." naming the abstract
  entry), and the probe result replaces this rule and adds a manifest row.
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
     told apart on write. The generator reports `MS0032` naming both and the shared members. A
     cycle in the "more specific" relation is reported the same way.
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
  `[Inline]`), has exactly one parameter whose type is the wrapper's underlying primitive, returns
  the containing type, is the only `[JsonFactory]` of its type, has no same-named static overload,
  and has a TS name that differs from every member the companion synthesizes (`create`,
  `toString`). Any accessibility is valid. Otherwise the generator MUST report `MS0034`
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
  FR-009 removes a duplicated overridden property, FR-037 adds `valueType: true` to struct
  contracts, and FR-048 changes contexts declared with `JsonSerializerDefaults.Web` (camelCase
  names, read options, number hints). The types of call sites change with FR-034 (breaking,
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
     property by comparing `toUpperCase()` of both names (STJ: `OrdinalIgnoreCase`); keys are
     applied in the parsed document order, so the last match wins (measured). The discriminator is
     always matched case-sensitively (measured).
  4. **Numbers read from strings**: when the option is set, the generator adds
     `numberType: "<CLR name>"` (for example `"System.Int32"`, `"System.Double"`) to every
     `primitive` descriptor of a C# numeric type, and the `decimal` converter reads strings too. A
     JSON string is accepted by STJ's measured grammar: integer types accept `[+-]?[0-9]+`
     (leading zeros allowed) within the type's range; `Single` / `Double` accept the JSON number
     grammar with an optional leading `+`, and the exact texts `NaN`, `Infinity` and `-Infinity`;
     no whitespace, hex, empty string or other spelling. A rejected string throws `JsonException`
     `The JSON value could not be converted to <CLR name>.` (measured text for `System.Int32`). The
     option applies to properties, collection elements, dictionary values and roots (measured), and
     never to enums, `Nullable<T>` given `"null"`, or int discriminators (measured).
  5. Writing is unaffected: `AllowReadingFromString` changes reads only (measured: numbers are
     still written as JSON numbers).
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
| Int tag written `1.0` / `1e0` | `JsonException` "could not be converted" | `JSON.parse` yields `1`, accepted | Undetectable after `JSON.parse` |
| Number text `1E+21`, `-0` | as shown | `1e+21`, `0` | Fixtures avoid them |
| Writing `NaN` / `±Infinity` without `AllowNamedFloatingPointLiterals` | `ArgumentException` (measured) | `JSON.stringify` writes `null` | Pre-existing; reachable now that Web contexts read `"NaN"` |
| Integer-like property names in a polymorphic hierarchy | allowed | JS key order puts them first | Rejected, `MS0032` |
| `byte[]` | base64 string | number array | `MS0037`; follow-up issue (DECIDED-16) |
| `TimeSpan` | `"01:02:03"` | `Temporal.Duration` string `PT1H2M3S` | Manifest row `divergent`; follow-up issue (DECIDED-16) |
| `DateTimeOffset` offset text | `+00:00` | per temporal converter | Manifest row; `divergent` if the bytes differ (DECIDED-16) |
| `Dictionary<int, …>` key order | insertion order | JS enumerates integer-like keys first | Manifest row `divergent` |
| `[StringEnum]` root/property | number unless `JsonStringEnumConverter` | string | Pre-existing type-mapping contract, unchanged |
| `[JsonPropertyOrder]` | reorders properties | declaration order | `MS0037` (DECIDED-3) |
| Missing non-required property | default value | `JsonException` | FR-045 |
| Unregistered C# subtype of a `[PlainObject]` hierarchy whose shape satisfies a registered variant's guard | `NotSupportedException` (knows the runtime type) | written as the matching variant | FR-019; plain objects carry no runtime type |
| Runtime type name of a plain object in `Runtime type '…' is not supported` | the C# type name | `Object` | FR-019 |
| Case-insensitive matching of non-ASCII names | `OrdinalIgnoreCase` | `toUpperCase()` | Not measured; fixtures use ASCII names; probed in S8 |
| Read depth limit | reader throws at max depth | enforced once S8 pins the message | DECIDED-15 |
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

The same release notes also list these changes, which do not break semantic JSON consumers: the
derived-first key order (FR-009), the `JsonException` class of the existing errors (FR-045), the
generic root getter renames (DECIDED-7), camelCase names and lenient reads for Web contexts
(FR-048), and the minimum `metano-runtime` version (DECIDED-17).

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
  Probe STJ in S6 before implementing; FR-017 holds the interim rule.
- **DECIDED-13 — Closed generic polymorphic bases.** Supported (FR-015).
- **DECIDED-14 — Name-collision strategy.** FR-036 as written: STJ names kept; Error when the loser
  is referenced or the name is a reserved `SerializerContext` member, Warning otherwise.
- **DECIDED-15 — Depth limit on read.** Enforced with the serialize-side counter; the reader message
  and boundary are pinned by an S8 manifest row (FR-016).
- **DECIDED-16 — Value-root wire mismatches** (`byte[]`, `TimeSpan`, `DateTimeOffset`). Recorded as
  `divergent` manifest rows and documented divergences; `MS0037` for `byte[]`; each converter is
  fixed in its own follow-up issue.
- **DECIDED-17 — Runtime version skew.** The minimum `metano-runtime` version is stated in
  `docs/serialization.md` and the changelog only. FR-049 makes the release a major version, so a
  consumer that upgrades the tools also sees the runtime's major bump.

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
  ships these additions. `PackageJsonWriter` does not write a `metano-runtime` dependency today
  (user-managed), so the requirement is communicated through docs and the changelog
  (DECIDED-17). The major version of FR-049 makes the upgrade visible.
- Deserialization is fed by `JSON.parse` output, so lexical facts STJ sees (duplicate keys, exact
  number text) are not observable to the runtime.
- C# 15 `union` types and STJ union support (.NET 11 RC) are specified separately (C# unions
  specification, PR #310, diagnostics `MS0038`–`MS0043`). Both specs synthesize guards for
  `[PlainObject]` / interface shapes with the same builder; OPEN-19 tracks the one rule where they
  differ.
