# Implementation Plan: BCL collections as runtime types, runtime type descriptors

**Branch**: `spec/011-runtime-collections-and-types` | **Date**: 2026-11-01 | **Spec**: [spec.md](./spec.md) | **Issue**: [#335](https://github.com/danfma/metano/issues/335)

## Summary

Metano 3.0.0 makes the BCL collections `metano-runtime` classes (D1), introduces `JsArray<T>` for the native array (D2), makes BCL exceptions runtime classes with a typed-catch lowering (D10), replaces IIFEs with methods and helpers (D4), and adds runtime type descriptors consumed by `is` / `as` / patterns / `catch` / `GetType()` / `typeof` / JSON (D5–D9, D14). Three interop features complete it: `dynamic` as `any` (D11), `Js.Code` inline target code (D12), and the `UnsafeCast<T>()` / `AsDynamic()` escape hatches (D13). It ships in one major with #328, spec 009 and spec 010 (D16).

## Technical Context

- **Language/Version**: C# 14 on .NET 10 for the compiler (SDK 11 RC once spec 010's toolchain stage lands); TypeScript (runtime, generated code) built with `tsgo`; Bun for tests and the benchmark.
- **Primary Dependencies**: Roslyn 5.3.0 → 5.9.0 (spec 010 010a), `IDynamic*Operation` (available), `Microsoft.CSharp.dll` in `TranspileHelper` references (FR-030), `mitata` (dev dependency of `metano-runtime`, benchmark only).
- **Testing**: TUnit goldens (`tests/Metano.Tests/Expected/`), bun tests in `targets/js/metano-runtime/test/system/{collections,exceptions,reflection}/`, the regenerated samples, spec 009's conformance fixtures, Dart tests.
- **Constraints**: `TreatWarningsAsErrors`; the core does not depend on any target (NFR-007); no runtime code generation (NFR-003); output for code without runtime questions changes only by the collection / exception migration (NFR-002); the budgets of NFR-001 and NFR-006.
- **Scale/Scope**: about 6 new core files (`IrCollectionTypeRef`, `IrDynamicTypeRef`, `IrReifiedTypeParameter`, `RuntimeTypeReachability`, `CollectionKindResolver`, the dynamic extractor), edits to `IrRuntimeTypeTest`, `IrTypeRefMapper`, `IrExpressionExtractor` (patterns, casts, `GetType`, `typeof`, `Js.Code`, escape hatches), `IrStatementExtractor` (`when` filters); about 7 new TS-target files (collection bridge, runtime-type emitter, runtime-test bridge, catch-chain bridge, inline-code bridge, interop rewriters) and edits to `IrToTsTypeMapper`, `IrToTsExpressionBridge.*`, `IrTypeCheckBuilder`, `ImportCollector`, `IrRuntimeRequirementScanner`, `JsonSerializerContextTransformer`, `TypeGuardBuilder`; 7 rewritten mapping files under `src/Metano/Runtime/`; a new `src/Metano/Interop/` folder (`JsArray`, `Js`, `InteropExtensions`); 3 new runtime modules and the reworked `HashSet`; every `targets/js/sample-*` regenerated; Dart printer edits.

## Constitution Check

| Principle | Status | Notes |
|---|---|---|
| Spec as source of truth | ✅ | This spec plus #335 land before code; the baseline matrix and the diagnostic catalog are updated in this PR; specs 009 / 010 are amended in follow-up PRs per the interaction table. |
| Clean Code / expressive names | ✅ | One job per unit: `CollectionKindResolver`, `RuntimeTypeReachability`, `IrToTsRuntimeTestBridge`, `IrToTsCatchChainBridge`, `IrToTsInlineCodeBridge`, `RuntimeTypeEmitter`. The mapping files lose every template with a double `$this`. |
| Ports & Adapters | ✅ | Roslyn `IDynamic*Operation` and `IDynamicTypeSymbol` are read in the core extractor only; targets consume `IrDynamic*` nodes. Descriptors are IR data; the TS runtime is the adapter. |
| Pragmatism | ✅ | Arrays stay native and unchecked (OD-4); descriptors are emitted only where asked (D14); `dynamic` and `Js.Code` are deliberate escape hatches with narrow diagnostics. |
| DX | ✅ | Each gap gets a specific diagnostic (`MS0044`–`MS0054`); the migration notes list every renamed member. |

**Required by gates**: ADR [0026](../../docs/adr/0026-runtime-collections-and-type-descriptors.md) (this PR); dual-agent review (`compiler-man` + `bob`) on every stage; the global reviewers (backend / frontend / architecture) on every code stage; the NFR-001 benchmark report before the collection stage merges.

## Design

| Layer | Change |
|---|---|
| `src/Metano/Runtime/*.cs` | Rewrite `Lists`, `Dictionaries`, `Sets`, `Queues`, `Stacks`, `ImmutableCollections`, `Enumerators` as name-only member mappings plus factory constructors (FR-007); delete the `Enumerable.from` entries of `Linq.cs`; `Exceptions.cs` maps every FR-013 class. |
| `src/Metano/Interop/` (new) | `JsArray<T>` (FR-004), `Js` (FR-031), `InteropExtensions` (FR-040 / FR-041). The `Metano` package ships them. |
| `Metano.Compiler/IR` | `IrCollectionTypeRef` + `IrCollectionKind`, `IrArrayTypeRef.IsJsArray`, `IrDynamicTypeRef`, the `IrRuntimeTypeTest` categories with `Of(type, staticType)`, `IrReifiedTypeParameter` on signatures and classes, `IrRuntimeTypeDescriptor`, `IrCatchClause.Filter`, the expression nodes of data-model.md § 2. |
| `Metano.Compiler/Extraction` | `CollectionKindResolver` (replaces the string-prefix predicates of `RoslynTypeQueries` for collections); `IrTypeRefMapper` arms for collections, `JsArray`, `dynamic`; `IrExpressionExtractor`: `is` / `as` / cast → the new nodes with the classification, `GetType()` / `typeof` → `IrGetTypeExpression` / `IrTypeOfExpression` (constant fold for plain objects), `Js.Code` → `IrInlineCodeExpression` with constant validation (MS0051), `UnsafeCast` / `AsDynamic` → their nodes, `IDynamic*Operation` → `IrDynamic*` nodes (MS0052), array→interface `adopt` insertion (FR-003), index-from-end / ranges → method calls and `fromEnd` (FR-010, no let), collection initializers → factories (FR-007, no let), `new T()` / `default(T)` for reified `T`; `IrStatementExtractor`: `when` filters. |
| `Metano.Compiler/Analysis` | `RuntimeTypeReachability` (FR-027), `ReifiedTypeParameterAnalysis` (FR-024: the per-body question scan, the override-group propagation, MS0053, MS0049), `PlainObjectOverlapAnalysis` (MS0046, reusing 009 FR-019's coverage relation), the `IrRuntimeRequirementScanner` additions. |
| `Metano.Compiler.TypeScript` | `IrToTsCollectionBridge`, `RuntimeTypeEmitter`, `IrToTsRuntimeTestBridge` (absorbs `BuildTypeTest` and `IrTypeCheckBuilder.Generate`'s table), `IrToTsCatchChainBridge`, `IrToTsInlineCodeBridge`, hidden-parameter rendering in the method / constructor / interface bridges and in every call-site bridge (overload dispatcher included), `IrToTsTypeMapper` (collections, `JsArray`, BCL exceptions → runtime classes, `dynamic` → `any`, `System.Type` → `RuntimeType`), `ImportCollector` + `IrRuntimeRequirementToTsImport` (new subpaths), `JsonSerializerContextTransformer` (FR-028), `TypeGuardBuilder` (plain-object guard synthesis shared with 009 / 010; field checks for runtime collections use `instanceof List` etc.), `PackageJsonWriter` (no change: the runtime's own `package.json` is hand-maintained). |
| `metano-runtime` | `system/collections` (`List`, `Dictionary`, `KeyCollection`, `ValueCollection`, `HashSet` rework, `Queue`, `Stack`, `ImmutableList`, `ReadOnlyList`, interfaces, `fromEnd`, `isIterable`), `system/exceptions`, `system/reflection`, `system/equality` (`HashSet` / `Dictionary` equality), `system/linq` (`toList`, `toDictionary`, `toHashSet`), `system/json` (`runtimeType` link, new descriptor kinds, `map` removed, `JsonException` / `NotSupportedException` moved), `package.json#exports` (FR-011), the spec 007 smoke test. |
| `Metano.Compiler.Dart` | `IrToDartTypeMapper` (collections, `dynamic`), `IrBodyPrinter` (`when` filters, `UnsafeCast`, `AsDynamic`, MS0050 for `Js.*`), exception-name mapping (FR-035). |
| Samples / targets | Regenerate every `targets/js/sample-*`; add `samples/SampleRuntimeTypes` + `targets/js/sample-runtime-types` (US3, US4, US6 scenarios, the factory, reified calls from hand-written TS, `Js.Code`); update sample bun tests for `List` / `Dictionary`. |
| Docs | `docs/runtime-types.md` (new), `docs/bcl-mappings.md`, `docs/serialization.md`, README, CHANGELOG migration notes, attribute catalog "Interop surface". |

## Stages (each its own PR, in order; all `feat!:` land on a `release/3.0` integration branch together with #328, 009 and 010)

1. **011a — Exceptions** (`feat!:`): FR-013, FR-014, FR-015 (typed catch), FR-016 for the existing helpers (`integer-division`, `number-parsing`), the `system/exceptions` module, the `Exception` base descriptor only (a minimal `reflection` stub with `runtimeType`, `classType`, `isType` for `class`). Goldens: `typed-catch`, `bcl-exception-classes`, `exception-hierarchy`. Gate: spec 009's `JsonException` / `NotSupportedException` tests pass against the moved classes.
2. **011b — Collections** (`feat!:`): FR-001–FR-012, FR-016 for the collections, `JsArray`, the mapping rewrite, the LINQ terminals, the JSON descriptor kinds (FR-028's collection half), every sample regenerated, the **benchmark** (below) run and recorded. Goldens: `collections-basic`, `collections-type-arguments`, `collections-interfaces-adopt`, `runtime-helpers`, `null-conditional-guards` (regenerated), `index-and-range` (regenerated), `dictionary-views`, `stack-enumeration`, `linq-to-list`. Gate: NFR-001 budgets met; `grep "(() =>"` over the samples reports only switch lowerings; spec 009 fixtures byte-identical.
3. **011c — Descriptors and tests** (`feat:`): FR-017–FR-023, FR-025–FR-027, FR-029 (the classification extension; the union path of 010 renders through it), `MS0044`–`MS0048`, `MS0054`, the dispatcher table absorbed (NFR-004). Goldens: `runtime-types-factory`, `runtime-types-interfaces`, `plain-object-tests`, `closed-generic-descriptors`, `get-type-fallbacks`, `checked-casts`, `nfr-002-no-questions` (the NFR-002 sample). Gate: NFR-002 diff test passes; the US3 bun scenario passes.
4. **011d — Reified type parameters** (`feat:`): FR-024, `MS0049`, `MS0053`, `new T()` / `default(T)` for reified `T` (retiring MS0019 at reified sites), the generic-class hidden field. Goldens: `reified-type-parameters`, `reified-overrides`, `reified-delegates`, `reified-class`, `reified-params-optional`. Gate: the US4 bun scenario passes; #291's `default(T)` tests for reified sites.
5. **011e — `dynamic`** (`feat:`): FR-030, `MS0052`, `TranspileHelper` + `Microsoft.CSharp`, the first probe of what each `IDynamic*Operation` binds to (research R6 [I]). Goldens: `dynamic-member-access`, `dynamic-conversions`, `dynamic-patterns`.
6. **011f — Inline code and escape hatches** (`feat:`): FR-031–FR-034, FR-040–FR-042, `MS0050`, `MS0051`, the `Js.Code` sample scenario, `docs/runtime-types.md` § four-way table. Goldens: `inline-code-expression`, `inline-code-statement`, `inline-code-holes-import`, `escape-hatches`.
7. **011g — Dart** (`feat:`): FR-035 and the Dart tests.
8. **011h — Release** (`chore:` / `docs:`): FR-036, FR-038, the CHANGELOG migration notes, README versions; merged together with #328, 009 and 010 through `release/3.0`.

Deferred / follow-ups: `Target.Language` constant folding (OD-16 (b)); sorted collections (OD-18 (b)); `Dart.Code`; a Dart `Stack` and exception bodies (#173, #175); every-`arr[i]` bounds checks (OD-4 (b)); member metadata on descriptors (reflection beyond `Type`'s identity surface); `TypeError` → `NullReferenceException` translation (OD-9 (c)).

## Benchmark plan (NFR-001; not run in this PR)

- **Harness**: `targets/js/metano-runtime/bench/collections.bench.ts`, `mitata`, run with `bun run bench` (script added in 011b); Node 22 is run once for comparison, Bun is the gate.
- **Fixtures**: 1e6 integers, 1e5 short strings, 1e5 record instances (`Point(x, y)` with `equals` / `hashCode`).
- **Cases** (each vs its native baseline): `List.get` / `set` in a `for` loop vs `arr[i]`; `List.add` vs `push`; `for..of` over `List` vs array; `Dictionary.get` / `set` / `containsKey` with number and string keys vs `Map`; `Dictionary` with record keys (no native baseline: reported absolute, with `Map`-by-reference as the lower bound); `HashSet.contains` 3.0 vs 2.x; `Queue.dequeue` ring buffer vs `shift`; `Stack.pop` vs `pop`; LINQ `where` + `select` + `toList` over `List` vs over array.
- **Method**: median of 10 runs after 3 warm-up runs, `--smol` off, GC between cases; the ratio to the baseline is the reported figure; `docs/runtime-types.md` records the table and the machine.
- **Gate**: every budget of NFR-001; a miss blocks 011b until OD-3 / OD-4 are revisited.
- **Size** (NFR-006): `bun build --minify` of a file that imports each module alone and of the three together, gzipped; recorded in the same table.

## Test strategy

- **Core units**: `CollectionKindResolver` for every C# type of FR-001 (source and metadata symbols); `IrRuntimeTypeTest.Of` for every category with and without a static type; `RuntimeTypeReachability` (base receiver reaches derived types; interfaces force `plainImplementers`; JSON reaches; nothing else does); `ReifiedTypeParameterAnalysis` (each trigger, non-triggers such as `new List<T>()`, propagation over an override group, MS0053 positions, trailing / defaulted / before-`params` placement); `PlainObjectOverlapAnalysis` (covers / covered / mutual / none).
- **Goldens**: listed per stage above, each checked by `tsgo --strict` in the sample where applicable.
- **Diagnostics**: one test per reason of contracts/diagnostics.md (MS0044 4 reasons, MS0045 2, MS0046 1, MS0047 1, MS0048 3, MS0049 1, MS0050 3, MS0051 4, MS0052 4, MS0053 2, MS0054 3), asserting code, severity and location; warning tests also assert emission.
- **Runtime (bun)**: every member of contracts/runtime-collections.md including exceptions and messages, enumeration-modification detection, record keys, `adopt` sharing, `Unknown` wildcard; every `getType` fallback; `isType` / `asType` / `castTo` for every kind; memoization identity; `toException` for `Exception`, `Error`, string, `undefined`; the typed-catch chain through generated code.
- **End-to-end**: regenerated samples build and pass; the new `sample-runtime-types` runs US3 / US4 / US6 scenarios; spec 009's conformance sample is byte-identical; the spec 007 smoke test covers the new subpaths.
- **NFR-002**: a fixture sample without runtime questions is transpiled before 011c and after 011d; the diff is empty.
- **Dart**: FR-035 items.
- **Regression**: the full TUnit suite and every bun sample.

## CI changes

- `ci.yml`: `cd targets/js/sample-runtime-types && bun run build && bun test`; `bun run bench --check` is **not** in CI (benchmarks are a review artifact, run on demand and recorded); the smoke test of spec 007 gains the three subpaths; the `git diff --exit-code targets/` check covers the regenerated samples.
- `release.yml`: no change beyond the `release/3.0` integration (handled by #328's spec).

## Compatibility and migration

See spec.md § Migration (3.0.0). Consumers without runtime-type questions see only the collection / exception migration (NFR-002). Cross-package consumers must upgrade both sides. The incremental cache's `ConfigurationFingerprint` gains a `runtime-types-v1` token so 2.x caches never mask the new output (as ADR-0025 did for the layout).

## Risks

| ID | Risk | Mitigation |
|---|---|---|
| K1 | `List.get(i)` is measurably slower than `arr[i]` in hot loops | NFR-001 budgets gate 011b; OD-3 / OD-4 are the levers; V8 inlines monomorphic methods (assumption, verified by the benchmark). |
| K2 | JS consumers of generated packages break on `List` where they expected arrays | 3.0.0 major; migration notes; `Iterable` + `toArray()` / `adopt` conversions; `JsArray<T>` for interop signatures. |
| K3 | Reachability misses a type (a `GetType()` answer or `is IFoo` falls back wrongly) | The base-receiver-reaches-derived rule and interface forcing are unit-tested; OD-6 (a) makes library descriptors complete. |
| K4 | Hidden parameters change public TS signatures | Only bodies that ask get them (FR-024); MS0049 names every interop-visible one; `Unknown` is the wildcard. |
| K5 | `dynamic` extraction paths are unexplored (research R6 [I]) | Stage 011e starts with a probe matrix over every `IDynamic*Operation` before any lowering is written. |
| K6 | Typed catch now rethrows what 2.x swallowed | Called out first in the migration notes; the chain is pinned by goldens. |
| K7 | The `[PlainObject]` overlap relation (009 FR-019) is conservative and warns on legitimate shapes | It is a warning; `UnsafeCast` or a `[JsonPolymorphic]` discriminator are the documented ways out. |
| K8 | Spec 009 / 010 follow-up edits drift from this spec | The interaction table is the checklist for those PRs; the union path is exercised by 011c's goldens. |
| K9 | Bundle size creeps above NFR-006 | Size is part of the benchmark table; modules are split so unused ones tree-shake. |
| K10 | ADR numbering collides with spec 010's planned `0026` | This PR takes 0026; spec 010's plan is updated to 0027 in its follow-up PR. |

## Project Structure

```text
specs/011-runtime-collections-and-types/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── runtime-collections.md
│   ├── runtime-types.md
│   ├── ts-emission.md
│   └── diagnostics.md
└── checklists/requirements.md
```

## Complexity Tracking

> No constitution violations. No entries.
