# Phase 1 Data Model: Complete imports for nested record variants

This feature touches existing IR/AST nodes and one analysis result. No new types are introduced; one IR field changes from always-null to populated.

## Affected entities

### IrClassDeclaration.NestedTypes (core IR) — `Metano.Compiler/IR` — UNCHANGED

Stays `null` (the shipped fix is Strategy B; see research.md R2). It is **not** the seam used.
`IrRuntimeRequirementScanner.ScanClass`'s `NestedTypes` recursion (`:83-85`) therefore stays
dormant, exactly as before this feature — populating it in the shared core regressed the Dart
target, so the nested scan was moved into the TS target instead.

### ScanTypeAndEmittedNested (TS target) — `Metano.Compiler.TypeScript/Transformation`

The new seam. A recursive driver that accumulates runtime requirements for a type plus the
nested companions the TS target emits into the same file.

| Aspect | Behavior |
|--------|----------|
| Skip gate | `HasImport` / `HasIgnore(TypeScript)` / entry-point — applied uniformly to top-level and nested types |
| Own requirements | `IrRuntimeRequirementScanner.Scan(GetOrExtractIr(type))` |
| Recursion | into each `nested` where `IsExportableNestedType(nested)` (the exact emission gate, reused) |

- **Validation rule**: the recursion set equals what `TransformNestedTypes` emits, because both
  gate on `IsExportableNestedType` — no spurious requirement from a non-emitted nested type.
- **Relationship**: feeds the `acc` set consumed by `ImportCollector` via the requirement→import
  bridge. Requirements are a `HashSet`, so a helper shared by parent and variants collapses to one import.

### TsNamespaceDeclaration (TS AST) — `Metano.Compiler.TypeScript/TypeScript/AST`

Companion namespace emitted next to a discriminated-union base type.

| Field | Role | Walker coverage today | After fix |
|-------|------|-----------------------|-----------|
| `Functions` | Branded companions | Walked (`ImportCollector.cs:742-748`) | Walked via unified `CollectFromTopLevel(func)` |
| `Members` | Nested type companions (variant classes) | **Not walked** | Walked via `CollectFromTopLevel(member)` |

- **Validation rule**: every symbol referenced by any node in `Members` must end up imported (import-completeness invariant; see contract).
- **Relationship**: `Members` is produced by `TypeTransformer.TransformNestedTypes` (`:174-181`) and printed by `Printer.cs`.

### IrRuntimeRequirement (analysis result) — `Metano.Compiler/IR`

A semantic, target-agnostic dependency descriptor (e.g., `("HashCode", Hashing)`, `("ValueEquals", Equality)`).

- **Derivation**: `IrRuntimeRequirementScanner.Scan(typeIr)` accumulates a `HashSet<IrRuntimeRequirement>` over the type and (after fix) its nested types.
- **Mapping**: the TS target converts each requirement to a `metano-runtime` import (`IrRuntimeRequirementToTsImport`).
- **Invariant**: it is a **set** — duplicates across top level and variants collapse automatically (supports SC-005 dedup).

### Variant class (conceptual) — generated `TsClass` inside `Members`

One per nested record. Carries a constructor (variant fields), `equals`/`hashCode`/`with`. References that may require imports: the field's type (intra-project or cross-package), `valueEquals` (non-strict field), `HashCode`, and any guard/helper.

## Equality classification (unchanged, central)

`IrEqualityClassifier.UseStrictEquality(irType)` is the single source of truth consumed by **both**:
- the emitter (`IrToTsRecordSynthesisBridge.FieldEquality`, `:89-96`) — decides `===` vs `valueEquals(...)`, and
- the scanner (`IrRuntimeRequirementScanner.HasNonStrictField`, `:115-123`) — decides whether to add `ValueEquals`.

Keeping classification centralized is what makes "the import lands iff the call is emitted" hold for nested variants once `NestedTypes` is populated.
