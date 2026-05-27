# Diagnostic Catalog (Baseline)

Metano's stable diagnostic contract. Source of truth: the diagnostic-code constants in the codebase
(emitted via `src/Metano.Compiler/Diagnostics/`). Migrated from `old-spec/10-diagnostic-catalog.md` and
**corrected against the code**.

> Drift corrected during migration: the actual range is **`MS0001`–`MS0025`** (25 codes). The legacy
> catalog stopped at `MS0022`; FR-039 said "through `MS0024`"; the legacy feature-support matrix said
> "`MS0001`-`MS0008`". All are superseded by this table. New since the legacy catalog: `MS0023`,
> `MS0024`, `MS0025`.

## Diagnostic model

Each diagnostic carries: severity, stable code, message, optional source location. Stable codes are
part of the transpiler contract (ADR-0010) and are searchable across docs, tests, issues, and ADRs.

## Stable codes (`MS0001`–`MS0025`)

| Code | Symbolic name | Meaning |
| --- | --- | --- |
| `MS0001` | `UnsupportedFeature` | A C# language feature is not supported by the transpiler. |
| `MS0002` | `UnresolvedType` | A referenced type could not be resolved or is not transpileable. |
| `MS0003` | `AmbiguousConstruct` | An ambiguous construct may produce incorrect output. |
| `MS0004` | `ConflictingAttributes` | Conflicting attributes are present on a single symbol. |
| `MS0005` | `CyclicReference` | A cyclic reference exists between generated target files. |
| `MS0006` | `InvalidModuleEntryPoint` | Invalid `[ModuleEntryPoint]` (signature or conflicting setup). |
| `MS0007` | `CrossPackageResolution` | Cross-package resolution failure (missing/divergent package identity). |
| `MS0008` | `EmitInFileConflict` | Conflicting `[EmitInFile]` grouping makes output placement ambiguous. |
| `MS0009` | `FrontendLoadFailure` | Source frontend failed to load or compile the project. |
| `MS0010` | `OptionalRequiresNullable` | `[Optional]` applied to a non-nullable parameter/property. |
| `MS0011` | `InvalidDiscriminator` | `[Discriminator]` references a missing / non-`[StringEnum]` / nullable field. |
| `MS0012` | `InvalidExternal` | `[External]` on a concrete non-static class, or combined with `[Transpile]`. |
| `MS0013` | `IgnoreReferencedByTranspiledCode` | Transpilable code references a type marked `[Ignore]` for the active target. |
| `MS0014` | `InvalidConstant` | `[Constant]` argument/initializer is not a compile-time constant literal. |
| `MS0015` | `InvalidErasable` | `[NoContainer]` on a non-static class, or combined with `[Transpile]`. |
| `MS0016` | `InvalidInline` | `[Inline]` applied to an unsupported shape. |
| `MS0017` | `InterfacePrefixCollision` | Stripping the `I` prefix would collide with another top-level type. |
| `MS0018` | `InvalidThis` | `[This]` outside the first positional parameter, or combined with `ref`/`out`/`params`. |
| `MS0019` | `GenericNewConstraint` | `new()`-constraint instantiation produces invalid target code (generics erased at runtime). |
| `MS0020` | `ErasableFactoryNameClash` | A `[NoContainer]` factory's emitted name collides within the emit scope. |
| `MS0021` | `ExtensionHelperNameClash` | Two extension members resolve to the same emitted helper name. |
| `MS0022` | `AliasedImportConflict` | A local declaration shadows an import; an alias was synthesized (Info; pin with `[ImportAlias]`). |
| `MS0023` | `InvalidEmit` | `[Emit]` template is invalid (bad placeholder/shape). |
| `MS0024` | `UnsupportedQueryableBody` | A `[Queryable]` lambda body uses an unsupported construct for expression-tree capture. |
| `MS0025` | `AssetCopyFailure` | A `<MetanoAsset>` copy failed (missing source, path escape, or I/O error). |

## Related

- `docs/adr/0010-metano-diagnostics.md`
- [feature-support-matrix.md](./feature-support-matrix.md) · [attribute-catalog.md](./attribute-catalog.md)
