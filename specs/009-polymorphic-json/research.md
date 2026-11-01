# Research: System.Text.Json-faithful JSON contracts

Evidence tags: **[Verified]** = reproduced by running code (probe or test) or by reading the cited
source on `main` at `7ff7544` (re-checked where noted against `0fa9f4c` after the rebase); **[Inferred]** = reasoned from code or docs, not executed.

Sources: the phase 3/4 research notes (sections `stj-hierarchy`, `json-codegen-map`, design
`polymorphic-json`) and the batch 3 triage (`json-collection-root`, `branded-validating-factory`),
kept outside the repository by the maintainer; plus a source-generation probe run for this spec
(.NET SDK 10.0.401, `net10.0`, one `JsonSerializerContext` with every case below).

## R1 — STJ polymorphic wire format

**Facts** (reflection and source-gen byte-identical where both were run) [Verified]:

| Case | STJ output / behavior |
|---|---|
| `ImageClip` through base `Clip` | `{"kind":"image","Path":"a.png","Seconds":1.5}` |
| `ImageClip` through `ImageClip` | `{"Path":"a.png","Seconds":1.5}` (no tag) |
| `List<Clip>` root | `[{"kind":"image","Path":"a","Seconds":1.5},{"kind":"silence","Seconds":2}]` |
| `Dictionary<string, Clip>` root | `{"x":{"kind":"silence","Seconds":2}}` |
| Web defaults | `{"kind":"image","path":"a","seconds":1.5}` — tag name/value never renamed |
| No `TypeDiscriminatorPropertyName` | `{"$type":"a","V":1}` |
| Int tag `[JsonDerivedType(typeof(IA), 1)]` | `{"$type":1,"X":5}`; reading `"1"` (string) → `JsonException: Read unrecognized type discriminator id '1'.` |
| Mixed int + string tags on one base | allowed: `{"$type":"s","Y":2}` |
| `HLeaf(A,B,C) : HMid(A,B) : HBase(A)` | `{"t":"leaf","C":3,"B":2,"A":1}` — most-derived properties first |
| Non-polymorphic hierarchy | also derived-first: `class CD` → `{"DerP":2,"BaseP":1}` |
| Untagged derived `[JsonDerivedType(typeof(NA))]` | writes `{"V":1}`; reads back as the base (data lost) |
| Concrete base instance | no tag (`{}`); missing tag on read → base |
| Base registers itself `[JsonDerivedType(typeof(SBase), "b")]` | valid; writes `{"$type":"b","V":1}`; reads back as `SBase` (critic probe, net10.0) |
| Abstract derived type `[JsonDerivedType(typeof(AMid))]` | `InvalidOperationException: … cannot be abstract classes or interfaces unless 'JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor' is specified` (critic probe) |
| Closed generic base: `GDer : GBase<int>`, `[JsonDerivedType(typeof(GDer),"g")]` on `GBase<T>` | through `GBase<int>`: `{"$type":"g","Extra":2,"Value":1}`; throws only when reached through `GBase<string>` (critic probe) |
| Nested polymorphism, `NMid : NBase`, both polymorphic | through `NBase`, an `NMid` writes only the outer tag `{"t":"mid","B":2,"A":1}`; an `NLeaf` registered only on `NMid` → `NotSupportedException` through `NBase`; the inner tag is ignored on read (critic probe) |
| Overridden property (`ODer` overrides `Name`) | written once, at the derived position: `{"$type":"o","X":2,"Name":"x","Base":1}` (critic probe) |
| Non-scalar tag | message names the token: `True`, `False`, `StartObject`, `StartArray`, `Null` (critic probe) |
| Int tag written `1.0` | `JsonException: The JSON value could not be converted to IBase.` — not observable after `JSON.parse` (critic probe) |
| `Deserialize<RefType>("null")` | `null` for every reference-type root, object and polymorphic included; throws for value types (critic probe) |
| Object cycle | `JsonException: A possible object cycle was detected …` at MaxDepth 64 [Inferred from STJ docs; boundary pinned by a fixture in S8] |
| Value roots | `byte[]` → base64 `"AQID"`; `TimeSpan` → `"01:02:03"`; `DateTimeOffset` → `…+00:00` (critic probe); TS side: number array, `PT1H2M3S` [Inferred from `converters.ts`] |
| Tag not first (default) | `NotSupportedException: The JSON payload for polymorphic interface or abstract type 'Clip' must specify a type discriminator.` |
| Tag not first + `AllowOutOfOrderMetadataProperties = true` on the context | reads `ImageClip` (source-gen probe) |
| Unknown tag | `JsonException: Read unrecognized type discriminator id 'video'.` (case-sensitive: `Image` also fails) |
| Missing tag, abstract base | `NotSupportedException … must specify a type discriminator.` |
| Wrong-case tag name `Kind` under Web (case-insensitive props) | treated as missing |
| Null tag | `JsonException: The '$id', '$ref' or '$type' metadata properties must be JSON strings. Current token type is 'Null'.` |
| Duplicate tag | `JsonException: Deserialized object contains a duplicate 'kind' metadata property.` |
| Unregistered runtime type, default | `NotSupportedException: Runtime type 'UFailX' is not supported by polymorphic type 'UFail'.` (source-gen probe) |
| `FallBackToNearestAncestor` | `{"$type":"a","B":2,"A":1}` (source-gen probe) |
| `FallBackToBaseType`, runtime type derives from a registered type | `{"A":1}` (source-gen probe) |
| `IgnoreUnrecognizedTypeDiscriminators`, concrete base, unknown tag | base (`IgnBase { V = 1 }`) (source-gen probe) |
| Same flag, abstract base, unknown tag | `NotSupportedException … must specify a type discriminator.` (source-gen probe) |
| Data property named like the tag (after policy) | `InvalidOperationException: The type 'KA' contains property 'kind' that conflicts with an existing metadata property name.` (Web); without a policy both `kind` and `Kind` are written |
| Intermediate static type `HMid` (not polymorphic) | `{"B":2,"A":1}`; reading a `leaf` tag through `HMid` ignores tag and `C` |
| Tag present, read through the derived type | tag ignored as unknown property |
| Doubles | shortest round-trip form (`2`, `1.5`), but `1E+21` and `-0` (JS: `1e+21`, `0`) |

**Decision D1**: Mirror every row except duplicate-tag detection, `1.0` int tags and
exponent/negative-zero number text (not observable after `JSON.parse` / not reproducible by
`JSON.stringify`), and the value-root converter mismatches of DECIDED-16. Each exception is listed in
spec.md "Documented divergences".

**Gaps not measured**: interface-typed derived entries and interface diamonds under
`FallBackToNearestAncestor` (OPEN-18, rejected in v1); reading the tag of an abstract derived entry
under `FallBackToNearestAncestor` (DECIDED-12, probed in S6); the read-side depth limit message and
boundary (DECIDED-15, pinned in S8). Interface bases, Web defaults and null roots are measured in
R8. Nested polymorphism is measured (rows above), which closed OPEN-9.

## R2 — Where the generator stands on `main` (post #238 / #250)

[Verified, `src/Metano.Compiler.TypeScript/Transformation/JsonSerializerContextTransformer.cs`]:

- The context bypasses the IR: `TypeTransformer` hands `JsonSerializerContext` subclasses straight to
  the transformer, which reads Roslyn symbols.
- `ReadSerializableTypes` keeps only `ConstructorArguments[0].Value is INamedTypeSymbol` — array
  roots are dropped — and ignores `TypeInfoPropertyName`.
- `CollectContractClosure` (#250) walks roots, then properties (through nullable / array / map /
  hash-set) and the `base` chain; `IsDiscoverableObjectType` admits only non-generic
  (`IsGenericType: false`), current-assembly (`IsInCurrentAssembly`), transpilable
  classes/structs. Consequences: a property typed `Page<EpisodeSummary>` emits
  `ref → () => this.page` with no such getter (TS2339) → **FR-033**; a property typed with a
  library record dangles the same way → **FR-047**. `TranspilableBaseOf(current)` is enqueued
  with no `IsGenericType` check, so a closed generic base is linked as `this.gBase` /
  `TypeSpec<GBase>` → **FR-015**. Derived types named by `[JsonDerivedType]` are not discovered.
- `CollectSerializableProperties` reads own `GetMembers()` public properties only, with no dedupe
  against base declarations: an overridden property appears in both the base and derived lists →
  **FR-009**; `[JsonInclude]` fields are not collected → **FR-046**.
- `SpecGetterName` = camelCase of `ResolveTsName` (honors `[Name]`, drops type arguments); used by
  roots, refs and base links (single helper since #250).
- `SpecTypeReferenceName` already qualifies nested types (`Shape.Circle`).
- `BuildConstructorArguments` reads constructor parameters from the whole hierarchy (#238), and the
  derived positional record `super(...)` bug is fixed (`Expected/inheritance-derived.ts`).
- `BuildFactoryStatement` throws for abstract classes ("polymorphic JSON deserialization is not
  supported yet").
- `TranspilableBaseOf` calls `SymbolHelper.IsTranspilable(baseType)` **without**
  `assemblyWideTranspile` / `currentAssembly`, so under `[assembly: TranspileAssembly]` no `base:`
  link is emitted and inherited properties vanish from the wire. The existing test
  `ReachedDerivedType_PullsInItsBaseSpec` uses `[Transpile]` and does not catch it → **FR-010**.
  Passing the current assembly is not enough: `IsAssemblyWideOptIn` requires the type to live in
  the current compilation, so a base from a referenced `[TranspileAssembly]` library would still be
  dropped; transpilability must be evaluated against `type.ContainingAssembly` [Inferred from
  `SymbolHelper.IsAssemblyWideOptIn`]. `JsonSerializerContextTests` has no `TranspileWithLibrary`
  test.
- The context bypasses the IR, so `IrRuntimeRequirementScanner` never sees the type arguments of
  `ValueSpec<…>`; imports for `HashSet`, `Temporal`, `Decimal`, `UUID` and cross-package element
  types must be produced by the context transformer (comment near `ImportCollector.cs` line 150)
  → **FR-041**.
- `IrTypeRefMapper` maps `IEnumerable<T>`, `IReadOnlyCollection<T>` and `IQueryable<T>` to TS
  `Iterable<T>`, while `arrayConverter.serialize` does `(value as unknown[]).map(…)`, which throws
  for a `Set`, a generator or a LINQ pipe → **FR-038** (affects properties today too).
- `ReadNamingPolicy` reads only `PropertyNamingPolicy`; no other source-gen option is read →
  **FR-046** (`MS0037`).
- `deserializeWithSpec` throws plain `Error`s for non-object input and missing properties →
  **FR-045**.
- `ReadNamingPolicy` reads only the named `PropertyNamingPolicy` argument → FR-048 (DECIDED-4: full Web parity).
- `BuildBrandedDescriptor` always emits `create: <TsName>.create` → **FR-021**.
- The non-object root problem is general: the root path never consults `ClassifyPropertyType`,
  although that classifier already produces the right kind for every shape (the same types work as
  properties) → **FR-030**.

## R3 — Runtime on `main`

[Verified, `targets/js/metano-runtime/src/system/json/`]:

- `TypeDescriptor` is an 11-kind union; `resolveConverter` checks custom converters by `kind` only,
  then an exhaustive switch.
- `serializeWithSpec` / `deserializeWithSpec` use `collectProperties` (base-first) for both
  directions; every `ref`, array element, map value and nullable inner goes through them via
  `refConverter`.
- `SerializerContext.createSpec` registers class-backed specs by constructor, lazily.
- `TypeSpec.base` is evaluated eagerly in the `createSpec({...})` argument, before `??=` assigns
  [Inferred from JS semantics]. Any link from a base to its derived specs must therefore be a thunk,
  or two getters would recurse forever. The P1 prototype passed "derived getter accessed first" with
  thunks [Verified in the design notes, `bun test test/poly`, 8 pass].
- Existing derived-serialization runtime tests use `toEqual`, which ignores key order, and no
  committed generated target contains `base:` [Verified in the design notes] — so FR-009 changes no
  committed output and no passing assertion.

**Decision D2 — data, not a new descriptor kind.** Put `polymorphism?` on `TypeSpec` and dispatch
inside `serializeWithSpec` / `deserializeWithSpec`, which become thin dispatchers over
non-dispatching `serializeContract` / `deserializeContract`. *Rationale*: every entry point already
funnels there (root, `ref`, containers), the 11-kind switch and the `GetWireKind` mapping stay
untouched, the generated context needs no new value import, and the descriptor is plain data that
golden files can diff. *Rejected*: a `polymorphic` `TypeDescriptor` kind (two exhaustive switches to
extend, and dispatch would not apply at the root); a converter object emitted by the generator
(opaque, needs a new value import through `ImportCollector`, invites a general `[JsonConverter]`
hook nobody asked for).

**Decision D3 — derived-first order for every spec** (maintainer decision). Implemented once in
`collectWireProperties` and used for serialization everywhere; `collectProperties` keeps its exported
base-first contract and test. *Rejected*: derived-first only on the polymorphic path (two ordering
rules, and non-polymorphic hierarchies would still diverge from STJ bytes).

**Decision D4 — dispatch index outside the getters.** A `WeakMap<PolymorphismSpec, { byCtor, byTag }>`
built on first use (never inside a getter), `byTag` a `Map` so `1` and `"1"` stay distinct. Variant
`type` is read from `spec().type`, not duplicated in the entry.

## R4 — STJ `JsonTypeInfo` property naming

[Verified, source-gen probe for this spec] Generated context properties for the listed roots:
`ListClip`, `IReadOnlyListClip`, `ClipArray`, `DictionaryStringClip`, `DictionaryInt32String`,
`HashSetString`, `ImmutableArrayClip`, `IEnumerableClip`, `Color`, `String`, `Int32`,
`NullableInt32`, `Double`, `Guid`, and `Names` for
`[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Names")]`. Every registered derived
type also gets a property (`ImageClip`, `SilenceClip`, `IA`, `UFailA`, …) although only the bases
were listed — the closure rule of FR-003. Two roots named `Dup` in different namespaces produce
`warning SYSLIB1031: There are multiple types named 'Dup'. Source was generated for the first one
detected. Use 'JsonSerializableAttribute.TypeInfoPropertyName' to resolve this collision.`

**Decision D5**: one naming function (FR-031) mirroring these names, camel-cased for the getter.
Deliberate divergence: a non-generic type keeps its resolved TS name, so `[Name("Bar")]` on `Foo`
yields `bar` / `listBar` (STJ: `Foo` / `ListFoo`); this preserves every existing getter name.
Collisions mirror `SYSLIB1031` (warning, first wins) **only for unreferenced roots**. STJ can drop
the loser safely because its generated `JsonTypeInfo` for a property is resolved by type, not by
name; Metano's `ref` thunks resolve **by getter name** and are typed `() => TypeSpec`, so a dropped
loser that is still referenced would silently serialize with the winner's contract and still
compile. Hence `MS0036` is an Error in that case, and also when a name hits a `SerializerContext`
member (`options`, `converters`, `createSpec`, `resolve`, `_registry`, and now `createValueSpec`, `readOptions`) → FR-036, DECIDED-14.

**Decision D6 — `ValueSpec` over the full descriptor union** (not a `CollectionSpec` limited to
array/map/hashSet): primitive, enum, branded, temporal, decimal and nullable roots then work with no
extra code, and the root dispatch is a single `kind != object` branch.

## R5 — Branded validating factory

[Verified in the triage, re-read on `main`]: the branded companion already emits `Title.from` with
the full validation body; only the descriptor points at `create`. `brandedConverter` calls
`descriptor.create(value)` unguarded, so pointing `create` at `from` enforces validation with no
runtime change (triage check: `"   "` throws, `"  ok "` → `"ok"`). Private static methods are
emitted (no visibility filter in `IrClassExtractor.IsUserMethod`), so private factories are valid.
The companion declares each static as `IrToTsNamingPolicy.ToFunctionName(method.Name,
method.Attributes)` (`IrToTsBrandedBridge`), so the factory reference MUST be computed by that same
function. `TypeScriptTransformContext.ResolveMethodHelperName` is an extension-method registry
helper that happens to agree today; it is not the rule to share [Verified, `0fa9f4c`]. The
companion also synthesizes `create` and `toString`, so a factory named `Create` would collide →
`MS0034`. Conversion operators are kept by `IsUserMethod` (as `op_Explicit`) and are rejected by
`MS0034` (a JSON factory must be a named static). `BuildBrandedDescriptor` uses the unqualified
`ResolveTsName`; nested branded types need the qualified reference (`SpecTypeReferenceName`).

**Decision D7**: opt-in method-level `[JsonFactory]` (maintainer decision). *Rejected*: a `From`
convention (silently changes how existing data loads); `[JsonConstructor]` (never applies to the
bare-primitive wire shape and points at the non-validating constructor); a type-level
`[Branded(Factory = nameof(From))]` (cannot pick an overload; `[Branded]` has no members).
Overloaded factories are rejected in v1: the TS `from` would be the overload dispatcher, which
selects by the runtime JSON type and could call a different overload than the C# converter.

## R6 — Diagnostics numbering

[Verified, after rebasing on `origin/main` `0fa9f4c`]: `MetaSharpDiagnostic.cs` ends at `MS0031`
(`ExplicitImplementationConflict`, landed with #278 in `d96909c`). Re-checked on `cd14e75`:
`MS0031` is still the last code. The C# unions specification (PR #310) also started at `MS0032`;
the maintainer allocated `MS0032`–`MS0037` to this spec and `MS0038`–`MS0043` to unions
(DECIDED-2). This spec lists its range under "Reserved codes" in the baseline diagnostic catalog;
PR #310 adds its own rows there.

## R7 — Conformance testing approach

**Decision D8**: one shared manifest (`cases.json`, contracts/wire-format.md) checked in once and
verified from both sides. Each row carries id, context, getter, direction (`roundtrip` / `write` /
`read`), the sides it applies to, the expected class, and the JSON text or the error class and
message. Each side builds values through a per-id builder; a missing builder fails. The TUnit test
runs every `stj` row against a real STJ source-generated context; the Bun suite runs every `ts`
row against the generated context, asserting `instanceof` only for read and roundtrip rows.

A single universal assertion `JSON.stringify(serialize(deserialize(JSON.parse(text)))) === text`
was rejected because it cannot hold for every case: W13 (`FallBackToBaseType`) does not read back
through an abstract base; W12 reads back as the ancestor, so `instanceof` fails; E8 is
serialize-only; W16 is TypeScript-only (STJ writes a branded record struct as `{"Value":…}` and
FR-026 ships no converter). Defining values separately on each side would also let them drift.
*Rationale*: no extra CI job, and each side fails independently when it drifts.
*Rejected*: a separate oracle console project run in CI (heavier, same signal); semantic JSON
comparison (would hide the ordering rules this feature exists to honor).

The STJ-side sample builds under `TreatWarningsAsErrors`: cases that produce `SYSLIB1031` (name
collision) or a Metano warning (`MS0033`, warning-form `MS0036`, `MS0037`) stay in inline TUnit
compilations and never enter `samples/SampleJsonContracts`. Biome scans only `targets/js/*/src/**`,
so compact JSON under `test/fixtures/` is not reformatted [Verified].

## R8 — Maintainer-decision probes (interface bases, Web defaults, null roots)

[Verified, source-gen probe for the decision round: .NET SDK 10.0.401, `net10.0`, one
`[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]` context; messages shown without the
`Path: … | LineNumber: …` suffix.]

**Web defaults.** Comparing every public instance property of `new
JsonSerializerOptions(JsonSerializerDefaults.Web)` with `General` shows exactly three wire-relevant
differences: `PropertyNamingPolicy = CamelCase`, `PropertyNameCaseInsensitive = true`,
`NumberHandling = AllowReadingFromString`. (`Converters` and `TypeInfoResolverChain` differ only as
object instances.)

| Case (Web context) | STJ result |
|---|---|
| `{"PATH":"a","seconds":1}` → `ImageClip` | reads `Path = "a"` |
| `{"path":"a","Path":"b","seconds":1}` | `Path = "b"` (last key wins) |
| `{"Kind":"image",…}` through `Clip` | `NotSupportedException … must specify a type discriminator.` (tag case-sensitive) |
| `"2.5"` for a `double` inside a polymorphic payload | reads `2.5` |
| `int` from `"5"`, `"+5"`, `"007"`, `"-5"` | `5`, `5`, `7`, `-5` |
| `int` from `"5.0"`, `" 5"`, `"5 "`, `""`, `"1e2"`, `"0x10"` | `JsonException: The JSON value could not be converted to System.Int32.` |
| `double` from `"1e2"`, `"+1.5"`, `"-0"`, `"NaN"`, `"Infinity"`, `"-Infinity"` | `100`, `1.5`, `-0`, `NaN`, `∞`, `-∞` |
| `double` from `"nan"`, `" 1.5"`, `"1.5 "`, `"0x1"` | `JsonException` |
| `decimal` from `"1.10"` | `1.10` (scale kept) |
| `List<int>` `["1",2]`, `Dictionary<string,int>` `{"k":"3"}` | `[1,2]`, `{"k":3}` |
| `int` root `"5"` | `5` |
| enum from `"1"` | `JsonException: The JSON value could not be converted to Color.` |
| `int?` from `"null"` | `JsonException: … converted to System.Nullable`1[System.Int32].` |
| `List<int>` from `"5"` (string for a collection) | `JsonException` |
| Int tag `"1"` (string) on `[JsonDerivedType(typeof(IA), 1)]` | `JsonException: Read unrecognized type discriminator id '1'.` (number handling does not apply to tags) |
| Writing numbers | still JSON numbers: `{"n":5,"m":7,"d":1.10,"x":2.5,…}` |
| Writing `double.NaN` | `ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. …` |

**Interface bases.**

| Case | STJ result |
|---|---|
| `[JsonDerivedType(typeof(Sq), "sq")] interface IShape`, write `Sq(2)` through `IShape` | `{"$type":"sq","side":2}` |
| Read `{"$type":"sq","side":2}` through `IShape` | `Sq` |
| Read `{"side":2}` through `IShape` | `NotSupportedException: The JSON payload for polymorphic interface or abstract type 'IShape' must specify a type discriminator.` |
| Write unregistered `Other : IShape` | `NotSupportedException: Runtime type 'Other' is not supported by polymorphic type 'IShape'.` |
| `FallBackToBaseType` on `IShapeB { int Area }`, write unregistered `OtherB(3)` (extra member `Extra`) | `{"area":3}` (interface members only) |

**Null roots.**

| Case | STJ result |
|---|---|
| `Deserialize("null")` through abstract polymorphic `Clip` | `null` |
| `Deserialize("null")` through `record struct SPoint` | `JsonException: The JSON value could not be converted to SPoint.` |
| `Serialize<Clip?>(null)` | `null` |

**`[JsonPropertyOrder]`**: `record Ord([property: JsonPropertyOrder(2)] int A,
[property: JsonPropertyOrder(1)] int B)` writes `{"b":2,"a":1}`, so ignoring the attribute would
change the bytes; hence `MS0037` (DECIDED-3).

**Decision D9 — read options live on the context, not on each descriptor.** STJ's
`PropertyNameCaseInsensitive`, `NumberHandling` and `AllowOutOfOrderMetadataProperties` are
context-level options. The generated context declares them once (`readOptions` override), and
`createSpec` / `createValueSpec` attach them to each spec, so the static `JsonSerializer` entry
points, which receive only a spec, still see them. *Rejected*: a flag on every `PropertySpec`
(noisy goldens; the options are not per property); keeping `allowOutOfOrderMetadata` on
`PolymorphismSpec` (two places for one context option). The only per-descriptor addition is
`numberType` on numeric `primitive` descriptors, because the runtime cannot otherwise tell an
`int` from a `double` or a `string` property, and it needs the CLR name for the STJ message.

**Decision D10 — structural write dispatch for `[PlainObject]` variants.** Plain objects have no
constructor, so the base cannot resolve them through `byCtor`. The generator already builds
shape guards for `[PlainObject]` types (`TypeGuardBuilder.GenerateShapeGuard`, without the
`instanceof` fast path); FR-019 synthesizes them for every reached variant, as the unions spec
does for structural cases. Guards are evaluated only for plain objects, ordered most specific
first, and variants that cover each other are rejected at build time. *Rejected*: a runtime
shape matcher driven by `PropertySpec`s (a second, divergent definition of "is this value an X";
the unions spec requires one classification); a stored tag field on `[PlainObject]` values
(changes the TS shape of user types and their wire form outside JSON contexts).

