# Implementation Plan: C# 15 union declarations

**Branch**: `spec/009-csharp-unions` | **Date**: 2026-10-31 | **Spec**: [spec.md](./spec.md) | **Issue**: [#304](https://github.com/danfma/metano/issues/304)

## Summary

The work makes C# 15 `union` declarations a target-agnostic IR concept:

- `IrUnionDeclaration` for the declaration, and `IrNamedTypeKind.Union` plus its cases on every reference.
- Detection through a single Roslyn adapter, the only file allowed to touch `RSEXPERIMENTAL006`.
- Each target renders that IR. TypeScript erases a union to `export type U = A | B`, dispatches through the shared `IrRuntimeTypeTest` classification, and ends exhaustive switches with `$s satisfies never`. Dart emits `typedef U = Object`.
- The maintainer decisions D10–D13 shape four more pieces:
  - `default(U)` is emitted as `null`; slots that hold it by declaration are widened to `U | null` and read with `!`, and only positions that cannot hold `null` get the `MS0041` warning (FR-015, FR-039);
  - overlapping cases are the `MS0039` warning with first-wins output (FR-013, FR-040);
  - structural cases get synthesized `isX` guards (FR-041);
  - body members and extra interfaces lower to a companion `namespace U` merged with the alias, and call sites become `U.member(u, …)` (FR-042).
- JSON gets a `union` `TypeDescriptor` that reproduces .NET 11 STJ: bare payload on write, token-kind classification on read, and STJ's exact error messages.
- A toolchain stage comes first: Roslyn 5.9.0, SDK 11 RC in `global.json`, `RollForward=Major` on the net10 tools, and CI with both SDKs.

## Technical Context

- **Language/Version**:
  - C# 14 for Metano itself on .NET 10. The tools stay `net10.0` with `RollForward=Major`.
  - Test inputs and the sample are C# 15 preview on `net11.0`.
- **Primary Dependencies**:
  - Roslyn `Microsoft.CodeAnalysis.*` 5.9.0 (from 5.3.0).
  - System.Text.Json 11 RC. It is the reference behaviour for the fixtures only, not a Metano dependency.
  - TUnit, bun:test, tsgo.
- **Testing**:
  - Goldens in `tests/Metano.Tests/Expected/union-*.ts`.
  - `TranspileHelper` with net11 reference assemblies (OD-1).
  - `TranspileWithLibrary` for cross-package cases.
  - The `targets/js/sample-unions` bun tests, which check STJ fixtures.
- **Constraints**:
  - `TreatWarningsAsErrors`.
  - The core does not depend on any target.
  - No BCL-specific IR node types. A union is a language construct, so `IrUnionDeclaration` is allowed.
  - Non-union output stays byte-identical (NFR-001).
- **Scale/Scope**: about 8 new core files (including the default-widening pass and the member-call rewriter), edits in about 18 files spread across the core, TS and Dart, 1 runtime module, 1 sample plus its consumer, and 2 workflows.

## Constitution Check

| Principle | Status | Notes |
|---|---|---|
| Spec as source of truth | ✅ | This spec (010) plus issue #304 land before any code. The baseline matrix and the diagnostic catalog are updated in the same PR. |
| Clean Code / expressive names | ✅ | Reader, extractor, validator and bridge each own one job: `UnionShapeReader`, `IrUnionExtractor`, `UnionShapeValidator`, `IrToTsUnionBridge`, `UnionJsonDescriptorBuilder`. |
| Ports & Adapters | ✅ | Roslyn specifics (including the experimental API) sit behind one core adapter. Targets consume IR only (NFR-003). |
| Pragmatism | ✅ | Erasure, with no runtime wrapper and no new runtime export for dispatch (`satisfies never` is type-level). Where erasure only changes dispatch (overlapping cases, `default` in a non-null position), the output is emitted with a warning that names the divergence; where Metano cannot produce a faithful output at all (hand-written unions, nested unions, unsupported member kinds), it is an error. The companion namespace adds a runtime value only for unions that declare members. |
| DX | ✅ | Each misuse gets a specific diagnostic (MS0038–MS0043), and missing cases surface as a TS build error. |

**Required by gates**:
- an ADR `docs/adr/0026-erased-csharp-unions.md` that records erasure, the STJ-parity wire format, the host-framework decision and the maintainer decisions D10–D13 (default widening, first-wins overlap, synthesized guards, the companion namespace);
- dual-agent review (`compiler-man` + `bob`) on every stage;
- the global reviewers (backend / frontend / architecture) on every code stage.

## Design

| Layer | Change |
|---|---|
| Directory / CI | `Directory.Packages.props`: Roslyn 5.9.0. `global.json`: SDK `11.0.100-rc.1.26425.128`, `allowPrerelease: true`, `rollForward: latestFeature`. Tool csproj files: `<RollForward>Major</RollForward>`. `ci.yml` / `release.yml`: `global-json-file: global.json` plus `dotnet-version: 10.0.x` (no floating `11.0.x`), a `sample-unions` bun step, the negative TS1360 script step, and a path-filtered fixture-drift job (FR-004). |
| `Metano.Compiler/Extraction` | `UnionShapeReader`, the only `#pragma warning disable RSEXPERIMENTAL006`: `IsUnion`, `IsCompilerGenerated`, `ReadCases` (declared and constructed), `ReadBodyMembers` (the user members FR-042 lowers), `ReadShapeViolations` (the member kinds it does not). `UnionDefaultWidening` (FR-039): finds the slots that hold `default(U)` by declaration, marks their type as widened, adds the non-null assertion to their `U`-typed reads, and reports MS0041 for a default value in a non-widenable `U` position. `UnionMemberCallRewriter` (FR-042): `u.M(a)` / `u.P` / `U.S(a)` → companion calls, with the evaluate-once nodes (`IrLetExpression` / `IrEvaluationTraits`) for a null-conditional receiver. The `IUnion.Value` cast rewrite and the `IUnion` conversion check (FR-034). The relational-pattern case test from `NarrowedType` (FR-033). `IrBreakStatement.TargetsSwitch` from `IBranchOperation` (FR-021). `IrUnionExtractor`. A union arm in `IrTypeRefMapper.BuildNamedTypeSemantics`, placed before the Branded/Record/Struct arms, with a visited-set guard and `originResolver` threaded through. Identity rewrites in `IrExpressionExtractor`: union `Value`, `new U(x)`, conversions, plus the `Equals`/`GetHashCode` mapping. Pattern + `when` on switch sections in `IrStatementExtractor`. |
| `Metano.Compiler/IR` | `IrUnionDeclaration` (with optional `Members`, `NestedTypes` and `Interfaces`, FR-042), `IrNamedTypeKind.Union`, `IrNamedTypeSemantics.UnionCases`, `IrNullableTypeRef.IsDefaultWidening` (optional, default `false`; FR-039), a non-null assertion expression for widened reads (or the existing one, if the IR already has it). Synthesized structural guards reuse `IrNamedTypeSemantics.HasGeneratedGuard` (FR-041). `IrSwitchExpression.ScrutineeType` (optional, default `null`). `IrSwitchCase.PatternLabels` (optional list of `IrSwitchLabel(Pattern, When)`), `IrBreakStatement.TargetsSwitch` (default `false`). |
| `Metano.Compiler/Analysis` | `UnionShapeValidator` (MS0038 and MS0040 errors, MS0039 and MS0043 warnings), run at declarations and once per constructed generic instance at use sites (FR-031), with the erased-domain overlap rule (FR-013). `IrRuntimeRequirementScanner` walks `IrTypePattern` and `IrUnionDeclaration`, and its `ResolveTypeCheckHelper` handles `Union` as the OR of its cases. An audit of every `IrNamedTypeKind` `_ =>` consumer: `IrEqualityClassifier`, the scanner (`ResolveTypeCheckHelper`), `IrTypeCheckBuilder.GenerateForParam` and its three callers (`IrToTsOverloadDispatcherBridge`, `IrToTsConstructorDispatcherBridge`, `IrToTsClassBridge.Operators`), the class / exception / constructor bridges, and the expression bridge (FR-032). |
| `Metano.Compiler.TypeScript` | `IrToTsUnionBridge` (`TsTypeAlias` + `TsUnionType`; the AST and printer already exist), which also owns the union guard and its value imports (FR-017, FR-035) and the companion namespace (FR-042, through the existing namespace emission and its spec 006 import path); `TypeGuardBuilder` skips unions, and runs for every structural union case without `[GenerateGuard]` to synthesize its guard in the case's own file (FR-041). Labeled-block lowering for pattern switch statements (FR-021). `TryEmitUnion` at the head of the TypeTransformer emit chain. A union OR-test in `BuildTypeTest`, which renders `IrRuntimeTypeTest`. The `never` tail in `IrToTsExpressionBridge.SwitchExpression`. Statement pattern-label lowering. A cross-assembly fallback in `TryResolveGuardImport`. In `JsonSerializerContextTransformer`: a `Union` kind in `ClassifyPropertyType` ahead of the nullable-inner / `Ref` / struct checks, `ChildTypesOf(Union)` returning the cases, the inline union descriptor, the STJ token table and `NumberHandling` reading (FR-030, FR-037), MS0038 for undescribable or cross-assembly object cases (FR-038), plus MS0042. |
| `metano-runtime/system/json` | `UnionDescriptor` in `types.ts`. `unionConverter` in `converters.ts` (with the STJ messages in one place, `union-messages.ts`). Exported from `index.ts`. |
| `Metano.Compiler.Dart` | `IrToDartUnionBridge` (typedef; body members skipped, FR-027), a union-aware switch tail, and pattern labels in `IrBodyPrinter`. |
| Tests | `TranspileHelper.Net11UnionReferences` (FR-036): the net11 ref pack resolved from `global.json` through `BundledVersions.props`, plus `Metano.dll`; used for both sides of `TranspileWithLibrary` when either declares a union. |
| Samples | `samples/SampleUnions` (net11.0, preview) + `targets/js/sample-unions` (Bun workspace member). A fixture generator project (OD-2). |

## Stages (each its own PR, in order)

1. **010a — Toolchain** (`chore:`): FR-001–FR-004, nothing else. Gate: the full test suite has the same pass/skip counts, `git diff --exit-code targets/` is clean, `dotnet csharpier check .` passes, and the tool starts on a .NET 11-only `DOTNET_ROOT`.
2. **010b — Shape** (`feat:`): FR-005–FR-007, FR-009, FR-010, FR-012–FR-018, FR-031, FR-034, FR-036, FR-039, FR-027 (typedef part). Goldens: `union-basic`, `union-primitive`, `union-nullable`, `union-generic`, `union-identity`, `union-nested-in-class`, `union-record-field-equality`, `union-default-widening`, `union-overlap-warning` (declaration side).
3. **010c — Patterns** (`feat:`, plus `fix:` commits for FR-021 and FR-033's `object` part): FR-008, FR-011, FR-019–FR-022, FR-032, FR-033, FR-035, FR-040, FR-041, FR-027 (switch part). PR #298 merged on 2026-10-31, so nothing blocks this stage except its switch-statement part, which waits for OQ-3 (FR-021 versus the `switch (true)` lowering #239 merged). Goldens: `union-switch`, `union-switch-guarded`, `union-switch-overlap` (first arm wins), `union-structural-synthesized-guard`, `union-switch-nullable`, `union-switch-statement`, `union-switch-relational`, `union-guard`, `union-overload-dispatch`, `union-cross-package`, and the non-union regressions `switch-statement-pattern-labels` (mid-section `break`, switch in a `for` loop, middle `default`, mixed labels, multi-label section, nested switches) and `relational-pattern-object`.
4. **010d — JSON** (`feat:`): FR-023–FR-026, FR-029, FR-030, FR-037, FR-038. Runtime tests, the sample and its fixtures, and the CI bun step.
5. **010e — Members** (`feat:`): FR-042 and the Dart member skip of FR-027. It needs 010c, because member bodies use union patterns (`this switch { … }`). Goldens: `union-members`, `union-interface`, `union-members-cross-package`; the sample gains a union with members and an extra interface (SC-006).
6. **Deferred / follow-ups**: union roots (OD-8, after #270 in spec 009), `[JsonPolymorphic]` cases (OD-7, after #268 in spec 009), adapters for a union converted to its extra interface (OQ-1), consumer-local guards for cross-assembly structural cases (OQ-2), a Dart companion for members, open-`T` tests, nested-union flattening, Dart sealed classes, cross-assembly object cases in consumer contexts (OD-13), relaxed structural overlap (OD-15), STJ parity for `byte[]` / `[StringEnum]` / `char` descriptors (OD-16).

## Test strategy

- **Reader units**: a source union, a metadata union (emitted in-memory, then referenced), a generic union and its constructed instances, a nullable case, a hand-written `[Union] struct` / `[Union] class` (MS0038, from source and from metadata), a plain `struct : IUnion` (not a union), and case order.
- **Reference set**: one test asserts that a union compilation over `Net11UnionReferences` reports no CS0433 / CS0518 / CS0656, and one asserts the clear failure message when the pack path does not exist.
- **Goldens** (`tests/Metano.Tests/Expected/`): one per stage-2/3 scenario listed above, each checked by `tsgo --strict` in the consumer sample where applicable.
- **Diagnostics**: one test per reason. MS0038 has 13 reasons, MS0039 7, MS0040 4, MS0041 4, MS0042 3 and MS0043 5, and each test asserts code, severity and location. Every warning test (MS0039, MS0041–MS0043) also asserts that the output is emitted.
- **Dart**: typedef, generic typedef, `Object?`, the switch tail (also over a nullable union), statement labels with a mid-section `break` and a middle `default`, a union with body members (skipped), and no MS0009.
- **JSON transformer units**: `Holder(Clip, Id)` as the only root still declares `imageClip` / `silenceClip` and no `clip` getter; `Opt<int>`; Web defaults; `byte[]` / `[StringEnum]` / `char` / dictionary-key MS0038; the cross-package consumer context (US4-4).
- **Runtime (bun, `metano-runtime/test/system/json/union.test.ts`)**: write and read per token kind, every STJ error message verbatim, null passthrough, and composition in array, map, nullable and DTO.
- **End-to-end (`targets/js/sample-unions`)**:
  - `bun run build` (the `never` checks compile; a deliberately broken file under `test/negative/` is type-checked in a separate script that expects TS1360);
  - `bun test` (dispatch, the `default` throw through a widened field, companion members, fixture byte parity);
  - fixtures produced by a net11 STJ console project (`samples/SampleUnions.Fixtures`) that writes `targets/js/sample-unions/test/fixtures/*.json`.
- **Regression**: the full TUnit suite and every existing bun sample, unchanged.

## CI changes

- `ci.yml` and `release.yml`: `actions/setup-dotnet` with `global-json-file: global.json` (the exact pinned RC) plus `dotnet-version: 10.0.x` for the runtime the tests and CSharpier use. No floating `11.0.x`: an SDK change happens only through a reviewed `global.json` bump. Release packs with that pinned SDK (OD-14).
- `ci.yml` bun job: `cd targets/js/sample-unions && bun run build && bun run typecheck:negative && bun test`. The negative script type-checks `test/negative/` and passes only when tsgo reports TS1360.
- `ci.yml` fixture-drift job (if OD-2a): a separate job, path-filtered on `global.json`, `samples/SampleUnions.Fixtures/**` and `targets/js/sample-unions/test/fixtures/**`. It runs the generator, then `git diff --exit-code targets/js/sample-unions/test/fixtures`, so STJ drift never fails an unrelated PR.
- The existing `git diff --exit-code targets/` check after `dotnet build Metano.slnx` now also covers the `sample-unions` output.

## Compatibility and migration

- **Consumers without unions**: no output change (NFR-001), except FR-021, whose earlier output silently turned `case T x:` into `default:`, and FR-033 for relational patterns over `object`. Both are released as `fix:` and called out in the CHANGELOG.
- **Consumers with unions**: they need the release that ships 010b or later, SDK 11, and `LangVersion preview`. Older tools fail with MS0009, which is Roslyn's parse error. The README states the minimum tool version.
- **Host**: `RollForward=Major` changes nothing on machines that have .NET 10. On machines with only .NET 11, the tool now starts instead of failing.
- **Wire**: a union's JSON shape is STJ's own, so a .NET 11 service and a Metano TS client interoperate with no converter.
- **Semantic release**: 010a is `chore:` (or `build:`), 010b–e are `feat:`, and the FR-021 and FR-033 commits are `fix:`. There are no breaking changes.

## Risks

| ID | Risk | Mitigation |
|---|---|---|
| K1 | RSEXPERIMENTAL006 and STJ RC behaviour change before GA | One adapter file (NFR-002), one message module; the fixture regeneration in CI catches STJ drift. |
| K2 | A union under a `[JsonSerializerContext]` inherits existing context bugs (derived-first order, `base` under TranspileAssembly) | Fixtures use non-inheritance cases; the inheritance-related work stays with #268. |
| K3 | CSharpier 1.2.6 cannot parse `union` | Verify in 010a; `.csharpierignore` for union sources if needed. |
| K4 | Merge conflicts with other pattern PRs | #298 has merged; 010c rebases onto `origin/main` right before the push. |
| K5 | MS0039 warns on legitimate unions (int/long), and first-wins output silently picks the earlier case at runtime | The warning is deliberate and names the winning case (D11, FR-040); OD-15 can relax rule 4 later behind tests. |
| K6 | Adding `PatternLabels` to `IrSwitchCase` and `TargetsSwitch` to `IrBreakStatement` ripples into Dart and `IrBodyCoverageProbe` | Optional fields default to `null` / `false`; every consumer checks `PatternLabels` before treating an empty `Labels` list as `default`; Dart has its own test. |
| K7 | STJ rejects `5.5` for an `int` case with `The JSON value could not be converted to System.Int32.`, but today's TS `primitive` descriptor accepts any number | The union converter validates integer cases with STJ's message. Tightening every `int` descriptor in the same way is a separate decision and stays out of scope. |
| K8 | The merged #298 classification is renamed or its primitive rendering changes later | The spec depends on the capability (FR-019); FR-013 is written over value domains, so it does not depend on the rendering. |
| K9 | The Dart 3 switch semantics behind FR-027 are from the language specification, not a probe | The Dart tests are the gate; if native Dart switches diverge, Dart takes the same labeled-block lowering as TS. |
| K10 | Default widening (FR-039) changes public TS types (`pending: Clip \| null`) and adds `!` at reads | Only slots that really hold `default(U)` are widened; the `!` is type-only, so runtime behaviour equals C#; goldens pin both. |
| K11 | The alias + namespace merge (FR-042) interacts with barrels, `import type` and `isolatedModules` | Research R11 verified the merge, a single value import and namespace `let` writes under tsgo strict with `isolatedModules`; the `union-members` golden is also compiled by the sample's tsgo build. |
| K12 | Synthesized guards (FR-041) add an `isX` export to a case type's file | Only types that are union cases change; the export matches what `[GenerateGuard]` would add, so no new naming rule appears. |

## Project Structure

```text
specs/010-csharp-unions/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── ts-emission.md
│   ├── json-wire.md
│   └── diagnostics.md
└── checklists/requirements.md
```

## Complexity Tracking

> No constitution violations. No entries.
