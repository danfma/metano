# Feature Specification: System.Text.Json-faithful JSON contracts (polymorphism, validating branded factories, non-object roots)

**Feature Branch**: `spec/008-polymorphic-json` (spec directory `009-polymorphic-json`; `008` is
already taken by [`008-exports-subpath-root`](../008-exports-subpath-root/spec.md), see OPEN-1)

**Created**: 2026-10-31

**Status**: Draft (revised after critic review; awaiting maintainer decisions on the open items)

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
R1–R4).

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
    (FR-015, OPEN-13).

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
   `FallBackToNearestAncestor`, an interface or `[PlainObject]` base or variant, a generic derived
   type, or a data property whose final JSON name equals the discriminator, **Then** `MS0032`
   (Error) is reported. Registering the base type itself is **not** an error.
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
   OPEN-14). A getter name equal to a `SerializerContext` member is always an Error.
6. **Given** a context whose `[JsonSourceGenerationOptions]` sets a wire-affecting option the
   generator does not mirror (e.g. `DefaultIgnoreCondition`, `NumberHandling`,
   `ReferenceHandler`, `Converters`), or a root / property of type `byte[]`, **Then** `MS0037`
   (Warning) names the option or member (FR-046).

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

### Edge Cases

- **Out-of-order tag**: `{"Path":"a","kind":"image",...}` is treated as *missing* tag (STJ
  default). With `[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]` on the
  context, the tag may appear anywhere.
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
  where it serves only as a nearest-ancestor target; reading its tag is OPEN-12.
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
- **Dispatch by constructor identity**: values copied with spread / `structuredClone`, or classes
  loaded twice from duplicated package copies, are "unregistered runtime types" and fail under the
  default handling.
- **Custom converter for kind `ref`**: context converters match by descriptor kind only, so such a
  converter bypasses polymorphic dispatch for every `ref` — documented, unchanged.
- **Null JSON root**: see FR-037 / OPEN-6.
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
  `[JsonSourceGenerationOptions]` and apply it to every polymorphic spec of that context.
- **FR-003**: The contract closure (#250) MUST include every type named by a `[JsonDerivedType]` of
  a type already in the closure, transitively, together with its transpilable base chain and the
  types reachable from its properties — mirroring STJ, which exposes a `JsonTypeInfo` for every
  registered derived type. Discovery stays deterministic (breadth-first, attribute order).
- **FR-004**: A polymorphic type's spec MUST carry a data-only `polymorphism` descriptor: the
  discriminator name **verbatim** (never passed through the naming policy), whether the C# base is
  abstract, and the derived entries in attribute order, each with its tag (`string`, `number`, or
  absent) and a **lazy** link (`() => this.<getter>`) to its spec. A self-registration of the base
  is an entry whose link points at the base's own getter. Non-default options
  (`unknownDerivedType`, `ignoreUnrecognizedTags`, `allowOutOfOrderMetadata`) are emitted only when
  set. Specs of non-polymorphic types MUST NOT change.
- **FR-005**: Serializing through a polymorphic spec MUST resolve the value's runtime constructor
  **first against the registered entries** (`byCtor`, which includes the base itself when it is
  self-registered): a registered type → its tag (if any) **as the first JSON property**, then its
  contract; otherwise, the base itself → base contract, no tag; otherwise per
  `UnknownDerivedTypeHandling` (`FailSerialization` → `NotSupportedException`; `FallBackToBaseType`
  → base contract, no tag; `FallBackToNearestAncestor` → nearest registered ancestor in the
  prototype chain, abstract entries included, else the base contract). The dispatch MUST apply
  wherever the spec is used: the root, `ref`, and through `array`, `hashSet`, `map` and `nullable`
  descriptors.
- **FR-006**: Serializing through a non-polymorphic spec (including a derived type's own spec and an
  intermediate type of a polymorphic hierarchy) MUST NOT write a tag and MUST NOT re-dispatch. A
  derived type that is itself polymorphic is not re-dispatched when reached through an outer base
  either — this is STJ's behavior (research.md R1, nested polymorphism rows).
- **FR-007**: Deserializing through a polymorphic spec MUST: treat the tag as present only when the
  discriminator name matches **case-sensitively** and — unless `allowOutOfOrderMetadata` — it is
  the first key; reject a present tag that is neither a JSON string nor a JSON number with
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
  `Path: … | LineNumber: … | BytePositionInLine: …` suffix (OPEN-5). The two pre-existing plain
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
  the base or a derived type is an interface or `[PlainObject]` (no constructor identity to
  dispatch on, v1 — OPEN-8); a derived type is generic (open or closed, v1); a derived type is
  not assignable to a constructed polymorphic base reached by the closure (FR-015); a data
  property of any contract in the hierarchy has a final JSON name (after `[JsonPropertyName]` /
  policy) equal to the discriminator (STJ throws `InvalidOperationException` for this); a JSON
  property name in the hierarchy is integer-like.
- **FR-013**: The generator MUST report `MS0033` (`LossyJsonPolymorphism`, Warning) when a derived
  type is registered without a tag (serializes without a tag and reads back as the base, exactly
  as in STJ), and when a derived type is itself polymorphic (informational: its own
  `[JsonDerivedType]`s apply only through its own spec, as in STJ).
- **FR-014**: The discriminator name and values MUST be emitted verbatim. Data property names keep
  today's rule (`[JsonPropertyName]`, else the context naming policy, else the C# name), with the
  Web-defaults correction of OPEN-4.
- **FR-015**: A **closed generic polymorphic base** (`GDer : GBase<int>` with
  `[JsonDerivedType(typeof(GDer), "g")]` on `GBase<T>`) MUST be supported (OPEN-13): the attributes
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
  by a manifest row measured against STJ; the read side follows OPEN-15.
- **FR-017**: Under `FallBackToNearestAncestor`, an abstract derived entry MUST be accepted as a
  nearest-ancestor target on serialize (its tag, if any, is written, then the runtime value's
  nearest-registered-ancestor contract). Reading its tag follows OPEN-12, which MUST be probed
  against STJ before S6 is implemented.

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
  never with a bare generic name; a reference-type value root adds `| null` (FR-037).
- **FR-033**: The contract closure MUST admit **closed generic object types** wherever they are
  reached — roots, properties (directly or through nullable / array / map / hash-set), `base` links
  and derived links — keyed by their FR-031 name, as well as the object types reachable from a
  non-object root (element, map value, nullable inner). `BuildRefDescriptor` and the getter name
  helper already route through the FR-031 name, so every emitted `ref` resolves to an emitted
  getter. A golden for `record Feed(Page<EpisodeSummary> Latest)` MUST compile under
  `tsgo --strict`.
- **FR-034**: The runtime MUST add `ValueSpec<T> { descriptor: TypeDescriptor }` and
  `JsonTypeInfo<T> = TypeSpec<T> | ValueSpec<T>`. `JsonSerializer.serialize` and
  `BoundSerializer.serialize` MUST keep the `TypeSpec<T>` overload (returning
  `Record<string, unknown>`) and add a `ValueSpec<T>` overload (returning `unknown`); both
  `deserialize` methods MUST accept `JsonTypeInfo<T>`. A `ValueSpec` is converted through the same
  converter resolution as a property, so context converters apply by kind and polymorphic elements
  dispatch per FR-005/FR-007. `specFor` and the constructor registry are unchanged.
- **FR-035**: The generator MUST report `MS0035` (`InvalidJsonSerializableRoot`, Error) at the
  attribute for a root that is an open generic or type parameter, an error type, `object` /
  `dynamic`, an interface that is not a recognized collection/dictionary/set, an **object-kind type
  that is not a transpilable class/struct** (BCL types such as `KeyValuePair<,>`, `ValueTuple`,
  `[NoTranspile]` / `[Ignore]` / `[External]` types, types from assemblies that are neither the
  current one nor a Metano package), or an **array of rank > 1** (`int[,]`; STJ does not support
  it) — instead of dropping it or emitting an invalid spec.
- **FR-036**: The generator MUST report `MS0036` (`JsonTypeInfoNameCollision`) when two distinct
  types map to the same member name, or when a member name (getter or `_` field) equals a
  `SerializerContext` instance member (`options`, `converters`, `createSpec`, `resolve`,
  `_registry`, `constructor`). Severity: **Error** when the losing type is the target of any `ref`,
  `base` or derived link (otherwise every such link would silently serialize the winner's
  contract), and always for a reserved-member collision; **Warning** only when the losing type is
  an unreferenced root, which is then not emitted (mirrors STJ `SYSLIB1031`). The first type in
  discovery order keeps the name. Alternative strategy: OPEN-14.
- **FR-037**: Null at a root (OPEN-6): a reference-type `ValueSpec` root (collections, `string`,
  dictionaries, sets) is typed `ValueSpec<T | null>`, reads JSON `null` as `null` and writes a
  `null` value as JSON `null`; a value-type `ValueSpec` root (`int`, `Guid`, enums, branded) throws
  `JsonException` for JSON `null`, like STJ. A `TypeSpec` root follows the OPEN-6 decision; the
  recommended default throws `JsonException` for JSON `null` (documented divergence).
- **FR-038**: `arrayConverter` (and the hash-set path) MUST serialize through
  `Array.from(value as Iterable<unknown>, (x) => elemConv.serialize(x))`, so `Iterable<T>` values
  produced for `IEnumerable<T>`, `IReadOnlyCollection<T>` and `IQueryable<T>` roots and properties
  (a `Set`, a generator, a LINQ pipe) serialize as JSON arrays instead of throwing `TypeError`.

#### D. Cross-cutting

- **FR-040**: Contexts that use none of the new attributes and only object roots MUST produce
  byte-identical generated TypeScript, except where FR-010 adds a previously missing `base:` link
  and FR-009 removes a duplicated overridden property.
- **FR-041**: The runtime additions MUST be additive and exported from
  `metano-runtime/system/json` (`ValueSpec`, `JsonTypeInfo`, `PolymorphismSpec`, `DerivedTypeSpec`,
  `TypeDiscriminator`, `UnknownDerivedTypeHandling`, `JsonException`, `NotSupportedException`,
  `collectWireProperties`). The generated context adds **no new value import**: types that appear
  only inside `ValueSpec<…>` / `TypeSpec<…>` type arguments (`HashSet`, `Temporal`, `Decimal`,
  `UUID`, cross-package element types) are imported **type-only** (`import type` or a per-name
  `type` qualifier), and those imports MUST be produced by the context transformer itself, since
  the context bypasses the IR runtime-requirement scanner.
- **FR-042**: The Dart target is out of scope (it has no JSON context; roadmap RT-02).
- **FR-043**: Documentation MUST be updated: `docs/serialization.md` (polymorphism, roots, factory,
  wire order, error classes, the documented divergences, the minimum `metano-runtime` version, and
  the two false claims about base properties in factories and `new Decimal`), the attribute catalog
  (`[JsonFactory]`), the diagnostic catalog (`MS0032`–`MS0037`), the feature-support matrix
  Serialization rows, and the changelog / release notes (minimum runtime version, OPEN-7 renames).
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
  `IgnoreReadOnlyFields`, `IncludeFields`, `NumberHandling`, `UseStringEnumConverter`,
  `Converters`, `ReferenceHandler`, `PropertyNameCaseInsensitive`, `DictionaryKeyPolicy`,
  `ReadCommentHandling`, `AllowTrailingCommas`, `UnmappedMemberHandling`, `RespectNullableAnnotations`,
  `RespectRequiredConstructorParameters`. Formatting-only options (`WriteIndented`, `Encoder`) are
  not reported: `JSON.stringify` formatting belongs to the caller. It MUST also report `MS0037` at the member for a `[JsonInclude]`
  field / non-public property (not collected) and for a `byte[]` root or property (STJ writes
  base64; Metano writes a number array). Only `PropertyNamingPolicy` (and its `JsonSerializerDefaults.Web`
  default, OPEN-4) and `AllowOutOfOrderMetadataProperties` are mirrored.
- **FR-047**: Cross-package contracts (OPEN-10): the closure MUST admit object types declared in a
  referenced `[TranspileAssembly]` + `[EmitPackage]` library (FR-010 rule), with imports resolved
  through the existing `OriginResolver` path and the package added to `package.json#dependencies`
  as for any cross-package import. This covers: a polymorphic base and its variants declared in the
  library with the context in the consumer; a consumer type deriving from a library base (base link
  and inherited properties present); a consumer property typed with a library record. A
  `[JsonDerivedType]` target that is not importable (not transpilable in its declaring assembly)
  is `MS0032`.

### Diagnostics (reserved codes, see OPEN-2)

| Code | Name | Severity | Raised for |
|---|---|---|---|
| `MS0032` | `InvalidJsonPolymorphism` | Error | FR-012, FR-015, FR-047 rules |
| `MS0033` | `LossyJsonPolymorphism` | Warning | FR-013 rules |
| `MS0034` | `InvalidJsonFactory` | Error | FR-022, FR-023 |
| `MS0035` | `InvalidJsonSerializableRoot` | Error | FR-035 |
| `MS0036` | `JsonTypeInfoNameCollision` | Error / Warning | FR-036 |
| `MS0037` | `UnmirroredJsonOption` | Warning | FR-046 |

### Documented divergences

Each item is listed in `docs/serialization.md` and, where a fixture exists, marked in the manifest
(`sides` restricted, or `divergent` with both texts recorded).

| Area | STJ | Metano | Status |
|---|---|---|---|
| Duplicate tag keys `{"kind":"a","kind":"b"}` | `JsonException` | `JSON.parse` keeps the last value | Undetectable after `JSON.parse` |
| Int tag written `1.0` / `1e0` | `JsonException` "could not be converted" | `JSON.parse` yields `1`, accepted | Undetectable after `JSON.parse` |
| Number text `1E+21`, `-0` | as shown | `1e+21`, `0` | Fixtures avoid them |
| Integer-like property names in a polymorphic hierarchy | allowed | JS key order puts them first | Rejected, `MS0032` |
| `byte[]` | base64 string | number array | `MS0037`; follow-up issue (OPEN-16) |
| `TimeSpan` | `"01:02:03"` | `Temporal.Duration` string `PT1H2M3S` | Manifest row `divergent`; follow-up issue (OPEN-16) |
| `DateTimeOffset` offset text | `+00:00` | per temporal converter | Manifest row; `divergent` if the bytes differ (OPEN-16) |
| `Dictionary<int, …>` key order | insertion order | JS enumerates integer-like keys first | Manifest row `divergent` |
| `[StringEnum]` root/property | number unless `JsonStringEnumConverter` | string | Pre-existing type-mapping contract, unchanged |
| Missing non-required property | default value | `JsonException` | FR-045 |
| Null `TypeSpec` root | `null` | `JsonException` | OPEN-6 recommended default |
| Read leniency of `JsonSerializerDefaults.Web` (case-insensitive names, numbers from strings) | lenient | strict | OPEN-4 |
| Read depth limit | reader throws at max depth | see OPEN-15 | OPEN-15 |
| Error messages | with a `Path`, `LineNumber`, `BytePositionInLine` suffix | without | OPEN-5 |

### Key Entities

- **Polymorphism descriptor** — data on a base `TypeSpec`: discriminator, abstract flag, derived
  entries (tag + lazy spec), unknown-type handling, ignore flag, out-of-order flag.
- **Derived entry** — one `[JsonDerivedType]`: optional tag (`string | number`) and a thunk to the
  registered type's spec (its `type` is read from that spec, never duplicated); may point at the
  base itself.
- **Value spec** — a root that is not an object: a single `TypeDescriptor`.
- **JSON type info name** — the STJ-compatible member name of a context entry, produced by one
  function.
- **JSON factory** — the one static method of a branded type that deserialization calls.
- **Conformance case** — one manifest row: id, context, getter, direction, sides, expected class,
  JSON text or error.

## Open decisions

Every item below has a recommended default that this spec and plan assume. The maintainer
confirms or overrides each before implementation starts.

- **OPEN-1 — Directory number.** The task named `008-polymorphic-json`, but `008-exports-subpath-root`
  exists on `main`. *Recommended*: keep `009-polymorphic-json`; the branch name stays as created.
- **OPEN-2 — Diagnostic numbers.** `MS0029`–`MS0031` are on `main` (`MS0031` landed with #278,
  `d96909c`). Re-checked after rebasing on `0fa9f4c`: no `origin` branch mentions `MS0032`–`MS0039`.
  *Recommended*: reserve `MS0032`–`MS0037` as above; re-check at merge time of each slice.
- **OPEN-3 — Option coverage in v1.** *Recommended*: all of int tags, the three
  `UnknownDerivedTypeHandling` modes, `IgnoreUnrecognizedTypeDiscriminators` and
  `AllowOutOfOrderMetadataProperties` (each measured, each a few lines). `[JsonPropertyOrder]` is
  **out** (not polymorphism-specific; it would need a `PropertySpec.order`). Alternative: report
  `MS0037` when `[JsonPropertyOrder]` appears on a contract member.
- **OPEN-4 — `JsonSerializerDefaults.Web`.** `ReadNamingPolicy` only reads the named
  `PropertyNamingPolicy` argument, so `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]`
  contexts keep PascalCase in TS while .NET writes camelCase (measured: `{"kind":"image","path":"a",
  "seconds":1.5}`). *Recommended*: adopt **only the camelCase naming** of Web defaults in this
  feature (an explicit `PropertyNamingPolicy` still wins), as its own `fix:` commit. The other Web
  defaults (`PropertyNameCaseInsensitive = true`, `NumberHandling = AllowReadingFromString`) only
  relax reading; the TS reader stays strict, which is listed as a divergence and reported by
  `MS0037` when those options are set explicitly.
- **OPEN-5 — Error detail.** *Recommended*: STJ-named classes, messages without the JSON path suffix
  (tracking a path costs on every call). Alternative: track `$.a[0].b` paths for full message
  parity.
- **OPEN-6 — Null JSON at a root.** STJ returns `null` for every reference-type root, `ValueSpec`
  or object (polymorphic included), and throws for value-type roots. *Recommended*: mirror STJ for
  reference-type `ValueSpec` roots, typed `ValueSpec<T | null>` so the null is visible (FR-037);
  for `TypeSpec` roots keep `deserialize(...): T` and throw `JsonException` for JSON `null`
  (documented divergence), because widening the `TypeSpec` overload to `T | null` breaks every
  existing strict caller. Alternative: widen `deserialize` for `TypeSpec<T>` to return `T | null`
  (full parity, breaking type change, needs a changelog entry). Fixtures: `null` through `clip`
  (TS-side error row, STJ-side `null` row), `listClip` (both: `null`) and `int32` (both:
  `JsonException`).
- **OPEN-7 — Getter renames for closed generic roots.** FR-031 renames today's (non-compiling)
  `list` / `page` getters to `listEpisodeSummary` / `pageEpisodeSummary`. *Recommended*: accept
  (the old output never compiled for collections; generic object roots had no tests). Call it out
  in the changelog.
- **OPEN-8 — Interface and `[PlainObject]` hierarchies.** *Recommended*: reject with `MS0032` in v1.
  Later: dispatch through generated `[GenerateGuard]` predicates or a stored tag field.
- **OPEN-9 — Nested polymorphism.** *Closed*: measured (research.md R1). STJ does not re-dispatch
  through an outer base; FR-006 mirrors it and `MS0033` states parity rather than a limitation.
- **OPEN-10 — Types from referenced packages.** *Recommended* (now specified as FR-010 / FR-047):
  admit referenced Metano-package types in the closure, base links and derived links. To confirm:
  the consumer context emits its own getters for library types (as STJ does) rather than reusing a
  library context.
- **OPEN-11 — Conformance sample name.** *Recommended*: `samples/SampleJsonContracts` →
  `targets/js/sample-json-contracts` (covers all three issues).
- **OPEN-12 — Reading the tag of an abstract derived entry** (`FallBackToNearestAncestor` only). Not
  probed. *Recommended*: probe in S6 before implementing; until then the runtime treats such a tag
  as an abstract base with a missing tag (`NotSupportedException` "…must specify a type
  discriminator." naming the abstract entry), and the manifest gets the measured row.
- **OPEN-13 — Closed generic polymorphic bases.** *Recommended*: support them (FR-015), since FR-033
  already introduces constructed-type naming in the closure. Alternative: reject generic
  polymorphic bases with `MS0032` in v1 and add a test.
- **OPEN-14 — Name-collision strategy.** *Recommended*: FR-036 as written (Error when the loser is
  referenced, STJ names kept). Alternative: disambiguate automatically with a containing-type- or
  namespace-qualified name (`clipImage`) for the loser, keep STJ's name for the first, and document
  the divergence from STJ's property names.
- **OPEN-15 — Depth limit on read.** STJ's reader also enforces max depth 64, but `JSON.parse`
  already accepted the text. *Recommended*: enforce the same depth counter in
  `deserializeContract` and throw `JsonException` with STJ's reader message once probed; until
  probed, document as a divergence.
- **OPEN-16 — Value-root wire mismatches** (`byte[]`, `TimeSpan`, `DateTimeOffset`). *Recommended*:
  record them as `divergent` manifest rows and documented divergences in this feature, report
  `MS0037` for `byte[]`, and fix each converter in its own follow-up issue (they change existing
  property behavior, not only roots).
- **OPEN-17 — Runtime version skew.** `PackageJsonWriter` writes no `metano-runtime` dependency (it
  is user-managed), so nothing pins the version that understands `polymorphism`. A bundler that
  skips type checking (Vite) on an older runtime silently writes no tag. *Recommended*: state the
  minimum runtime version in docs and the changelog only. Alternative: have `PackageJsonWriter` add
  a `metano-runtime` peer range, or have `createSpec` throw on an unknown descriptor shape version.

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
  FR-010 `base:` additions, FR-009 dedupes and OPEN-7 renames.
- **SC-005**: With `[JsonFactory]`, 0 invalid branded values are accepted by `deserialize` in the
  factory test matrix (blank, too long, wrong primitive); without it, output is byte-identical.
- **SC-006**: Each of the 6 diagnostics has at least one test per rule, and no rule produces an
  invalid `.ts` file instead of a diagnostic.

## Assumptions

- .NET 10 STJ behavior (measured on SDK 10.0.401) is the reference; later STJ changes are tracked
  by the conformance manifest failing in CI.
- Generated packages that use polymorphism or value specs need the `metano-runtime` version that
  ships these additions. `PackageJsonWriter` does not write a `metano-runtime` dependency today
  (user-managed), so the requirement is communicated through docs and the changelog (OPEN-17).
- Deserialization is fed by `JSON.parse` output, so lexical facts STJ sees (duplicate keys, exact
  number text) are not observable to the runtime.
- C# 15 `union` types and STJ union support (.NET 11 RC) are a separate future feature.
