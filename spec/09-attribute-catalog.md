# Attribute Catalog

This appendix lists the currently available Metano annotations relevant to the
transpilation model. The current codebase exposes **21 attribute types** in
`Metano.Annotations`, plus the supporting `EmitTarget` enum used by some
annotations.

## Type Selection and Inclusion

| Attribute | Purpose |
| --- | --- |
| `TranspileAttribute` | Marks an individual type for transpilation. |
| `TranspileAssemblyAttribute` | Marks an assembly for assembly-wide transpilation. |
| `NoTranspileAttribute` | Excludes a type from transpilation. |
| `NoEmitAttribute` | Keeps a symbol available for resolution without emitting a file. |

## Naming and Emission Shape

| Attribute | Purpose |
| --- | --- |
| `NameAttribute` | Overrides emitted type/member names. |
| `IgnoreAttribute` | Omits a member from output. |
| `StringEnumAttribute` | Emits enum output as string-based TS representation. |
| `PlainObjectAttribute` | Emits object shape without class wrapper semantics. |
| `InlineWrapperAttribute` | Emits wrapper types using branded/opaque-style semantics. |
| `EmitInFileAttribute` | Co-locates multiple types in a single output file. |

## Modules and Top-Level Emission

| Attribute | Purpose |
| --- | --- |
| `ExportedAsModuleAttribute` | Emits a static class as a module surface. |
| `ModuleEntryPointAttribute` | Promotes a method body to top-level module statements. |
| `ModuleAttribute` | Declares module-related emission metadata. |
| `ExportVarFromBodyAttribute` | Promotes a variable declared in a module entry body into module export surface. |

## Type Safety and Validation

| Attribute | Purpose |
| --- | --- |
| `GenerateGuardAttribute` | Generates a runtime `isT` type guard plus a throwing `assertT(value, message?)` companion that wraps it. |

## Packaging and Interop

| Attribute | Purpose |
| --- | --- |
| `EmitPackageAttribute` | Declares npm package identity for emitted output. |
| `ImportAttribute` | Maps a C# facade to an external JS/TS module import. |
| `ExportFromBclAttribute` | Exposes selected BCL-mapped behavior into emitted output. |

## Declarative Lowering and Mapping

| Attribute | Purpose |
| --- | --- |
| `EmitAttribute` | Injects declarative JS/TS template-based output. |
| `MapMethodAttribute` | Declares method-level lowering rules. |
| `MapPropertyAttribute` | Declares property-level lowering rules. |

## Notes

- This catalog is product-oriented, not a replacement for
  [`docs/attributes.md`](../docs/attributes.md), which remains the explanatory
  reference.
- `EmitTarget` is intentionally not listed as an attribute because it is a
  supporting enum, not an annotation type.
- Availability in the codebase does not imply every combination is valid; some
  attribute interactions are constrained and diagnosed explicitly.
