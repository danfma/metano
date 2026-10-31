# Implementation Plan: System.Text.Json-faithful JSON contracts

**Branch**: `spec/008-polymorphic-json` (spec directory `009-polymorphic-json`, OPEN-1) | **Date**:
2026-10-31 | **Spec**: [spec.md](./spec.md)

**Input**: issues [#268](https://github.com/danfma/metano/issues/268),
[#269](https://github.com/danfma/metano/issues/269), [#270](https://github.com/danfma/metano/issues/270).

## Summary

Mirror System.Text.Json in the generated TypeScript `SerializerContext` for three gaps:

1. **Polymorphism (#268)** — read `[JsonPolymorphic]` / `[JsonDerivedType]`, pull derived types into
   the contract closure, emit a data-only `polymorphism` descriptor on the base spec, and dispatch
   inside the runtime's `serializeWithSpec` / `deserializeWithSpec` (tag first, STJ errors, three
   unknown-type modes, ignore flag, out-of-order flag). Serialize every spec most-derived first.
2. **Validating branded factory (#269)** — `[JsonFactory]` on one static method switches the branded
   descriptor from `create: T.create` to `create: T.<factory>`. Compiler-only.
3. **Non-object roots (#270)** — classify roots with the existing property classifier, emit a
   `ValueSpec { descriptor }` for every non-object kind, accept array roots, and name every context
   member with one STJ-compatible function that honors `TypeInfoPropertyName`.

Plus two bundled fixes the feature depends on: the `base:` link under `[assembly: TranspileAssembly]`
(FR-010) and, if OPEN-4 is accepted, the `JsonSerializerDefaults.Web` naming policy.

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
check per object); dispatch index O(1) per value after first use

**Constraints**: no BCL-specific IR node types (the JSON context bypasses the IR and stays so); no
new value import in the generated context; Bun only

**Scale/Scope**: ~1 generator file split into a `Json/` folder, 4 runtime files, 1 attribute, 5
diagnostics, 1 sample, ~60 tests

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
| `Json/JsonContractRoots` | Replaces `ReadSerializableTypes`: returns `JsonContractRoot(ITypeSymbol, TypeInfoPropertyName?, Location)`, accepts `IArrayTypeSymbol`, reports `MS0035`. |
| `Json/JsonTypeInfoNames` | The single naming function (FR-031); replaces `SpecGetterName` / `RefGetterName`; reports `MS0036`. |
| `Json/JsonPolymorphismReader` | `TryRead(INamedTypeSymbol) → JsonPolymorphism?`; own attributes only, matched by metadata name. |
| `Json/JsonPolymorphismValidator` | `MS0032` / `MS0033` rules over the closure (needs final JSON names, so it runs after property collection). |
| `Json/JsonContractClosure` | Extracted `CollectContractClosure`; adds `[JsonDerivedType]` targets and the element types of value roots; dedupes by `JsonTypeInfoNames`. |
| `Json/JsonBrandedFactoryResolver` | Finds and validates `[JsonFactory]`; `MS0034`; returns the TS member name via the shared `[Name] ?? camelCase` rule (promote `TypeScriptTransformContext.ResolveMethodHelperName` to a shared internal static, do not copy it). |
| `JsonSerializerContextTransformer` | Orchestrates: roots → closure → per entry `TypeSpec` (object kind) or `ValueSpec` (other kinds); appends `polymorphism`; `TranspilableBaseOf` passes `AssemblyWideTranspile` + `CurrentAssembly` (FR-010); `ReadNamingPolicy` honors `JsonSerializerDefaults.Web` (OPEN-4). |

### Annotations (`src/Metano/Annotations/`)

`JsonFactoryAttribute` (`[AttributeUsage(AttributeTargets.Method)] public sealed class
JsonFactoryAttribute : Attribute;`), matched by name in `SymbolHelper` like `HasBranded`.

### Diagnostics (`src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs`)

`MS0032`–`MS0036` per [contracts/diagnostics.md](./contracts/diagnostics.md).

### Runtime (`targets/js/metano-runtime/src/system/json/`)

| File | Change |
|---|---|
| `types.ts` | `TypeDiscriminator`, `UnknownDerivedTypeHandling`, `DerivedTypeSpec`, `PolymorphismSpec`, `TypeSpec.polymorphism?`, `ValueSpec`, `JsonTypeInfo`. |
| `errors.ts` (new) | `JsonException`, `NotSupportedException`. |
| `polymorphism.ts` (new) | Dispatch index (`WeakMap`), `resolveSerializeTarget`, `resolveDeserializeTarget`. Start from the phase 3 prototype (`wf34/runtime-ext/rt/src/system/json/polymorphism.ts`, 8 passing tests). |
| `converters.ts` | `serializeWithSpec` / `deserializeWithSpec` become dispatchers over non-dispatching `serializeContract` / `deserializeContract`; tag inserted first; `collectWireProperties` used for serialization; `resolveConverter` exported internally for `ValueSpec`. |
| `json-serializer.ts` | `ValueSpec` overloads; `JsonTypeInfo` on `deserialize`. |
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
src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs               # MS0032–MS0036
src/Metano.Compiler/SymbolHelper.cs                                  # HasJsonFactory / FindJsonFactory
src/Metano.Compiler.TypeScript/Transformation/JsonSerializerContextTransformer.cs
src/Metano.Compiler.TypeScript/Transformation/Json/                  # NEW folder (units above)
targets/js/metano-runtime/src/system/json/{types,converters,json-serializer,index}.ts
targets/js/metano-runtime/src/system/json/{errors,polymorphism}.ts  # NEW
targets/js/metano-runtime/test/system/json/{polymorphism,value-spec,wire-order}.test.ts  # NEW
samples/SampleJsonContracts/                                         # NEW (OPEN-11)
targets/js/sample-json-contracts/                                    # NEW generated + bun tests
targets/js/sample-json-contracts/test/fixtures/stj/*.json            # NEW conformance fixtures
tests/Metano.Tests/Json/                                             # NEW TUnit tests (see below)
tests/Metano.Tests/Expected/json-polymorphic-context.ts              # NEW golden
tests/Metano.Tests/Expected/json-value-roots-context.ts              # NEW golden
tests/Metano.Tests/Expected/json-branded-factory-context.ts          # NEW golden
docs/serialization.md, docs/attributes.md                            # FR-043
specs/001-project-baseline-evolution/baseline/{attribute-catalog,diagnostic-catalog,feature-support-matrix}.md
```

## Implementation slices (one PR each, rebased on `origin/main` right before merge)

| # | Slice | Issue | Commit type | Depends on |
|---|---|---|---|---|
| S1 | `base:` link under `TranspileAssembly` (FR-010) | #268 | `fix:` | — |
| S2 | Web-defaults naming policy (OPEN-4, if accepted) | #268 | `fix:` | — |
| S3 | Naming function + roots + `ValueSpec` runtime (FR-030–FR-037, `MS0035`, `MS0036`) | #270 | `feat:` | — |
| S4 | Derived-first wire order (`collectWireProperties`, FR-009) | #268 | `fix:` | — |
| S5 | Runtime polymorphism (types, errors, dispatch) with bun tests | #268 | `feat:` | S4 |
| S6 | Generator polymorphism (reader, closure, descriptor, `MS0032`/`MS0033`) + golden | #268 | `feat:` | S1, S3, S5 |
| S7 | `[JsonFactory]` (FR-020–FR-026, `MS0034`) + golden | #269 | `feat:` | — |
| S8 | Conformance sample + STJ fixtures on both sides + CI step | #268 #269 #270 | `test:` | S3, S6, S7 |
| S9 | Docs and baseline catalogs/matrix (FR-043) | all | `docs:` | S8 |

S3 lands before S6 so polymorphic collection roots (`List<Clip>`) and derived getters use the final
names from the start. S7 is independent and can ship first.

## Test strategy

### TUnit (`tests/Metano.Tests/Json/`)

- `JsonPolymorphismReaderTests`: defaults (`$type`, `FailSerialization`), string/int/mixed tags,
  untagged, enum mapping of `UnknownDerivedTypeHandling`, `Inherited = false` (attribute on a base's
  base is not read), `AllowOutOfOrderMetadataProperties` on the context.
- `JsonPolymorphicContextTests`: golden `json-polymorphic-context.ts`; closure adds unlisted derived
  types and their bases; nested variants qualified (`Shape.Circle`); `base:` under
  `TranspileAssembly` (S1 regression); `tsgo --strict` compile of the golden in the sample.
- `JsonTypeInfoNamesTests`: every row of the data-model naming table; `TypeInfoPropertyName`
  applies to refs as well as the root; `[Name]` divergence; non-generic names unchanged (regression
  for all existing JSON tests).
- `JsonValueRootTests`: one test per root shape (List, `T[]`, IReadOnlyList, IEnumerable, IList,
  ICollection, ImmutableArray, Dictionary, IReadOnlyDictionary, HashSet, string, int, `int?`, Guid,
  DateTime, decimal, `[StringEnum]`, numeric enum, `[Branded]`) asserting getter, `ValueSpec<…>`
  type and descriptor; golden `json-value-roots-context.ts`; no `new List(` / `new IReadOnlyList(`
  in any output.
- `JsonBrandedFactoryTests`: the six triage cases (factory used, no factory unchanged, `[Name]`,
  private, cross-package via `TranspileWithLibrary`, nested in nullable/array/map) + golden
  `json-branded-factory-context.ts`.
- `JsonDiagnosticsTests`: one test per rule in [contracts/diagnostics.md](./contracts/diagnostics.md),
  asserting code, severity and location, and that no invalid `.ts` is emitted instead.
- `StjConformanceFixtureTests`: references `samples/SampleJsonContracts`, declares a real STJ
  source-gen context over the same types (test-only partial class), serializes every valid case and
  asserts byte equality with `targets/js/sample-json-contracts/test/fixtures/stj/<case>.json`;
  runs every error case and asserts the exception type and message prefix recorded in
  `errors.json`. Setting `METANO_UPDATE_STJ_FIXTURES=1` rewrites the fixtures (local only; CI never
  sets it).

### Bun

- `metano-runtime/test/system/json/polymorphism.test.ts`: the 8 prototype tests plus int/mixed tags,
  untagged variant, the three unknown-type modes (nearest with and without a registered ancestor),
  ignore flag on concrete vs abstract base, out-of-order tag with and without the flag, null tag,
  intermediate static type, both getter access orders, error classes and messages.
- `wire-order.test.ts`: `collectWireProperties` order; `JSON.stringify` key order for a 3-level
  hierarchy; `collectProperties` unchanged.
- `value-spec.test.ts`: array/map/hashSet/primitive/nullable round-trip, `instanceof` elements,
  custom converter by kind applies at the root, null root per OPEN-6, the `TypeSpec` overload still
  returns `Record<string, unknown>` (checked by `bun run build`).
- Branded: a descriptor whose `create` throws makes `deserialize` throw at every nesting level.
- `sample-json-contracts/test/stj-conformance.test.ts`: reads every fixture and asserts
  `JSON.stringify(serialize(deserialize(JSON.parse(text)))) === text`, `instanceof`, and the error
  table.

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
- `release.yml`: unchanged (the runtime is versioned with the release as today).

## Compatibility and migration

| Change | Who is affected | Migration |
|---|---|---|
| Derived-first key order (FR-009) | Code comparing serialized JSON text of hierarchies | None for semantic consumers; text now equals STJ. |
| `base:` under `TranspileAssembly` (FR-010) | `TranspileAssembly` projects with JSON hierarchies | Inherited properties now appear on the wire — the previous output silently lost data. Changelog entry as a fix. |
| Generic root getter renames (OPEN-7) | Contexts with generic roots (did not compile for collections) | Rename accessors (`ctx.list` → `ctx.listEpisodeSummary`) or set `TypeInfoPropertyName`. |
| `MS0036` on same-name types | Contexts that silently merged two `Dup` types | Add `TypeInfoPropertyName`. |
| Web defaults (OPEN-4) | Contexts declared with `JsonSerializerDefaults.Web` | Wire names become camelCase, matching .NET. |
| Runtime additions | Generated packages using polymorphism or value specs | Require the `metano-runtime` release that ships S3/S5; verify how `PackageJsonWriter` pins the runtime range before release. |
| `[JsonFactory]` | Opt-in | None. |

## Risks

- **Constructor-identity dispatch** fails for spread/`structuredClone` copies and duplicated package
  instances (fail-fast under the default handling; documented).
- **Custom converters for `ref`** bypass dispatch (documented; unchanged rule).
- **Unmeasured STJ corners** (nested polymorphism, interface diamonds) — guarded by `MS0032`/`MS0033`
  until measured.
- **Fixture drift across .NET versions** — intentional signal; regenerate deliberately with the
  update switch and review the diff.
- **Conflicts**: other PRs merge in parallel; `JsonSerializerContextTransformer.cs` and
  `MetaSharpDiagnostic.cs` are hot files — rebase each slice on `origin/main` right before merging
  and re-check the diagnostic numbers (OPEN-2).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Five diagnostic codes | Errors vs warnings for distinct concerns, each documented separately in the catalog | One code with mixed severities hides which rules fail the build |
| Checked-in fixtures verified from two runtimes | Byte-identical parity is the feature's promise | Semantic comparison would hide the ordering rules; a separate oracle job duplicates the TUnit signal |
