# Implementation Plan: C# 15 union declarations

**Branch**: `spec/009-csharp-unions` | **Date**: 2026-10-31 | **Spec**: [spec.md](./spec.md) | **Issue**: [#304](https://github.com/danfma/metano/issues/304)

## Summary

The work makes C# 15 `union` declarations a target-agnostic IR concept:

- `IrUnionDeclaration` for the declaration, and `IrNamedTypeKind.Union` plus its cases on every reference.
- Detection through a single Roslyn adapter, the only file allowed to touch `RSEXPERIMENTAL006`.
- Each target renders that IR. TypeScript erases a union to `export type U = A | B`, dispatches through the shared `IrRuntimeTypeTest` classification, and ends exhaustive switches with `$s satisfies never`. Dart emits `typedef U = Object`.
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
- **Scale/Scope**: about 6 new core files, edits in about 12 files spread across the core, TS and Dart, 1 runtime module, 1 sample plus its consumer, and 2 workflows.

## Constitution Check

| Principle | Status | Notes |
|---|---|---|
| Spec as source of truth | ✅ | This spec (009) plus issue #304 land before any code. The baseline matrix and the diagnostic catalog are updated in the same PR. |
| Clean Code / expressive names | ✅ | Reader, extractor, validator and bridge each own one job: `UnionShapeReader`, `IrUnionExtractor`, `UnionShapeValidator`, `IrToTsUnionBridge`, `UnionJsonDescriptorBuilder`. |
| Ports & Adapters | ✅ | Roslyn specifics (including the experimental API) sit behind one core adapter. Targets consume IR only (NFR-003). |
| Pragmatism | ✅ | Erasure, with no runtime wrapper and no new runtime export for dispatch (`satisfies never` is type-level). Anything erasure cannot represent faithfully is a hard error, not a special case. |
| DX | ✅ | Each misuse gets a specific diagnostic (MS0031–MS0036), and missing cases surface as a TS build error. |

**Required by gates**:
- an ADR `docs/adr/0026-erased-csharp-unions.md` that records erasure, the STJ-parity wire format and the host-framework decision;
- dual-agent review (`compiler-man` + `bob`) on every stage;
- the global reviewers (backend / frontend / architecture) on every code stage.

## Design

| Layer | Change |
|---|---|
| Directory / CI | `Directory.Packages.props`: Roslyn 5.9.0. `global.json`: SDK `11.0.100-rc.1.26425.128`, `allowPrerelease: true`, `rollForward: latestFeature`. Tool csproj files: `<RollForward>Major</RollForward>`. `ci.yml` / `release.yml`: `dotnet-version: \| 10.0.x \n 11.0.x`, plus a `sample-unions` bun step. |
| `Metano.Compiler/Extraction` | `UnionShapeReader`, the only `#pragma warning disable RSEXPERIMENTAL006`: `IsUnion`, `ReadCases`, `ReadShapeViolations`. `IrUnionExtractor`. A union arm in `IrTypeRefMapper.BuildNamedTypeSemantics`, placed before the Branded/Record/Struct arms, with a visited-set guard and `originResolver` threaded through. Identity rewrites in `IrExpressionExtractor`: union `Value`, `new U(x)`, conversions, plus the `Equals`/`GetHashCode` mapping. Pattern + `when` on switch sections in `IrStatementExtractor`. |
| `Metano.Compiler/IR` | `IrUnionDeclaration`, `IrNamedTypeKind.Union`, `IrNamedTypeSemantics.UnionCases`. `IrSwitchExpression.ScrutineeType` (optional, default `null`). `IrSwitchCase.Pattern` / `When` (optional). |
| `Metano.Compiler/Analysis` | `UnionShapeValidator` (MS0031–MS0034, MS0036). `IrRuntimeRequirementScanner` walks `IrTypePattern` and `IrUnionDeclaration`. An audit of every `IrNamedTypeKind` `_ =>` consumer (`IrEqualityClassifier`, the scanner, `IrTypeCheckBuilder`, the class / exception / constructor bridges, the expression bridge). |
| `Metano.Compiler.TypeScript` | `IrToTsUnionBridge` (`TsTypeAlias` + `TsUnionType`; the AST and printer already exist). `TryEmitUnion` at the head of the TypeTransformer emit chain. A union OR-test in `BuildTypeTest`, which renders `IrRuntimeTypeTest`. The `never` tail in `IrToTsExpressionBridge.SwitchExpression`. Statement pattern-label lowering. A cross-assembly fallback in `TryResolveGuardImport`. In `JsonSerializerContextTransformer`, a `union` arm before the `ref` fallback, plus MS0035. |
| `metano-runtime/system/json` | `UnionDescriptor` in `types.ts`. `unionConverter` in `converters.ts` (with the STJ messages in one place, `union-messages.ts`). Exported from `index.ts`. |
| `Metano.Compiler.Dart` | `IrToDartUnionBridge` (typedef), a union-aware switch tail, and pattern labels in `IrBodyPrinter`. |
| Samples | `samples/SampleUnions` (net11.0, preview) + `targets/js/sample-unions` (Bun workspace member). A fixture generator project (OD-2). |

## Stages (each its own PR, in order)

1. **009a — Toolchain** (`chore:`): FR-001–FR-004, nothing else. Gate: the full test suite has the same pass/skip counts, `git diff --exit-code targets/` is clean, `dotnet csharpier check .` passes, and the tool starts on a .NET 11-only `DOTNET_ROOT`.
2. **009b — Shape** (`feat:`): FR-005–FR-007, FR-009, FR-010, FR-012–FR-018, FR-027 (typedef part). Goldens: `union-basic`, `union-primitive`, `union-nullable`, `union-generic`, `union-identity`.
3. **009c — Patterns** (`feat:`, plus a `fix:` commit for FR-021): FR-008, FR-011, FR-019–FR-022, FR-027 (switch part). This stage waits for PR #298. Goldens: `union-switch`, `union-switch-guarded`, `union-switch-nullable`, `union-switch-statement`, `union-cross-package`, and a non-union `switch-statement-pattern-labels` regression.
4. **009d — JSON** (`feat:`): FR-023–FR-026, FR-029. Runtime tests, the sample and its fixtures, and the CI bun step.
5. **Deferred / follow-ups**: union roots (OD-8), `[JsonPolymorphic]` cases (OD-7), body members (OD-5), open-`T` tests, nested-union flattening, Dart sealed classes.

## Test strategy

- **Reader units**: a source union, a metadata union (emitted in-memory, then referenced), a generic union, a nullable case, a hand-written `[Union] struct` / `[Union] class`, a plain `struct : IUnion` (not a union), and case order.
- **Goldens** (`tests/Metano.Tests/Expected/`): one per stage-2/3 scenario listed above, each checked by `tsgo --strict` in the consumer sample where applicable.
- **Diagnostics**: one test per reason. MS0031 has 8 reasons, MS0032 4, MS0033 4, MS0034 3, MS0035 2 and MS0036 4, and each test asserts code, severity and location.
- **Dart**: typedef, `Object?`, the switch tail, statement labels, and no MS0009.
- **Runtime (bun, `metano-runtime/test/system/json/union.test.ts`)**: write and read per token kind, every STJ error message verbatim, null passthrough, and composition in array, map, nullable and DTO.
- **End-to-end (`targets/js/sample-unions`)**:
  - `bun run build` (the `never` checks compile; a deliberately broken file under `test/negative/` is type-checked in a separate script that expects TS1360);
  - `bun test` (dispatch, the `default` throw, fixture byte parity);
  - fixtures produced by a net11 STJ console project (`samples/SampleUnions.Fixtures`) that writes `targets/js/sample-unions/test/fixtures/*.json`.
- **Regression**: the full TUnit suite and every existing bun sample, unchanged.

## CI changes

- `ci.yml` and `release.yml`: `actions/setup-dotnet` with `dotnet-version: |\n 10.0.x\n 11.0.x` and `dotnet-quality: preview` until 11 reaches GA.
- `ci.yml` bun job: `cd targets/js/sample-unions && bun run build && bun test`.
- `ci.yml` .NET job (if OD-2a): run the fixture generator, then `git diff --exit-code targets/js/sample-unions/test/fixtures`.
- The existing `git diff --exit-code targets/` check after `dotnet build Metano.slnx` now also covers the `sample-unions` output.

## Compatibility and migration

- **Consumers without unions**: no output change (NFR-001), except FR-021, whose earlier output silently turned `case T x:` into `default:`. That change is released as a `fix:` and called out in the CHANGELOG.
- **Consumers with unions**: they need the release that ships 009b or later, SDK 11, and `LangVersion preview`. Older tools fail with MS0009, which is Roslyn's parse error. The README states the minimum tool version.
- **Host**: `RollForward=Major` changes nothing on machines that have .NET 10. On machines with only .NET 11, the tool now starts instead of failing.
- **Wire**: a union's JSON shape is STJ's own, so a .NET 11 service and a Metano TS client interoperate with no converter.
- **Semantic release**: 009a is `chore:` (or `build:`), 009b–d are `feat:`, and the FR-021 commit is `fix:`. There are no breaking changes.

## Risks

| ID | Risk | Mitigation |
|---|---|---|
| K1 | RSEXPERIMENTAL006 and STJ RC behaviour change before GA | One adapter file (NFR-002), one message module; the fixture regeneration in CI catches STJ drift. |
| K2 | A union under a `[JsonSerializerContext]` inherits existing context bugs (derived-first order, `base` under TranspileAssembly) | Fixtures use non-inheritance cases; the inheritance-related work stays with #268. |
| K3 | CSharpier 1.2.6 cannot parse `union` | Verify in 009a; `.csharpierignore` for union sources if needed. |
| K4 | Merge conflicts with PR #298 and other pattern PRs | 009c starts after #298 merges and rebases onto `origin/main` right before the push. |
| K5 | MS0032 over-rejects legitimate unions (int/long) | It is deliberate (OD-11); it can be relaxed later behind tests. |
| K6 | Adding `Pattern` / `When` to `IrSwitchCase` ripples into Dart and `IrBodyCoverageProbe` | Optional fields default to `null`; every consumer checks `Pattern` first; Dart has its own test. |
| K7 | STJ rejects `5.5` for an `int` case with `The JSON value could not be converted to System.Int32.`, but today's TS `primitive` descriptor accepts any number | The union converter validates integer cases with STJ's message. Tightening every `int` descriptor in the same way is a separate decision and stays out of scope. |

## Project Structure

```text
specs/009-csharp-unions/
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
