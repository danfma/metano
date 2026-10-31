# Research: System.Text.Json-faithful JSON contracts

Evidence tags: **[Verified]** = reproduced by running code (probe or test) or by reading the cited
source on `main` at `7ff7544`; **[Inferred]** = reasoned from code or docs, not executed.

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

**Decision D1**: Mirror every row except duplicate-tag detection and exponent/negative-zero
number text (not observable after `JSON.parse` / not reproducible by `JSON.stringify`).

**Gaps not measured**: nested polymorphism (a derived type that has its own `[JsonDerivedType]`s),
`[JsonDerivedType]` on interfaces with diamonds, `UnknownDerivedTypeHandling` on interfaces. These
are the subject of OPEN-8 and OPEN-9.

## R2 — Where the generator stands on `main` (post #238 / #250)

[Verified, `src/Metano.Compiler.TypeScript/Transformation/JsonSerializerContextTransformer.cs`]:

- The context bypasses the IR: `TypeTransformer` hands `JsonSerializerContext` subclasses straight to
  the transformer, which reads Roslyn symbols.
- `ReadSerializableTypes` keeps only `ConstructorArguments[0].Value is INamedTypeSymbol` — array
  roots are dropped — and ignores `TypeInfoPropertyName`.
- `CollectContractClosure` (#250) walks roots, then properties (through nullable / array / map /
  hash-set) and the `base` chain; it discovers only non-generic, current-assembly, transpilable
  classes/structs. Derived types named by `[JsonDerivedType]` are not discovered.
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
- `ReadNamingPolicy` reads only the named `PropertyNamingPolicy` argument → OPEN-4.
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
Collisions mirror `SYSLIB1031` (warning, first wins) → `MS0036`.

**Decision D6 — `ValueSpec` over the full descriptor union** (not a `CollectionSpec` limited to
array/map/hashSet): primitive, enum, branded, temporal, decimal and nullable roots then work with no
extra code, and the root dispatch is a single `kind != object` branch.

## R5 — Branded validating factory

[Verified in the triage, re-read on `main`]: the branded companion already emits `Title.from` with
the full validation body; only the descriptor points at `create`. `brandedConverter` calls
`descriptor.create(value)` unguarded, so pointing `create` at `from` enforces validation with no
runtime change (triage check: `"   "` throws, `"  ok "` → `"ok"`). Private static methods are
emitted (no visibility filter in `IrClassExtractor.IsUserMethod`), so private factories are valid.
`TypeScriptTransformContext.ResolveMethodHelperName` (private today) already computes
`[Name] ?? camelCase` for static helpers; it is the single naming rule to share (no third copy).

**Decision D7**: opt-in method-level `[JsonFactory]` (maintainer decision). *Rejected*: a `From`
convention (silently changes how existing data loads); `[JsonConstructor]` (never applies to the
bare-primitive wire shape and points at the non-validating constructor); a type-level
`[Branded(Factory = nameof(From))]` (cannot pick an overload; `[Branded]` has no members).
Overloaded factories are rejected in v1: the TS `from` would be the overload dispatcher, which
selects by the runtime JSON type and could call a different overload than the C# converter.

## R6 — Diagnostics numbering

[Verified]: `MetaSharpDiagnostic.cs` ends at `MS0030` on `main`; `MS0031` is introduced by the
in-flight `fix/278-explicit-interface-members` (commit `ad80ef5`). No branch on `origin` mentions
`MS0032`+ or a `specs/009*` directory. → reserve `MS0032`–`MS0036` (OPEN-2).

## R7 — Conformance testing approach

**Decision D8**: fixture files checked in once, verified from both sides. A TUnit test builds a real
STJ source-generated context over the conformance sample's C# types and asserts that serializing
each case equals the fixture bytes and that each error case throws the recorded exception type and
message prefix. The Bun suite of the generated sample reads the same files and asserts
`JSON.stringify(serialize(deserialize(JSON.parse(text)))) === text`, `instanceof`, and the error
class/message. *Rationale*: no extra CI job, and each side fails independently when it drifts.
*Rejected*: a separate oracle console project run in CI (heavier, same signal); semantic JSON
comparison (would hide the ordering rules this feature exists to honor).
