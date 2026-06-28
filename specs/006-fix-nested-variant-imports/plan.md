# Implementation Plan: Complete imports for nested record variants

**Branch**: `006-fix-nested-variant-imports` (authored on `main` per maintainer choice) | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-fix-nested-variant-imports/spec.md`

## Summary

Generated TypeScript for the companion-namespace lowering of an abstract record with nested record variants (the `[StrictUnionGuard]` discriminated-union pattern) omits imports for symbols referenced only inside the nested `namespace` block. The fix closes two independent gaps in two pipeline layers:

1. **Type-reference imports (TypeScript target).** The import walker `ImportCollector.CollectFromTopLevel` handles `TsNamespaceDeclaration` but only walks `ns.Functions`, never `ns.Members` — where the variant classes live. The fix recurses `CollectFromTopLevel` over `ns.Members` (and unifies the function walk through the same entry point), so every symbol kind already handled by the `TsClass`/`TsFunction` cases (intra-project types, cross-package origins, value references, guards, helpers) is collected at any nesting depth.
2. **Runtime-helper imports (TypeScript target).** A variant's non-strict field needs the `valueEquals` import, collected by the runtime-requirement scanner — which only saw top-level types. The fix makes `TypeTransformer.ScanIrRuntimeRequirements` also scan the nested types the TS target emits into the parent's file, via a recursive helper `ScanTypeAndEmittedNested` that reuses the existing `IsExportableNestedType` gate and `GetOrExtractIr` dispatcher and applies the same `[Import]`/`[Ignore]`/entry-point skips uniformly. The core stays untouched.

> **Note (see research.md R2):** an earlier approach populated `IrClassExtractor.NestedTypes` in the target-agnostic core to wake the scanner's existing nested recursion. Dual-agent review caught that this regressed the Dart target (one-file-per-type → a child's `HashCode` folded into the parent file) and over-approximated emission for `[Import]` nested types. The shipped fix moves the collection into the TS target, where the "which nested types share the parent's file" knowledge belongs.

A golden test for the companion-namespace pattern (previously **zero** coverage — no fixture contained a `namespace` block) locks the behavior. The previously failing Vigiata output type-checks after regeneration.

## Technical Context

**Language/Version**: C# 14 (.NET 10, preview features) for the transpiler; generated output is TypeScript (ESNext, `moduleResolution: bundler`).

**Primary Dependencies**: Roslyn 5.3.0 (`Microsoft.CodeAnalysis`); ConsoleAppFramework (CLI); `metano-runtime` (generated-code runtime, supplies `valueEquals`/`HashCode`).

**Storage**: N/A (stateless transpilation; incremental cache is unrelated to this fix).

**Testing**: TUnit via `dotnet run --project tests/Metano.Tests/` (golden/expected-output tests with inline C# compilation through `TranspileHelper`); `bun test` for runtime/sample validation; `tsc --noEmit` as the external type-check oracle for the reproduction.

**Target Platform**: TypeScript target (`Metano.Compiler.TypeScript`). The Dart target is shape-only and unaffected, though the core IR fix benefits it for free.

**Project Type**: Compiler / transpiler (target-agnostic core + per-language adapters).

**Performance Goals**: No regression. Populating `NestedTypes` adds bounded extra IR extraction proportional to the count of nested types — negligible against existing per-type extraction.

**Constraints**: No new public attribute or CLI surface; no behavioral change to files without nested variants; exactly one import per symbol per module (no duplicates); no spurious runtime-helper imports for variants with only strict fields.

**Scale/Scope**: Two production edits (one core, one TS target), one new golden test (+ expected `.ts` fixture), and regeneration of any affected samples. The Vigiata project is a downstream consumer only — out of scope for source edits.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment | Verdict |
|-----------|------------|---------|
| I. Clean Code as the Baseline | Both edits are small and single-purpose; the nested scan reuses existing predicates/dispatcher (no duplicated gate or `TypeKind` switch); CSharpier + warnings-as-errors enforced. | PASS |
| II. Expressive, Intention-Revealing Code | Uses existing domain vocabulary (`ScanTypeAndEmittedNested`, `IsExportableNestedType`, `CollectFromTopLevel`, companion namespace). No new concepts introduced. | PASS |
| III. Screaming, Feature-Semantic Organization | No new folders; both edits land in the existing `Transformation/` capability folder of the TS target. | PASS |
| IV. Clean Architecture via Ports & Adapters | Both halves are **target** concerns — which symbols a generated file imports and which nested companions share its file — so both land in `Metano.Compiler.TypeScript`; the target-agnostic core is untouched. (The reversal from the core-edit approach was driven precisely by this principle — see research.md R2.) | PASS |
| V. Developer Experience First | Adds the first golden tests for the companion-namespace pattern (constitution mandates golden tests accompany transpiler behavior), including negative controls for the strict-only and `[Import]` cases; reproduction documented in quickstart. | PASS |
| VI. Pragmatism Over Dogma | The fix **reuses existing machinery** (`IsExportableNestedType`, `GetOrExtractIr`) rather than adding speculative abstraction or broadening core IR for every type. No new ports. | PASS |
| Spec as source of truth | Corrects the implementation of baseline **FR-028** (imports consistent with symbol origin) and **FR-041** (runtime-helper imports), under record/union emission **FR-006/FR-013**; the silent emission of incorrect output also touches **FR-040** (fail-or-correct). See research R4. | PASS |

**Result**: No violations. Complexity Tracking is intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/006-fix-nested-variant-imports/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — decisions R1–R4
├── data-model.md        # Phase 1 — affected IR/AST entities
├── quickstart.md        # Phase 1 — reproduce & validate walkthrough
├── contracts/
│   └── import-completeness.md   # Phase 1 — the import-emission invariant + golden expectation
└── checklists/
    └── requirements.md  # Spec quality checklist (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── Metano.Compiler/
│   ├── Extraction/
│   │   └── IrClassExtractor.cs            # unchanged (NestedTypes stays null) — see research.md R2
│   └── Analysis/
│       ├── IrRuntimeRequirementScanner.cs # unchanged — its NestedTypes recursion stays dormant
│       └── IrEqualityClassifier.cs        # unchanged — single source of truth for strict/value
└── Metano.Compiler.TypeScript/
    ├── Transformation/
    │   ├── ImportCollector.cs             # EDIT: walk ns.Members in TsNamespaceDeclaration — defect 1
    │   └── TypeTransformer.cs             # EDIT: ScanTypeAndEmittedNested scans emitted nested types — defect 2
    ├── Bridge/
    │   └── IrToTsRecordSynthesisBridge.cs # emits valueEquals call (FieldEquality) — read-only context
    └── TypeScript/
        ├── AST/TsNamespaceDeclaration.cs  # Functions + Members shape (read-only context)
        └── Printer.cs                     # prints both Functions and Members (read-only context)

tests/
└── Metano.Tests/
    ├── <new>NestedRecordVariantTests.cs   # NEW: golden test for the companion-namespace pattern
    └── Expected/
        └── <new>*.ts                      # NEW: expected output fixture(s)
```

**Structure Decision**: Single-project-per-target layout already in place. The fix is split across the two layers that own each concern (core IR extraction; TS import emission), consistent with Principle IV. No structural change.

## Complexity Tracking

> No constitution violations — no entries required.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
