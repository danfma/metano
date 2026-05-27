# Attribute Catalog

This appendix lists the currently available Metano annotations relevant to the
transpilation model. The current codebase exposes **26 attribute types** in
`Metano.Annotations`, plus the supporting `EmitTarget` enum used by some
annotations.

## Type Selection and Inclusion

| Attribute | Purpose |
| --- | --- |
| `TranspileAttribute` | Marks an individual type for transpilation. |
| `TranspileAssemblyAttribute` | Marks an assembly for assembly-wide transpilation. |

## Naming and Emission Shape

| Attribute | Purpose |
| --- | --- |
| `NameAttribute` | Overrides emitted type/member names. |
| `IgnoreAttribute` | Marks a type or member as .NET-only for every target, or for one target via `[Ignore(TargetLanguage.X)]`. Ignored types do not emit and may not be referenced from transpilable code (MS0013 `IgnoreReferencedByTranspiledCode`). Replaces the former `[NoTranspile]` / `[NoEmit]` split. |
| `StringEnumAttribute` | Emits enum output as string-based TS representation. |
| `PlainObjectAttribute` | Emits object shape without class wrapper semantics. |
| `BrandedAttribute` | Emits wrapper types using branded/opaque-style semantics. Successor of `InlineWrapperAttribute` — both attributes carry identical behavior while the legacy name stays supported. |
| `InlineWrapperAttribute` | Predecessor of `BrandedAttribute`; kept working for existing callers. Prefer `[Branded]` in new code. |
| `EmitInFileAttribute` | Co-locates multiple types in a single output file. |

## Modules and Top-Level Emission

| Attribute | Purpose |
| --- | --- |
| `NoContainerAttribute` | Marks a static class as a pure compile-time container: no `.ts` file emitted and every static member access drops the class qualifier (sole flatten anchor after #106). Effect is local to the decorated type — does not propagate to nested types or to `[Inline]` members. (Previously named `[Erasable]`; renamed in ADR-0017.) |
| `ExportedAsModuleAttribute` | **Deprecated** (`[Obsolete]`). Use `[NoContainer]` instead; will be removed in a future release. |
| `ModuleEntryPointAttribute` | Promotes a method body to top-level module statements. |
| `ModuleAttribute` | Declares module-related emission metadata. |
| `ExportVarFromBodyAttribute` | Promotes a variable declared in a module entry body into module export surface. |

## Type Safety and Validation

| Attribute | Purpose |
| --- | --- |
| `GenerateGuardAttribute` | Generates a runtime `isT` type guard plus a throwing `assertT(value, message?)` companion that wraps it. |
| `DiscriminatorAttribute` (TypeScript) | Names a `[StringEnum]` field as the discriminator; the generated `isT` short-circuits on a literal comparison against the type name before walking the remaining shape. |
| `ExternalAttribute` (TypeScript) | Marks an ambient runtime-provided shape — no file emitted. Accepts static classes, abstract classes, interfaces, structs, methods, properties, and fields; concrete non-static classes are rejected. Static member access stays class-qualified (`Js.document`); the flatten contract anchors exclusively on `[NoContainer]`. Combine with `[NoContainer]` on the same static class to keep the "no file + flatten" shape (`Js.cs` runtime-globals). |
| `ConstantAttribute` | Applied to a parameter or field; the value must be a compile-time constant literal. Violations surface as MS0014 `InvalidConstant`. Enables literal-type narrowing in `[Emit]` templates and safe `[Inline]` expansion. |
| `InlineAttribute` | Applied to a `static readonly` field, a `static` property with an expression-bodied getter, or a `static` method whose body is a single expression. Every reference substitutes the member's body at the call site; the declaration itself is not emitted. The optional `InlineMode` argument selects between `Materialize` (default — IIFE wrap, args evaluate once) and `Substitute` (β-reduction — args inline directly into the body, may duplicate). Class-level `[Inline]` propagates to every static member, with `Mode` cascading from the class declaration. Violations surface as MS0016 `InvalidInline`. See ADR-0017. |
| `ThisAttribute` | Applied to the first parameter of a delegate or inlinable method; promotes the slot to the synthetic JavaScript `this` receiver. The parameter is dropped from the emitted TS positional list and re-introduced as the function type's `this` annotation (`(this: T, …) => R`). Lambda / method-group / body-rewrite emission lands in a follow-up slice. Violations surface as MS0018 `InvalidThis`. |

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
