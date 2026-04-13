# Diagnostic Catalog

Metano exposes a stable diagnostic catalog intended for troubleshooting,
automation, and test traceability.

## Diagnostic Model

Each diagnostic carries:

- severity;
- stable code;
- message;
- optional source location.

The current stable code range is **`MS0001` through `MS0008`**.

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
