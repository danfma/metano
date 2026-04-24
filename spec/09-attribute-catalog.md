# Attribute Catalog

This appendix lists the currently available Metano annotations relevant to the
transpilation model. The current codebase exposes **24 attribute types** in
`Metano.Annotations`, plus the supporting `EmitTarget` enum used by some
annotations. Additional entries tagged *(planned)* below are reserved for the
upcoming attribute-family slices (see ADR-0015) and are not yet shipped.

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
| `BrandedAttribute` | Emits wrapper types using branded/opaque-style semantics. Successor of `InlineWrapperAttribute` — both attributes carry identical behavior while the legacy name stays supported. |
| `InlineWrapperAttribute` | Predecessor of `BrandedAttribute`; kept working for existing callers. Prefer `[Branded]` in new code. |
| `EmitInFileAttribute` | Co-locates multiple types in a single output file. |

## Modules and Top-Level Emission

| Attribute | Purpose |
| --- | --- |
| `ErasableAttribute` | Marks a static class whose scope vanishes at the call site. The class emits no `.ts` file and static member access flattens to a bare identifier. Members must already resolve without their own emission (literal returns, `[Emit]` templates, and — per ADR-0015 — upcoming `[External]` / `[Inline]` members). The broader member-emission contract and the full deprecation of `ExportedAsModuleAttribute` ship in follow-up slices. |
| `ExportedAsModuleAttribute` | Superseded by `ErasableAttribute`; remains fully functional until the follow-up slice migrates existing callers. |
| `ModuleEntryPointAttribute` | Promotes a method body to top-level module statements. |
| `ModuleAttribute` | Declares module-related emission metadata. |
| `ExportVarFromBodyAttribute` | Promotes a variable declared in a module entry body into module export surface. |

## Type Safety and Validation

| Attribute | Purpose |
| --- | --- |
| `GenerateGuardAttribute` | Generates a runtime `isT` type guard plus a throwing `assertT(value, message?)` companion that wraps it. |
| `DiscriminatorAttribute` (TypeScript) | Names a `[StringEnum]` field as the discriminator; the generated `isT` short-circuits on a literal comparison against the type name before walking the remaining shape. |
| `ExternalAttribute` (TypeScript) | Marks a `static class` as a stub for runtime-available JS globals — the class emits no file and static member access flattens to a bare identifier. Attribute usage now accepts method/property/field targets so the family can grow; the per-member declaration-suppression lowering and the split from class-level flatten ship in a follow-up slice. |
| `ConstantAttribute` | Applied to a parameter or field; the value must be a compile-time constant literal. Violations surface as MS0014 `InvalidConstant`. Enables literal-type narrowing in `[Emit]` templates and safe `[Inline]` expansion. |
| `InlineAttribute` *(planned)* | Will apply to a static readonly field, an expression-bodied method / extension, or a static class to request expansion at every use site. See ADR-0015. |

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
