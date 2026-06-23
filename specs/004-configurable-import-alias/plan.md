# Implementation Plan: Configurable Isolated Subpath-Import Alias for Generated Packages

**Branch**: `004-configurable-import-alias` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-configurable-import-alias/spec.md`

## Summary

Add an opt-in, configurable, isolated Node.js subpath-import alias for **internal**
(same-project) imports in Metano-generated TypeScript. Today the alias key is the
hardcoded `#` (`#/<ns>` specifiers + `#`/`#/*` `package.json` entries), which
collides with a host project's own `#` when generated code is emitted into a
subfolder of an existing npm package, and resolves to the wrong path because the
in-file specifier ignores the output directory.

The chosen approach threads a single optional `importAlias` value — set via a new
`--import-alias` CLI flag and `MetanoImportAlias` MSBuild property — into (a) the
in-file specifier builder (`PathNaming`) and (b) the `package.json` writer
(`PackageJsonWriter`), plus the read-side cycle detector
(`CyclicReferenceDetector`). When set (e.g. `contracts`), internal specifiers
become `#contracts/<ns>` and `package.json` gets only `#contracts`/`#contracts/*`
entries scoped to the output subfolder, leaving the host's `#` untouched. When
unset, output is byte-identical to today. The change is confined to the TypeScript
target adapter; the target-agnostic core (`Metano.Compiler`) is not touched. A
pre-existing double-slash path bug in nested-output `package.json` entries is fixed
as a companion correctness item.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (compiler); emitted output is TypeScript consumed under Bun/Node.

**Primary Dependencies**: Roslyn 5.3.0 (Microsoft.CodeAnalysis) for semantic analysis; ConsoleAppFramework for the `metano-typescript` CLI; the `Metano.Build` MSBuild integration that mirrors CLI flags as `Metano*` properties.

**Storage**: N/A — the transpiler reads a `.csproj` and writes `.ts` files plus a merged `package.json`.

**Testing**: TUnit for .NET (`dotnet run --project tests/Metano.Tests/`); bun:test for generated TypeScript and runtime.

**Target Platform**: .NET 10 CLI / MSBuild build step; generated artifacts target Node/Bun module resolution (subpath imports).

**Project Type**: Compiler / transpiler (target-agnostic core library + per-language target adapters + CLI).

**Performance Goals**: No performance-sensitive path is affected. The alias is a string-prefix substitution computed once per run; no measurable impact on transpile throughput.

**Constraints**: Strictly backward compatible — default (unset) output is byte-identical to current behavior. `TreatWarningsAsErrors` and `dotnet csharpier .` cleanliness are mandatory. The cache fingerprint MUST incorporate the alias so changing it invalidates stale output.

**Scale/Scope**: Small, localized change in the TypeScript target adapter — ~7 production files plus tests; no new project, no core change, no new dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Verdict |
|-----------|------------|---------|
| I. Clean Code as the Baseline | One small responsibility per edit; the alias-normalization rule is extracted into a named helper rather than inlined into a condition; csharpier + warnings-as-errors enforced. | ✅ Pass |
| II. Expressive, Intention-Revealing Code | `ImportAlias` / `AliasPrefix` / `starKey` / `rootKey` name the concept directly; the public surface (`--import-alias`, `MetanoImportAlias`) reveals intent. | ✅ Pass |
| III. Screaming, Feature-Semantic Organization | Edits land in existing capability folders (`Transformation/PathNaming.cs`, `Transformation/CyclicReferenceDetector.cs`, `PackageJsonWriter.cs`); no new generic `Helpers/`/`Utils/` bucket introduced. | ✅ Pass |
| IV. Clean Architecture via Ports & Adapters | The alias is a TypeScript-emission concern and lives **entirely in the TS target adapter** (`Commands.cs`, `TypeScriptTarget.cs`, `TypeTransformer.cs`, the writer). The core (`Metano.Compiler`, `TranspileOptions`) is **not** touched — dependencies still point inward. | ✅ Pass |
| V. Developer Experience First | Single documented CLI flag + MSBuild property; golden/expected-output tests accompany the behavior; no silent failure (the multi-project collision limitation is documented, not hidden). | ✅ Pass |
| VI. Pragmatism Over Dogma | Opt-in, minimal threading; no new config-file layer, no speculative cross-project collision machinery (explicitly deferred). | ✅ Pass |

**Result**: All gates pass. No deviations to record — Complexity Tracking is empty.

> Process note: the constitution's "Worktree per issue" workflow rule was waived by
> the user for this session (work proceeds on branch `004-configurable-import-alias`
> in the main working directory). User instruction takes precedence; this is a
> workflow choice, not a code-complexity violation, so it is not tracked as a gate
> failure.

## Project Structure

### Documentation (this feature)

```text
specs/004-configurable-import-alias/
├── plan.md              # This file (/speckit-plan output)
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — decisions & rationale
├── data-model.md        # Phase 1 output — config value objects & flow
├── quickstart.md        # Phase 1 output — developer walkthrough
├── contracts/           # Phase 1 output — CLI/MSBuild + generated-shape contracts
│   ├── cli-and-msbuild.md
│   └── generated-imports.md
└── checklists/
    └── requirements.md  # Spec quality checklist (from /speckit-specify)
```

### Source Code (repository root)

Changes are confined to the TypeScript target adapter and the build integration.
The target-agnostic core (`src/Metano.Compiler/`) is intentionally untouched.

```text
src/
├── Metano.Compiler/                      # CORE — NOT modified (no alias leaks here)
└── Metano.Compiler.TypeScript/
    ├── Commands.cs                        # + --import-alias flag; thread to target + writer
    ├── TypeScriptTarget.cs                # + ImportAlias prop; cache fingerprint
    ├── PackageJsonWriter.cs               # alias-scoped imports keys + double-slash fix
    └── Transformation/
        ├── PathNaming.cs                  # AliasPrefix → in-file specifier
        ├── TypeTransformer.cs             # construct PathNaming w/ alias; pass to detector
        └── CyclicReferenceDetector.cs     # recognize alias keys in the import graph
src/Metano.Build/
└── build/Metano.Build.targets            # + MetanoImportAlias property → --import-alias

tests/
└── Metano.Tests/                         # PathNaming, EmitPackage (package.json),
                                          # Cyclic, + new alias golden/no-regression tests
```

**Structure Decision**: Single-project compiler with target adapters (Option 1,
specialized). The feature is a TypeScript-emission concern, so it is implemented in
`src/Metano.Compiler.TypeScript/` (the TS adapter) and `src/Metano.Build/` (its
MSBuild surface), keeping the core port (`ITranspilerTarget` / `Metano.Compiler`)
free of language-specific path knobs — consistent with Principle IV.

## Complexity Tracking

> No constitution violations. No entries.
