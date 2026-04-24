# Diagnostic Catalog

Metano exposes a stable diagnostic catalog intended for troubleshooting,
automation, and test traceability.

## Diagnostic Model

Each diagnostic carries:

- severity;
- stable code;
- message;
- optional source location.

The current stable code range is **`MS0001` through `MS0016`**.

## Stable Codes

| Code | Symbolic name | Meaning |
| --- | --- | --- |
| `MS0001` | `UnsupportedFeature` | A C# language feature is not supported by the transpiler. |
| `MS0002` | `UnresolvedType` | A referenced type could not be resolved or is not transpileable. |
| `MS0003` | `AmbiguousConstruct` | An ambiguous construct may produce incorrect output. |
| `MS0004` | `ConflictingAttributes` | Conflicting attributes are present on a single symbol. |
| `MS0005` | `CyclicReference` | A cyclic reference exists between generated TypeScript files. |
| `MS0006` | `InvalidModuleEntryPoint` | Invalid use of `[ModuleEntryPoint]`, including incompatible signature or conflicting setup. |
| `MS0007` | `CrossPackageResolution` | Cross-package resolution failure, including missing or divergent package identity metadata. |
| `MS0008` | `EmitInFileConflict` | Conflicting `[EmitInFile]` grouping would make output placement ambiguous. |
| `MS0009` | `FrontendLoadFailure` | Source frontend failed to load or compile the project. |
| `MS0010` | `OptionalRequiresNullable` | `[Optional]` was applied to a non-nullable parameter or property. |
| `MS0011` | `InvalidDiscriminator` | `[Discriminator("FieldName")]` references a field that is missing, not a `[StringEnum]`, or nullable. |
| `MS0012` | `InvalidExternal` | `[External]` is malformed — class-level use combined with `[Transpile]`, or member-level use on a symbol that is not suppressible. |
| `MS0013` | `NoEmitReferencedByTranspiledCode` | A `[NoEmit]` type is referenced from transpiled code, which breaks the `.NET-only` painting. Migrate the referenced type to `[External]` (runtime-provided) or mark the caller `[NoTranspile]`. |
| `MS0014` | `InvalidConstant` | `[Constant]` argument or initializer is not a compile-time constant literal. |
| `MS0015` | `InvalidErasable` | `[Erasable]` was applied to a non-static class, or a member inside an `[Erasable]` class cannot satisfy the emission contract (requires a body, `[External]`, `[Emit]`, `[Inline]`, or `[Ignore]`). |
| `MS0016` | `InvalidInline` | `[Inline]` is malformed — field is not `static readonly`, method has a multi-statement body, or expansion introduces a recursion cycle. |

## Product Significance

- Diagnostics are part of the transpiler contract, not incidental logging.
- Stable codes are intended to be searchable across docs, tests, issues, and
  ADRs.
- The catalog is normative for the current implementation line and should be
  updated when codes are added, retired, or redefined.

## Related References

- [`04-functional-requirements.md`](./04-functional-requirements.md)
- [`11-adr-cross-reference.md`](./11-adr-cross-reference.md)
- [`../docs/adr/0010-metano-diagnostics.md`](../docs/adr/0010-metano-diagnostics.md)
