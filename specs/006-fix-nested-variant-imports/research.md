# Phase 0 Research: Complete imports for nested record variants

All decisions below are grounded in direct reads of the current source (file:line cited). No NEEDS CLARIFICATION remained from the spec.

## R1 — Fix for missing type-reference imports (defect 1)

**Decision**: In `ImportCollector.CollectFromTopLevel`, extend the `case TsNamespaceDeclaration ns` to also recurse `CollectFromTopLevel` over `ns.Members`, and route the existing `ns.Functions` walk through the same recursive entry point.

**Evidence**:
- `src/Metano.Compiler.TypeScript/Transformation/ImportCollector.cs:741-749` — the `TsNamespaceDeclaration` case iterates only `ns.Functions`; `ns.Members` is never visited.
- `src/Metano.Compiler.TypeScript/TypeScript/AST/TsNamespaceDeclaration.cs:8-13` — the node carries both `Functions` and `Members` (`IReadOnlyList<TsTopLevel>? Members`).
- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs:174-181` — nested variants are packed into `Members`, with `Functions: []`.
- The `TsClass` case (`ImportCollector.cs:750+`) already collects constructor parameter types, member signatures, method bodies, `extends`/`implements`, value names, and cross-package origins. Recursing into `Members` reuses this complete path for every variant.

**Rationale**: The variant classes are ordinary `TsClass` nodes living in `ns.Members`; the printer emits them (`Printer.cs` prints both lists). Walking `Members` through the same recursion the top level uses guarantees parity across all symbol kinds (intra-project types, cross-package origins, value references, guards, extension helpers) at any nesting depth — not a `UserProfile`-specific patch. Routing `Functions` through `CollectFromTopLevel(func, sink)` (the `TsFunction` case at `:730-736`) is a strict superset of the current inline walk because it additionally collects function `TypeParameters`; this removes duplicated traversal logic (Clean Code).

**Alternatives considered**:
- *Targeted patch — collect only the variant constructor parameter type.* Rejected: leaves the same class of bug latent for cross-package types, value references, guards, and helpers used only in variants (spec US2).
- *Resolve `valueEquals` here too.* Rejected: the walker has no resolution branch for pure runtime helpers (names like `valueEquals` fall through the resolution loop at `ImportCollector.cs:308-493` and are discarded). Runtime helpers are owned by the requirement scanner — see R2. Mixing them in would duplicate ownership.

## R2 — Fix for missing runtime-helper import `valueEquals` (defect 2)

> **REVISED after dual-agent review (Strategy A → Strategy B).** The original decision below
> (populate `IrClassDeclaration.NestedTypes` in the core `IrClassExtractor`) was implemented,
> reviewed, and **rejected** because it regressed the multi-target architecture. The shipped
> fix is Strategy B. The original analysis is retained for the record; the reversal rationale
> follows it.

### Shipped decision (Strategy B)

Leave the core untouched (`IrClassExtractor` keeps `NestedTypes: null`). Collect the
runtime requirements of emitted nested companions **in the TypeScript target's driver**:
`TypeTransformer.ScanIrRuntimeRequirements` delegates to a recursive helper
`ScanTypeAndEmittedNested` that (a) applies the same `HasImport` / `HasIgnore(TypeScript)` /
entry-point skips uniformly to top-level **and** nested types, (b) scans each type's own IR via
the existing `GetOrExtractIr`, and (c) recurses only into nested types where the existing
`IsExportableNestedType` gate is true. It reuses the target's emission predicate and dispatcher
verbatim — no duplicated gate, no duplicated `TypeKind` switch.

**Why Strategy A was reversed** (both found by the review and verified in code):
1. **Dart regression.** The core scanner's `NestedTypes` recursion folds a nested type's
   requirements into the **parent's** accumulated set. That is correct only when the target
   emits the nested type into the parent's file. The TS target does (companion namespace via
   `TransformNestedTypes`); the **Dart** target does not — it emits one file per type and resets
   `runtimeRequirements` per file (`DartTransformer.cs:115,205`), flattening nested types into
   their own files (`:223-233`). Populating `NestedTypes` in the shared core extractor made the
   Dart parent file import a child's `HashCode` (an unused-import the Dart analyzer flags). The
   "benefits Dart for free" premise was inverted.
2. **`[Import]` over-approximation.** The core gate used `SymbolHelper.IsTranspilable`, which
   does not exclude `[Import]`. But `BuildTypeStatements` (`TypeTransformer.cs:505`) and the
   top-level scan driver (`:941`) both skip `[Import]` types. A nested `[Import]` variant with a
   non-strict field would therefore pull a spurious `valueEquals` (a hard error under
   `noUnusedLocals`). Strategy B closes this by applying the same `[Import]`/`[Ignore]` skip to
   nested types.

**Root cause of A's flaw**: whether a nested type's helpers belong in the parent's file is a
**target** decision (file layout), so the collection must live in the target, not the
target-agnostic core. This realizes Principle IV better than A did.

### Original decision (Strategy A — NOT shipped)

Populate `IrClassDeclaration.NestedTypes` in `IrClassExtractor` with the transpilable nested
types of the type being extracted, replacing the unconditional `NestedTypes: null`. This
activates the **already-present** recursion in `IrRuntimeRequirementScanner.ScanClass`.

**Evidence**:
- `src/Metano.Compiler/Extraction/IrClassExtractor.cs:84` — `NestedTypes: null` is hard-coded in the returned `IrClassDeclaration`.
- `src/Metano.Compiler/Analysis/IrRuntimeRequirementScanner.cs:83-85` — `ScanClass` already does `if (c.NestedTypes is not null) foreach (var nested in c.NestedTypes) ScanType(nested, acc);`. Dead today only because the field is always null.
- `src/Metano.Compiler/Analysis/IrRuntimeRequirementScanner.cs:94-111` — a record with a non-strict constructor field adds the `ValueEquals` requirement; the strict/value decision is delegated to `IrEqualityClassifier.UseStrictEquality` (`HasNonStrictField`, lines 115-123).
- `src/Metano.Compiler.TypeScript/Bridge/IrToTsRecordSynthesisBridge.cs:89-96` — `FieldEquality` emits `valueEquals(...)` for non-strict fields and explicitly mirrors `IrEqualityClassifier` so the scanner and the emitter cannot drift.
- `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs:932-962` — the driver `ScanIrRuntimeRequirements` extracts IR per top-level `group.Types` and calls `IrRuntimeRequirementScanner.Scan(typeIr)`. With `NestedTypes` populated, scanning the top-level `GetUserProfileResponseData` IR transitively reaches `UserProfileLoaded` and yields `{HashCode, ValueEquals}` — exactly the required set.

**Blast-radius check**: `grep -rn "\.NestedTypes" src/` returns reads at **only** `IrRuntimeRequirementScanner.cs:83-84`. Every other `GetOrExtractIr` consumer in the TS target (`TypeTransformer.cs:575,588,601,614,1562,1583,1603,1625`) reads other IR fields, never `NestedTypes`. Populating the field therefore activates the intended recursion and changes nothing else. The TS emission of variants stays driven by `TransformNestedTypes` over Roslyn symbols (`TypeTransformer.cs:155-182`), so there is no double-emission.

**Filter-alignment requirement**: `NestedTypes` MUST contain exactly the nested types that the target emits, otherwise a non-emitted nested type with a non-strict field would pull a spurious `valueEquals` import (violating SC-005 / FR-005). The core already gates discovery with `SymbolHelper.IsTranspilable`; nested extraction MUST use the same gate so it is a consistent subset of/identical to the TS `IsExportableNestedType` predicate (`TypeTransformer.cs:184+`). This invariant is captured as a contract (see `contracts/import-completeness.md`) and asserted by the golden test.

**Rationale**: `NestedTypes: null` is simply *incomplete IR* — a class that declares nested types should carry them. The scanner recursion was written in anticipation of this. Completing the IR is the principled, non-speculative fix (Principle VI), lives in the target-agnostic core where IR shape belongs (Principle IV), keeps `IrEqualityClassifier` as the single source of truth, and benefits the Dart target for free.

**Alternatives considered**:
- *Strategy B — scan nested symbols only inside the TS driver `ScanIrRuntimeRequirements`.* Rejected: it leaves the core scanner recursion dead (Clean Code smell), is target-local (Dart would need the same fix again), and duplicates traversal the core already expresses. It would precisely match emission, but the filter-alignment requirement above closes that gap for Strategy A without the duplication.
- *Emit the `valueEquals` import from the import walker (R1 path).* Rejected for the ownership reason in R1: runtime helpers are scanner-owned; the walker has no resolution branch for them.
- *Recurse `Scan` from the parent without populating IR.* Rejected: the scanner intentionally consumes IR, not Roslyn symbols (it is core, compilation-context-free). Feeding it complete IR is the contract it expects.

## R3 — Test strategy

**Decision**: Add a TUnit golden test that compiles, via `TranspileHelper.Transpile`, an abstract record with `[StrictUnionGuard]` plus two nested record variants — one empty and one carrying a field whose type is a separate transpilable record in the same namespace (renamed via `[Name]` to mirror the reproduction) — and assert the full expected `.ts` for the variant file, including:
- the intra-project import of the referenced type, and
- the `valueEquals` import from `metano-runtime` alongside `HashCode`.

Add a negative-control assertion: a sibling case whose variant has only strict (primitive) fields MUST NOT import `valueEquals`.

**Evidence**: No expected fixture under `tests/Metano.Tests/Expected/` currently contains a `namespace` block — the companion-namespace pattern is entirely uncovered. The reproduction was confirmed externally with `tsc --noEmit` producing `TS2304` for both `UserProfile` and `valueEquals`.

**Rationale**: Golden tests give exact, diffable regression protection (Principle V; constitution Quality Gates). The negative control guards FR-005 (no spurious helper import). The test must fail on today's transpiler and pass after the fix (SC-004).

**Alternatives considered**:
- *Only assert presence of import substrings.* Rejected: weaker than the repo's golden convention and would miss dedup/ordering regressions (SC-005).
- *Rely on regenerating samples + `bun test`.* Kept as an additional end-to-end check, but not a substitute for an inline golden test that pins the minimal reproduction.

## R4 — Spec traceability (constitution gate)

**Decision**: Record this fix as correcting the implementation of baseline **FR-028** ("generate imports consistent with symbol origin") and **FR-041** ("runtime helper imports"), within record/discriminated-union emission **FR-006/FR-013**; note that the silent emission of non-importing output also relates to **FR-040** ("fail explicitly when it cannot generate correct output").

**Evidence**: The live baseline references FR ranges in `specs/001-project-baseline-evolution/baseline/.identifier-inventory.md` (FR-027–FR-031 Output incl. imports; FR-041–FR-045 advanced emission incl. runtime helpers). Verbatim FR text was retired with `old-spec/` and is recoverable read-only from git history (`2ed272f^:old-spec/04-functional-requirements.md`). There is no FR that literally mandates "generated TS must type-check"; the nearest are FR-040 and NFR-007 (TS-ecosystem compatibility).

**Rationale**: Satisfies the "Spec as source of truth" gate — this is a bug fix referencing existing FRs, not new product behavior, so no spec change request is required. If desired, FR-028's wording could later be tightened to say "regardless of nesting depth," tracked as documentation debt.
