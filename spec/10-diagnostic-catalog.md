# Diagnostic Catalog

Metano exposes a stable diagnostic catalog intended for troubleshooting,
automation, and test traceability.

## Diagnostic Model

Each diagnostic carries:

- severity;
- stable code;
- message;
- optional source location.

The current stable code range is **`MS0001` through `MS0018`**, with
`MS0013` reserved for the upcoming `[NoEmit]` redefinition slice
(see ADR-0015).

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
| `MS0012` | `InvalidExternal` | `[External]` was applied to a non-static class, or combined with `[Transpile]`. |
| `MS0014` | `InvalidConstant` | `[Constant]` argument or initializer is not a compile-time constant literal. |
| `MS0015` | `InvalidErasable` | `[Erasable]` was applied to a non-static class, or combined with `[Transpile]`. |
| `MS0016` | `InvalidInline` | `[Inline]` was applied to an unsupported shape (instance or mutable field, field without initializer, block-bodied property, non-static property, or any other target). |
| `MS0017` | `InterfacePrefixCollision` | Stripping the `I` prefix from an interface name (under `--strip-interface-prefix`) would collide with another top-level type in the same namespace. Keeps the prefix so the consumer surface stays compilable. |
| `MS0018` | `InvalidThis` | `[This]` (from `Metano.Annotations`) was applied outside the first positional parameter, or combined with `ref` / `out` / `params`. |

## Reserved Codes

The following codes are reserved for the follow-up slices of the
attribute-family roadmap (see ADR-0015) and are **not** yet implemented
in the compiler. They are listed here so the numbering range stays
stable across the stack, not as a promise of shipped behavior.

| Code | Symbolic name | Slice |
| --- | --- | --- |
| `MS0013` | `NoEmitReferencedByTranspiledCode` | `[NoEmit]` redefinition (painting diagnostic) |

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
