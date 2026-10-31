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

## Stable codes (`MS0001`–`MS0030`)

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
| `MS0013` | `IgnoreReferencedByTranspiledCode` | Transpilable code references a type marked `[Ignore]` for the active target. Not raised for: (a) source-generator output (e.g. System.Text.Json `*.g.cs`, as reported by the build workspace) — only user-authored code is scanned; this does not filter emission, so generator-added members of a transpilable partial type may still be emitted; (b) an interface ignored for the active target listed in a transpilable type's base list — it is erased from `implements`/`extends` in every target it is ignored for and replaced by its own non-ignored base interfaces; (c) references inside an attribute application, e.g. `[JsonConverter(typeof(MyConverter<T>))]` or an `[Ignore]` attribute class — attributes are never emitted — except attributes the compiler reads for emission (`[JsonSerializable(typeof(T))]`), which still raise. Every other position (parameters, return types, fields, properties, locals, `new`, casts, `is`/`as`, `typeof` in expressions, generic constraints, type arguments — including an `[Ignore]` type argument of a base interface) still raises. |
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
| `MS0026` | `JsxRenderableUnrecognized` | A type used in a JSX-renderable position is not a component, `[JsxNativeElement]`, or imported renderable typed as `JsxElement` (or a `[JsxComponentBuilder]` render returns a non-element). |
| `MS0027` | `InvalidJsTuple` | `[JsTuple]` applied to a type with no positional shape (a record/class with no primary-constructor parameter list), or a `[JsTuple]` record that declares an instance member beyond its positional elements (it must be positional-only — the bare array tuple has no object to host extra members). |
| `MS0028` | `InvalidJsCallable` | `[JsCallable]` applied to a non-interface, or a `[JsCallable]` interface exposing a member other than `Invoke` — declared or inherited from a base interface. |
| `MS0029` | `InvalidExportsSubpathRoot` | `[EmitPackage(SubpathRoot = …)]` is not a dotted C# namespace (TypeScript producer; the build fails, consumers treat the package as having no root). |
| `MS0030` | `ExportsSubpathKeyCollision` | Under `[EmitPackage(SubpathRoot = …)]`, two namespace barrels claim one `package.json#exports` key (e.g. `--namespace-barrels` root barrel + types in the root namespace on `.`); raised by the transform before files are written. |

## Build integration (`Metano.Build`)

Raised by the MSBuild target, not by the compiler, so it lives outside the `MS` code space.

| Code | Raised by | Meaning |
| --- | --- | --- |
| `METANOBUILD001` | `Metano.Build.targets` | The `metano-typescript` .NET tool is not available; the message lists the local-manifest / global install commands and `dotnet tool restore`. Opt out with `MetanoSkipToolCheck=true` (FR-035). |

## Related

- `docs/adr/0010-metano-diagnostics.md`
- [feature-support-matrix.md](./feature-support-matrix.md) · [attribute-catalog.md](./attribute-catalog.md)
