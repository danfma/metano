# Diagnostic Catalog

Metano exposes a stable diagnostic catalog intended for troubleshooting,
automation, and test traceability.

## Diagnostic Model

Each diagnostic carries:

- severity;
- stable code;
- message;
- optional source location.

The current stable code range is **`MS0001` through `MS0022`**.

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
| `MS0012` | `InvalidExternal` | `[External]` was applied to a concrete non-static class, or combined with `[Transpile]`. The attribute accepts static classes, abstract classes, structs, interfaces, methods, properties, and fields. |
| `MS0013` | `IgnoreReferencedByTranspiledCode` | A transpilable type's signature or body references a type marked `[Ignore]` for the active target. `[Ignore]` paints the symbol as .NET-only; ambient TS shapes belong to `[External]` instead. |
| `MS0014` | `InvalidConstant` | `[Constant]` argument or initializer is not a compile-time constant literal. |
| `MS0015` | `InvalidErasable` | `[NoContainer]` was applied to a non-static class, or combined with `[Transpile]`. (Constant name retained for diagnostic-id stability; the user-facing attribute is `[NoContainer]`.) |
| `MS0016` | `InvalidInline` | `[Inline]` was applied to an unsupported shape (instance or mutable field, field without initializer, block-bodied property, non-static property, or any other target). |
| `MS0017` | `InterfacePrefixCollision` | Stripping the `I` prefix from an interface name (under `--strip-interface-prefix`) would collide with another top-level type in the same namespace. Keeps the prefix so the consumer surface stays compilable. |
| `MS0018` | `InvalidThis` | `[This]` (from `Metano.Annotations`) was applied outside the first positional parameter, or combined with `ref` / `out` / `params`. |
| `MS0019` | `GenericNewConstraint` | Instantiating a generic type parameter via the `new()` constraint produces invalid TypeScript because TS erases generics at runtime. |
| `MS0020` | `ErasableFactoryNameClash` | A `[NoContainer]` static method's emitted TS name (after `[Name]` resolution, otherwise camelCase) collides with the TS name of a transpilable type the same emit scope can see, or with another `[NoContainer]` factory of the same name across classes. |
| `MS0021` | `ExtensionHelperNameClash` | Two extension members declared on different static classes resolve to the same emitted TS helper name, so the import collector cannot pick which file to import from on a bare call site. |
| `MS0022` | `AliasedImportConflict` | A local declaration shadows an imported symbol; the transpiler synthesized an alias to keep both surfaces working. Pin the alias deterministically with a `using NewName = T;` directive (or `[ImportAlias]`) to silence this notice (Info severity). |

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
