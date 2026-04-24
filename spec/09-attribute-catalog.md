# Attribute Catalog

This appendix lists the currently available Metano annotations relevant to the
transpilation model. The current codebase exposes **25 attribute types** in
`Metano.Annotations`, plus the supporting `EmitTarget` enum used by some
annotations.

## Type Selection and Inclusion

| Attribute | Purpose |
| --- | --- |
| `TranspileAttribute` | Marks an individual type for transpilation. |
| `TranspileAssemblyAttribute` | Marks an assembly for assembly-wide transpilation. |
| `NoTranspileAttribute` | Excludes a type from transpilation. |
| `NoEmitAttribute` | Paints a type as .NET-only — no file emitted and no transpiled code may reference it (MS0013 `NoEmitReferencedByTranspiledCode`). |

## Naming and Emission Shape

| Attribute | Purpose |
| --- | --- |
| `NameAttribute` | Overrides emitted type/member names. |
| `IgnoreAttribute` | Omits a member from output. |
| `StringEnumAttribute` | Emits enum output as string-based TS representation. |
| `PlainObjectAttribute` | Emits object shape without class wrapper semantics. |
| `BrandedAttribute` | Emits wrapper types using branded/opaque-style semantics. Renames `InlineWrapperAttribute` (deprecated). |
| `EmitInFileAttribute` | Co-locates multiple types in a single output file. |

## Modules and Top-Level Emission

| Attribute | Purpose |
| --- | --- |
| `ErasableAttribute` | Marks a static class whose scope vanishes at the call site. Members emit according to their own attributes (plain body → top-level function, `[External]` → ambient, `[Emit(...)]` → template, `[Inline]` → expansion, `[Ignore]` → dropped). Subsumes `ExportedAsModuleAttribute` (deprecated) and fixes the call-site flatten pass. |
| `ExportedAsModuleAttribute` | **Deprecated** — use `ErasableAttribute`. Kept for one release cycle. |
| `ModuleEntryPointAttribute` | Promotes a method body to top-level module statements. |
| `ModuleAttribute` | Declares module-related emission metadata. |
| `ExportVarFromBodyAttribute` | Promotes a variable declared in a module entry body into module export surface. |

## Type Safety and Validation

| Attribute | Purpose |
| --- | --- |
| `GenerateGuardAttribute` | Generates a runtime `isT` type guard plus a throwing `assertT(value, message?)` companion that wraps it. |
| `DiscriminatorAttribute` (TypeScript) | Names a `[StringEnum]` field as the discriminator; the generated `isT` short-circuits on a literal comparison against the type name before walking the remaining shape. |
| `ExternalAttribute` (TypeScript) | Marks a class or member as runtime-provided. No declaration emitted. Class-level `[External]` keeps class-qualified access (`Foo.bar` stays `Foo.bar`); flatten semantics moved to `[Erasable]`. Member-level `[External]` suppresses the declaration only. |
| `ConstantAttribute` | Applied to a parameter or field; the value must be a compile-time constant literal. Violations surface as MS0014 `InvalidConstant`. Enables literal-type narrowing in `[Emit]` templates and safe `[Inline]` expansion. |
| `InlineAttribute` | Applied to a static readonly field, an expression-bodied method / extension, or a static class. Field: initializer substitutes at every access. Method: body substitutes at every call with parameter renaming. Class: propagates `[Inline]` to every contained member. |

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
