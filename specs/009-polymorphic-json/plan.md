# Implementation Plan: System.Text.Json-faithful JSON contracts

**Branch**: `spec/008-polymorphic-json` (spec directory `009-polymorphic-json`, DECIDED-1) | **Date**:
2026-10-31 | **Spec**: [spec.md](./spec.md)

**Input**: issues [#268](https://github.com/danfma/metano/issues/268),
[#269](https://github.com/danfma/metano/issues/269), [#270](https://github.com/danfma/metano/issues/270).

## Summary

Mirror System.Text.Json in the generated TypeScript `SerializerContext` for three gaps:

1. **Polymorphism (#268)** — read `[JsonPolymorphic]` / `[JsonDerivedType]` on classes, records,
   interfaces and `[PlainObject]` types, pull derived types into the contract closure, emit a
   data-only `polymorphism` descriptor on the base spec, and dispatch inside the runtime's
   `serializeWithSpec` / `deserializeWithSpec` (tag first, STJ errors, three unknown-type modes,
   ignore flag, out-of-order flag). Reads dispatch on the tag; writes dispatch by constructor for
   class-backed variants and by synthesized guards for `[PlainObject]` variants. Serialize every
   spec most-derived first.
2. **Validating branded factory (#269)** — `[JsonFactory]` on one static method switches the branded
   descriptor from `create: T.create` to `create: T.<factory>`. Compiler-only.
3. **Non-object roots (#270)** — classify roots with the existing property classifier, emit a
   `ValueSpec { descriptor }` for every non-object kind, accept array roots, and name every context
   member with one STJ-compatible function that honors `TypeInfoPropertyName`.

Plus bundled fixes the feature depends on: transpilability of bases / closure members evaluated
against the declaring assembly (FR-010, covers `[assembly: TranspileAssembly]` and referenced
Metano packages, FR-047), closed generic object types in the closure (FR-033), property dedupe for
overrides (FR-009), iterable serialization (FR-038), `JsonException` for the existing
deserialization errors (FR-045). Two cross-cutting STJ parity items complete the set: full
`JsonSerializerDefaults.Web` parity (camelCase, case-insensitive names, numbers from strings,
FR-048) and STJ null-root semantics with `deserialize(…): T | null` (FR-037, **breaking**, FR-049).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (compiler, tests, STJ oracle); TypeScript (runtime, generated
code)

**Primary Dependencies**: Roslyn 5.3.0, System.Text.Json (in-box, .NET 10) for the oracle, TUnit,
Bun + `bun:test`, `tsgo`

**Storage**: N/A (checked-in JSON fixtures)

**Testing**: TUnit (`dotnet run --project tests/Metano.Tests/`), `bun test` in
`targets/js/metano-runtime` and the new conformance sample

**Target Platform**: TypeScript target only; Dart has no JSON context (RT-02)

**Project Type**: compiler target + runtime library

**Performance Goals**: no measurable cost for non-polymorphic specs (one `polymorphism` property
check per object, one `readOptions` check per contract); dispatch index O(1) per class instance after
first use; plain objects run at most one guard per `[PlainObject]` variant

**Constraints**: no BCL-specific IR node types (the JSON context bypasses the IR and stays so); no
new runtime value import in the generated context (only the synthesized `[PlainObject]` guards are
new value imports, FR-041); a breaking release only for the S3b signature change (FR-049); Bun only

**Scale/Scope**: ~1 generator file split into a `Json/` folder, 6 runtime files, 1 attribute, 6
diagnostics, 1 sample, ~110 tests

## Constitution Check

| Principle / Gate | Status | Notes |
|---|---|---|
| I. Clean Code | ✅ | `JsonSerializerContextTransformer` (911 lines) is split by responsibility before growing: closure, naming, polymorphism reader/validator, branded factory resolver, root classifier. |
| II. Expressive code | ✅ | Names follow the STJ vocabulary (`JsonTypeInfo`, `TypeDiscriminator`, `UnknownDerivedTypeHandling`). |
| III. Screaming organization | ✅ | New code under `Transformation/Json/` and `system/json/`; no `Helpers`. |
| IV. Ports & Adapters | ✅ | Everything is TS-target-side (the context already bypasses the IR); `JsonFactoryAttribute` is a target-agnostic annotation. No IR change, no BCL-specific IR node. |
| V. DX — Bun only | ✅ | Bun for every JS step. |
| VI. Pragmatism | ✅ | Data-only descriptor instead of a converter framework; `[JsonPropertyOrder]` and path-tracking deferred. |
| Spec as source of truth | ✅ | This spec precedes implementation; traces to baseline FR-018, FR-032–FR-034. |
| Dual-agent review | ⏳ | `compiler-man` + `bob` on each implementation PR. |
| Build & tests green | ⏳ | Full TUnit suite, runtime + every sample `bun run build && bun test`, `dotnet csharpier check .`, `bunx biome check .`. |
| Worktree per issue | ✅ | One worktree/branch per implementation slice below. |

## Design

### Generator (`src/Metano.Compiler.TypeScript/Transformation/`)

| Unit | Change |
|---|---|
| `Json/JsonContractRoots` | Replaces `ReadSerializableTypes`: returns `JsonContractRoot(ITypeSymbol, TypeInfoPropertyName?, Location)`, accepts rank-1 `IArrayTypeSymbol`, reports `MS0035` (incl. non-transpilable object kinds and rank > 1). |
| `Json/JsonTypeInfoNames` | The single naming function (FR-031), constructed generic types included; replaces `SpecGetterName` / `RefGetterName`; reports `MS0036` with the FR-036 severity rule and the reserved `SerializerContext` member names. |
| `Json/JsonOptionsReader` | Reads `[JsonSourceGenerationOptions]`: the `JsonSerializerDefaults` ctor argument, `PropertyNamingPolicy`, `PropertyNameCaseInsensitive`, `NumberHandling`, `AllowOutOfOrderMetadataProperties` (named options override the Web default); returns the naming policy and `JsonReadOptions`; reports `MS0037` for the FR-046 list. |
| `Json/JsonReadOptionsEmitter` | Emits the `readOptions` getter override (only when non-default) and the `numberType` hint on numeric `primitive` descriptors when numbers are read from strings (FR-048). |
| `Json/JsonStructuralVariants` | For `[PlainObject]` variants (FR-019): requests the synthesized guard from `TypeGuardBuilder`, computes required members and coarse kinds from the same field checks the guard emits, the "covers" relation and `testOrder`; reports `MS0032` for variants that cover each other or a cyclic order. |
| `Json/JsonPolymorphismReader` | `TryRead(INamedTypeSymbol) → JsonPolymorphism?`; own attributes only, matched by metadata name. |
| `Json/JsonPolymorphismValidator` | `MS0032` / `MS0033` rules over the closure (needs final JSON names, so it runs after property collection): assignability (base allowed), abstract derived types, interface-typed entries (OPEN-18), constructed generic bases (FR-015). |
| `Json/JsonContractClosure` | Extracted `CollectContractClosure`; adds `[JsonDerivedType]` targets, the element types of value roots, closed generic object types (FR-033) and referenced Metano-package types (FR-047); records whether each entry is a link target; dedupes by `JsonTypeInfoNames`. |
| `Json/JsonPropertyCollector` | Extracted `CollectSerializableProperties`; dedupes by final JSON name against the base chain (FR-009); reports `MS0037` for `[JsonInclude]` members and `byte[]`. |
| `Json/JsonBrandedFactoryResolver` | Finds and validates `[JsonFactory]`; `MS0034` (incl. operators and `create` / `toString` names); returns the TS member name via `IrToTsNamingPolicy.ToFunctionName`, the function the branded companion declares it with (no parallel rule). |
| `Json/JsonContextImports` | Type-only imports for types used only in `ValueSpec<…>` / `TypeSpec<…>` arguments (FR-041), resolved through `OriginResolver` for cross-package types. |
| `JsonSerializerContextTransformer` | Orchestrates: roots → closure → per entry `TypeSpec` (object kind) or `ValueSpec` (other kinds, via `createValueSpec`); appends `polymorphism`; `valueType: true` for C# value types (FR-037); interface bases without `type:` (FR-018); transpilability evaluated against `type.ContainingAssembly` (FR-010); qualified branded references (`Episode.Title.from`). |
| `TypeGuardBuilder` (existing) | Also emits the guard of a `[PlainObject]` type that a polymorphic spec reaches, without `[GenerateGuard]` (FR-019). The trigger is computed once per compilation from the contract closure, so the variant's own file and the context agree. Same builder and classification the C# unions spec uses for structural cases. |

### Annotations (`src/Metano/Annotations/`)

`JsonFactoryAttribute` (`[AttributeUsage(AttributeTargets.Method)] public sealed class
JsonFactoryAttribute : Attribute;`), matched by name in `SymbolHelper` like `HasBranded`.

### Diagnostics (`src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs`)

`MS0032`–`MS0037` per [contracts/diagnostics.md](./contracts/diagnostics.md).

### Runtime (`targets/js/metano-runtime/src/system/json/`)

| File | Change |
|---|---|
| `types.ts` | `TypeDiscriminator`, `UnknownDerivedTypeHandling`, `DerivedTypeSpec` (with `test?`), `PolymorphismSpec` (with `typeName?`, `testOrder?`), `TypeSpec.polymorphism?` / `valueType?` / `readOptions?`, `ValueSpec`, `JsonTypeInfo`, `JsonReadOptions`, `NumberTypeName`, `PrimitiveDescriptor.numberType?`. |
| `serializer-context.ts` | Protected `readOptions` getter (default `{}`), `createValueSpec`; `createSpec` attaches non-empty read options. |
| `number-from-string.ts` (new) | The FR-048 grammar per `NumberTypeName` (integer ranges, float literals) and the STJ error text. |
| `errors.ts` (new) | `JsonException`, `NotSupportedException`. |
| `polymorphism.ts` (new) | Dispatch index (`WeakMap`: `byCtor`, `byTag`, `structural`), `resolveSerializeTarget` (class instances: `byCtor` **before** the "ctor is base" shortcut, so a self-registered base writes its tag; plain objects: guards in `testOrder`), `resolveDeserializeTarget` (token-name mapping for non-scalar tags; `typeName` for bases without a constructor). Start from the phase 3 prototype (`wf34/runtime-ext/rt/src/system/json/polymorphism.ts`, 8 passing tests). |
| `converters.ts` | `serializeWithSpec` / `deserializeWithSpec` become dispatchers over non-dispatching `serializeContract` / `deserializeContract`; tag inserted first; `collectWireProperties` used for serialization; depth counter on both sides (FR-016); root null handling (FR-037); case-insensitive key matching and numbers from strings driven by `readOptions` (FR-048); `JsonException` for the existing errors (FR-045); `arrayConverter` / hash-set serialize via `Array.from` (FR-038); `resolveConverter` exported internally for `ValueSpec`. |
| `json-serializer.ts` | `TypeSpec` overloads widened to `T \| null` (**breaking**, FR-034); `ValueSpec` overloads. |
| `index.ts` | Re-export the new types, classes and `collectWireProperties`. |

### Emitted TypeScript

See [contracts/context-codegen.md](./contracts/context-codegen.md); wire text in
[contracts/wire-format.md](./contracts/wire-format.md).

## Project Structure

### Documentation (this feature)

```text
specs/009-polymorphic-json/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── context-codegen.md
│   ├── wire-format.md
│   ├── runtime-api.md
│   └── diagnostics.md
└── checklists/
    └── requirements.md
```

`tasks.md` is generated by `/speckit-tasks` after the open decisions are settled.

### Source Code (repository root)

```text
src/Metano/Annotations/JsonFactoryAttribute.cs                       # NEW
src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs               # MS0032–MS0037
src/Metano.Compiler/SymbolHelper.cs                                  # HasJsonFactory / FindJsonFactory
src/Metano.Compiler.TypeScript/Transformation/JsonSerializerContextTransformer.cs
src/Metano.Compiler.TypeScript/Transformation/Json/                  # NEW folder (units above)
targets/js/metano-runtime/src/system/json/{types,converters,json-serializer,index}.ts
targets/js/metano-runtime/src/system/json/{errors,polymorphism,number-from-string}.ts  # NEW
targets/js/metano-runtime/test/system/json/{polymorphism,value-spec,wire-order,errors,read-options,structural-dispatch}.test.ts  # NEW
samples/SampleJsonContracts/                                         # NEW (DECIDED-11)
targets/js/sample-json-contracts/                                    # NEW generated + bun tests
targets/js/sample-json-contracts/test/fixtures/stj/cases.json        # NEW shared conformance manifest
targets/js/sample-json-contracts/test/builders.ts                    # NEW per-id TS builders
tests/Metano.Tests/Json/                                             # NEW TUnit tests (see below)
tests/Metano.Tests/Expected/json-polymorphic-context.ts              # NEW golden
tests/Metano.Tests/Expected/json-value-roots-context.ts              # NEW golden
tests/Metano.Tests/Expected/json-branded-factory-context.ts          # NEW golden
tests/Metano.Tests/Expected/json-closure-generic-context.ts          # NEW golden (Feed / Page<T>)
tests/Metano.Tests/Expected/json-interface-context.ts                # NEW golden (interface base)
tests/Metano.Tests/Expected/json-plain-object-context.ts             # NEW golden ([PlainObject] variants)
tests/Metano.Tests/Expected/json-web-context.ts                      # NEW golden (Web read options)
docs/serialization.md, docs/attributes.md                            # FR-043
specs/001-project-baseline-evolution/baseline/{attribute-catalog,diagnostic-catalog,feature-support-matrix}.md
```

## Implementation slices (one PR each, rebased on `origin/main` right before merge)

| # | Slice | Issue | Commit type | Depends on |
|---|---|---|---|---|
| S1 | Declaring-assembly transpilability for bases and closure (FR-010, FR-047) + closed generic object types in the closure (FR-033) + property dedupe (FR-009 generator half) | #268 #270 | `fix:` | — |
| S2 | Web-defaults naming policy (FR-048 item 1) | #268 | `fix:` | — |
| S2b | Context read options: case-insensitive names, numbers from strings, `numberType` hints (FR-048 items 2–5, FR-002 plumbing) | #268 #270 | `feat:` | S2 |
| S3 | Naming function + roots + `ValueSpec` runtime (FR-030–FR-036, FR-038 typing, `MS0035`, `MS0036`) | #270 | `feat:` | — |
| S3b | Null-root parity: `T \| null` overloads, `valueType` flags, migration note (FR-034 signatures, FR-037, FR-049). May be held to share one major with the C# unions spec | #270 | `feat!:` (`BREAKING CHANGE:` footer) | S3 |
| S4 | Derived-first wire order (`collectWireProperties`, FR-009), `Array.from` iterables (FR-038), `JsonException` for existing errors (FR-045), depth limit (FR-016) | #268 #270 | `fix:` | — |
| S5 | Runtime polymorphism (types, errors, dispatch) with bun tests | #268 | `feat:` | S4 |
| S6 | Generator polymorphism for class hierarchies (reader, closure, descriptor, constructed generic bases, `MS0032`/`MS0033`) + golden; first probe DECIDED-12 | #268 | `feat:` | S1, S3, S5 |
| S6c | Interface bases (FR-018) and `[PlainObject]` variants with synthesized guards, `testOrder` and the indistinguishability rule (FR-019) + goldens | #268 | `feat:` | S6 |
| S6b | `MS0037` unmirrored options / `[JsonInclude]` / `byte[]` / `[JsonPropertyOrder]` (FR-046) | #268 #270 | `feat:` | S3 |
| S7 | `[JsonFactory]` (FR-020–FR-026, `MS0034`) + golden | #269 | `feat:` | — |
| S8 | Conformance sample + STJ fixtures on both sides + CI step (includes Web, interface, `[PlainObject]` and null-root rows; pins the read-depth message, DECIDED-15) | #268 #269 #270 | `test:` | S2b, S3b, S6c, S7 |
| S9 | Docs and baseline catalogs/matrix (FR-043) | all | `docs:` | S8 |

S3 lands before S6 so polymorphic collection roots (`List<Clip>`) and derived getters use the final
names from the start. S7 is independent and can ship first. S3b is the only breaking slice; the
maintainer decides when it merges (alone, or in the same major as the C# unions breaking changes,
PR #310). Slices merged before it keep `deserialize(…): T` for `TypeSpec`, so no other slice is
blocked by that decision. Each slice gets its own worktree and
branch from `origin/main`.

## Test strategy

### TUnit (`tests/Metano.Tests/Json/`)

- `JsonPolymorphismReaderTests`: defaults (`$type`, `FailSerialization`), string/int/mixed tags,
  untagged, self-registered base, enum mapping of `UnknownDerivedTypeHandling`, `Inherited = false`
  (attribute on a base's base is not read), `AllowOutOfOrderMetadataProperties` on the context.
- `JsonPolymorphicContextTests`: golden `json-polymorphic-context.ts`; closure adds unlisted derived
  types and their bases; nested variants qualified (`Shape.Circle`); self-registered base entry;
  closed generic base `gBaseInt32` (links and getter use the constructed name); overridden property
  emitted once; `base:` under `TranspileAssembly` (S1 regression); `tsgo --strict` compile of the
  golden in the sample.
- `JsonCrossPackageContextTests` (`TranspileWithLibrary`): base and variants in the library with the
  context in the consumer; a consumer type deriving from a library base (base link and inherited
  properties present); a consumer property typed with a library record. Each asserts the import
  lines and the `package.json` dependency.
- `JsonClosureGenericTests`: golden for `Feed { Page<EpisodeSummary> }` (getter
  `pageEpisodeSummary`, `ref` resolves) compiled with `tsgo --strict`.
- `JsonTypeInfoNamesTests`: every row of the data-model naming table; `TypeInfoPropertyName`
  applies to refs as well as the root; `[Name]` divergence; non-generic names unchanged (regression
  for all existing JSON tests).
- `JsonValueRootTests`: one test per root shape (List, `T[]`, IReadOnlyList, IEnumerable, IList,
  ICollection, ImmutableArray, Dictionary, IReadOnlyDictionary, HashSet, string, int, `int?`, Guid,
  DateTime, decimal, `[StringEnum]`, numeric enum, `[Branded]`) asserting getter, `ValueSpec<…>`
  type and descriptor (`| null` for reference types); golden `json-value-roots-context.ts`, which
  asserts the type-only import lines for `HashSet`, `Temporal`, `Decimal`, `UUID` and one
  cross-package element type and compiles under `tsgo --strict`; no `new List(` /
  `new IReadOnlyList(` in any output.
- `JsonBrandedFactoryTests`: the triage cases (factory used, no factory unchanged, `[Name]`,
  private, cross-package via `TranspileWithLibrary`, nested in nullable/array/map) plus map key,
  nested branded type (qualified reference) and the naming-function parity check (descriptor name
  equals the companion's declared name) + golden `json-branded-factory-context.ts`.
- `JsonStructuralPolymorphismTests`: interface base golden `json-interface-context.ts` (no
  `type:`, `typeName`, no `base:` on class variants); `[PlainObject]` golden
  `json-plain-object-context.ts` (guards synthesized in the variant files without
  `[GenerateGuard]`, value imports of the guards, `testOrder` only when needed, mixed class and
  `[PlainObject]` variants), both compiled under `tsgo --strict`.
- `JsonReadOptionsTests`: Web context emits camelCase names, the `readOptions` override and
  `numberType` hints; `PropertyNameCaseInsensitive = false` overrides Web; a default context is
  byte-identical (no override, no hints).
- `JsonDiagnosticsTests`: one test per rule in [contracts/diagnostics.md](./contracts/diagnostics.md)
  (including abstract derived type, closed generic base mismatch, non-transpilable object root,
  rank > 1, reserved member names, `MS0036` Error vs Warning, operator / `create` factory, every
  `MS0037` rule), asserting code, severity and location, and that no invalid `.ts` is emitted
  instead. All warning cases are inline compilations.
- `StjConformanceFixtureTests`: references `samples/SampleJsonContracts`, declares a real STJ
  source-gen context over the same types (test-only partial class), loads
  `targets/js/sample-json-contracts/test/fixtures/stj/cases.json`, and for every row whose `sides`
  include `stj` runs it per its `direction` through the `StjCaseBuilders` entry of that id
  (contracts/wire-format.md). Fails when a row has no builder or a builder has no row. Setting
  `METANO_UPDATE_STJ_FIXTURES=1` rewrites the `json` / `stjJson` text of the rows (local only; CI
  never sets it).

### Bun

- `metano-runtime/test/system/json/polymorphism.test.ts`: the 8 prototype tests plus int/mixed tags,
  untagged variant, self-registered base (write and read), the three unknown-type modes (nearest
  with and without a registered ancestor, abstract entry as nearest ancestor), ignore flag on
  concrete vs abstract base, out-of-order tag with and without the flag, null / boolean / object /
  array tags (token names), intermediate static type, nested polymorphism, both getter access
  orders, error classes and messages, depth limit on a self-containing value.
- `wire-order.test.ts`: `collectWireProperties` order; `JSON.stringify` key order for a 3-level
  hierarchy; `collectProperties` unchanged.
- `value-spec.test.ts`: array/map/hashSet/primitive/nullable round-trip, `instanceof` elements,
  custom converter by kind applies at the root, null root per FR-037 (`listClip` and `clip` return
  `null`, `int32` and a struct spec throw, `serialize(null, clip)` returns `null`), a `Set` and a
  generator serialized through an `Iterable` root and through an `Iterable` property (FR-038), and
  the `TypeSpec` overloads typed `T | null` (checked by `bun run build` with a `@ts-expect-error`
  assignment to `T`).
- `read-options.test.ts`: case-insensitive matching (last key wins, tag stays case-sensitive),
  every accepted and rejected number string of FR-048 per `NumberTypeName`, number strings inside
  arrays / map values / roots, enums and `Nullable` `"null"` rejected, writes unchanged, specs of a
  default context carry no `readOptions`.
- `structural-dispatch.test.ts`: `[PlainObject]` variants selected by guard in `testOrder`, class
  instances never reach guards, plain objects never match a constructor, unmatched plain object
  under each handling mode, concrete `[PlainObject]` base fallback, interface base without `type`.
- `errors.test.ts`: non-object input and missing property throw `JsonException` (FR-045).
- Branded: a descriptor whose `create` throws makes `deserialize` throw at every nesting level.
- `sample-json-contracts/test/stj-conformance.test.ts`: loads `cases.json` and runs every row whose
  `sides` include `ts` per its `direction` through `builders.ts`; byte equality for write /
  roundtrip rows, `instanceof` only for read / roundtrip rows, class and message prefix for error
  rows. Fails when a row has no builder or a builder has no row.

### Regression

Full TUnit suite; `bun run build && bun test` for the runtime and every sample; the CI "Verify
samples are regenerated cleanly" step must show zero diff in existing targets. Verified on `main`:
the three committed contexts (`sample-todo`, `sample-todo-service`, `sample-issue-tracker`) contain
no `base:` link and no generic root.

## CI changes

- `.github/workflows/ci.yml`, `js` job: add `cd targets/js/sample-json-contracts && bun run build &&
  bun test` after `sample-issue-tracker`.
- No new job: the `.NET` job already builds the solution (so the new sample is transpiled and the
  regeneration check covers it) and runs TUnit (so the STJ side of the fixtures is checked).
- Add `samples/SampleJsonContracts` to `Metano.slnx`. The Bun workspace already globs
  `targets/js/*`, so the new target joins it with no root `package.json` change.
- The sample builds under `TreatWarningsAsErrors` (both the STJ source generator and the Metano
  transpile): it contains no `SYSLIB1031` collision and no case that raises a Metano warning
  (`MS0033`, warning-form `MS0036`, `MS0037`); those live in inline TUnit compilations only.
- Biome scans only `targets/js/*/src/**`, so `test/fixtures/stj/cases.json` is not reformatted.
- `release.yml`: unchanged (the runtime is versioned with the release as today).

## Compatibility and migration

| Change | Who is affected | Migration |
|---|---|---|
| Derived-first key order (FR-009) | Code comparing serialized JSON text of hierarchies | None for semantic consumers; text now equals STJ. |
| `base:` under `TranspileAssembly` and for library bases (FR-010, FR-047) | `TranspileAssembly` projects and consumers of Metano packages with JSON hierarchies | Inherited properties now appear on the wire — the previous output silently lost data. Changelog entry as a fix. |
| Closed generic object types reached through properties (FR-033) | Contexts with such properties (did not compile: TS2339) | None; the missing getter is now emitted. |
| Overridden property dedupe (FR-009) | Hierarchies that override a serialized property | None for semantic consumers; the base `PropertySpec` list no longer repeats the derived one. |
| `JsonException` for existing errors (FR-045) | Code matching the message "Expected object for deserialization" | Catch `JsonException` or `Error`. |
| `MS0036` Error for referenced collisions | Contexts whose nested types share a simple name (`Clip.Image`, `Anchor.Image`) | Add `TypeInfoPropertyName` (previously silently wrong contract). |
| `MS0037` warnings | Contexts setting unmirrored options | Informational; remove the option or accept the divergence. |
| Generic root getter renames (DECIDED-7) | Contexts with generic roots (did not compile for collections) | Rename accessors (`ctx.list` → `ctx.listEpisodeSummary`) or set `TypeInfoPropertyName`. |
| `MS0036` on same-name types | Contexts that silently merged two `Dup` types | Add `TypeInfoPropertyName`. |
| Web defaults (FR-048) | Contexts declared with `JsonSerializerDefaults.Web` | Wire names become camelCase, matching .NET; reads become case-insensitive and accept numbers as strings, as .NET does. |
| **`deserialize(…): T \| null` through `TypeSpec` (FR-034, FR-037) — BREAKING** | Every strict TS caller that assigns the result to `T`, including this repository's hand-written sample tests | Handle `null` or assert with `!`; see spec.md "Migration". Released as a major version (FR-049). |
| JSON `null` at an object root returns `null` instead of throwing (FR-037) | Code that relied on the throw | Check for `null`. Struct contracts still throw. |
| Synthesized guards for `[PlainObject]` variants (FR-019) | Files of `[PlainObject]` types reached by a polymorphic spec | They gain an exported `is<Name>` guard; no existing export changes. |
| Runtime additions | Generated packages using polymorphism or value specs | Require the `metano-runtime` release that ships S3/S5. `PackageJsonWriter` writes no `metano-runtime` dependency (user-managed), so the minimum version is stated in `docs/serialization.md` and the changelog (DECIDED-17); the major version of FR-049 makes the upgrade explicit. |
| `[JsonFactory]` | Opt-in | None. |

## Risks

- **Constructor-identity dispatch** fails for spread/`structuredClone` copies and duplicated package
  instances (fail-fast under the default handling; documented).
- **Custom converters for `ref`** bypass dispatch (documented; unchanged rule).
- **Unmeasured STJ corners** (interface diamonds, abstract-entry tag on read, read depth) — guarded
  by `MS0032` or probed in a named slice (OPEN-18, DECIDED-12 in S6, DECIDED-15 in S8).
- **Structural dispatch** depends on the guard checking at least the required members the
  generator's analysis assumes; `JsonStructuralVariants` derives both from the same field checks to
  keep them aligned (FR-019), and OPEN-19 tracks the difference from the unions overlap rule.
- **Breaking release timing**: S3b may wait for the unions major; until it merges, null roots keep
  today's behavior.
- **Runtime version skew** — an older `metano-runtime` behind a non-type-checking bundler ignores
  `polymorphism` and writes no tag (DECIDED-17).
- **Fixture drift across .NET versions** — intentional signal; regenerate deliberately with the
  update switch and review the diff.
- **Conflicts**: other PRs merge in parallel; `JsonSerializerContextTransformer.cs` and
  `MetaSharpDiagnostic.cs` are hot files — rebase each slice on `origin/main` right before merging
  and re-check the diagnostic numbers (DECIDED-2: `MS0032`–`MS0037` here, `MS0038`–`MS0043` for
  the C# unions spec).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Six diagnostic codes | Errors vs warnings for distinct concerns, each documented separately in the catalog | One code with mixed severities hides which rules fail the build |
| Checked-in fixtures verified from two runtimes | Byte-identical parity is the feature's promise | Semantic comparison would hide the ordering rules; a separate oracle job duplicates the TUnit signal |
