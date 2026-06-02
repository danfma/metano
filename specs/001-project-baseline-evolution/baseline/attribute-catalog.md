# Attribute Catalog (Baseline)

Metano's customization surface. Source of truth: `src/Metano/Annotations/*.cs` (the
`Metano.Annotations` namespace) plus a few **backend-specific** attributes that live inside a target
project. Migrated from `old-spec/09-attribute-catalog.md` and **corrected against the codebase**.

> Drift corrected during migration: `Metano.Annotations` ships **27 attribute types** today (the legacy
> catalog said "26"; the CLAUDE.md quick-table said "21"). Newly surfaced here: `[ObjectArgs]`,
> `[Queryable]`, `[ImportAlias]`. Two non-attribute support enums also live in the folder
> (`EmitTarget`, `TargetLanguage`) and are intentionally not counted as attributes.

## Core attributes (`Metano.Annotations`)

### Type selection & inclusion

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[Transpile]` | Mark a single type for transpilation. | `Annotations/TranspileAttribute.cs` |
| `[TranspileAssembly]` | Transpile all public types in the assembly (opt-out via `[Ignore]`). | `Annotations/TranspileAssemblyAttribute.cs` |
| `[Ignore]` | Mark a type/member as .NET-only (all targets, or one via `[Ignore(TargetLanguage.X)]`). Ignored types do not emit and may not be referenced by transpilable code (→ MS0013). Replaces former `[NoTranspile]`/`[NoEmit]`. | `Annotations/IgnoreAttribute.cs` |

### Naming & emission shape

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[Name("x")]` | Override emitted type/member name. | `Annotations/NameAttribute.cs` |
| `[StringEnum]` | Emit enum as a TS string union instead of numeric. | `Annotations/StringEnumAttribute.cs` |
| `[PlainObject]` | Emit DTO-style object shape without class wrapper. | `Annotations/PlainObjectAttribute.cs` |
| `[Branded]` | Branded/opaque wrapper over a primitive (zero-cost nominal type). Successor of `[InlineWrapper]`. | `Annotations/BrandedAttribute.cs` |
| `[InlineWrapper]` | Predecessor of `[Branded]`; kept working. Prefer `[Branded]`. | `Annotations/InlineWrapperAttribute.cs` |
| `[EmitInFile("name")]` | Co-locate multiple types in one output file (→ MS0008 on conflict). | `Annotations/EmitInFileAttribute.cs` |
| `[ObjectArgs]` | Treat a parameter set as an object-args bag (widget/DSL-style call sites). | `Annotations/ObjectArgsAttribute.cs` |

### Modules & top-level emission

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[NoContainer]` | Static class → pure compile-time container: no file, member access drops the class qualifier. (Formerly `[Erasable]`, ADR-0017; → MS0015/MS0020 on misuse.) | `Annotations/NoContainerAttribute.cs` |
| `[ExportedAsModule]` | **Deprecated** — use `[NoContainer]`. | `Annotations/ExportedAsModuleAttribute.cs` |
| `[ModuleEntryPoint]` | Promote a method body to top-level module statements (→ MS0006 on misuse). | `Annotations/ModuleEntryPointAttribute.cs` |
| `[Module]` | Declare module-related emission metadata. | `Annotations/ModuleAttribute.cs` |
| `[ExportVarFromBody]` | Promote a local from an entry-point body into the module export surface. | `Annotations/ExportVarFromBodyAttribute.cs` |

### Type safety & lowering

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[GenerateGuard]` | Emit `isT` narrowing predicate + `assertT(value, message?)` throwing companion. | `Annotations/GenerateGuardAttribute.cs` |
| `[Constant]` | Parameter/field must be a compile-time constant literal (→ MS0014). Enables literal narrowing in `[Emit]`/`[Inline]`. | `Annotations/ConstantAttribute.cs` |
| `[Inline]` | Substitute a `static readonly` field / expression getter / single-expression method body at each call site (`Materialize` or `Substitute` mode; → MS0016). | `Annotations/InlineAttribute.cs` |
| `[This]` | First parameter becomes the synthetic JS `this` receiver (`(this: T, …) => R`; → MS0018). | `Annotations/ThisAttribute.cs` |
| `[Queryable]` | Capture a lambda body as an expression tree alongside the closure (IQueryable provider input; → MS0024 on unsupported body). | `Annotations/QueryableAttribute.cs` |

### Packaging & interop

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[EmitPackage]` | Declare npm package identity for emitted output (SSOT for cross-package imports, ADR-0011). | `Annotations/EmitPackageAttribute.cs` |
| `[Import]` | Map a C# facade to an external JS/TS module import. | `Annotations/ImportAttribute.cs` |
| `[ImportAlias]` | Pin a deterministic alias when a local declaration shadows an import (silences MS0022). | `Annotations/ImportAliasAttribute.cs` |
| `[ExportFromBcl]` | Expose selected BCL-mapped behavior into emitted output. | `Annotations/ExportFromBclAttribute.cs` |

### Declarative lowering & mapping

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[Emit("$0.foo($1)")]` | Inline a JS/TS template at the call site with arg placeholders (→ MS0023). | `Annotations/EmitAttribute.cs` |
| `[MapMethod]` | Declarative BCL method → JS method/template mapping. | `Annotations/MapMethodAttribute.cs` |
| `[MapProperty]` | Declarative BCL property → JS property/template mapping. | `Annotations/MapPropertyAttribute.cs` |

### Support enums (not attributes)

`EmitTarget` (`Annotations/EmitTarget.cs`) and `TargetLanguage` (`Annotations/TargetLanguage.cs`).

## Backend-specific attributes (TypeScript)

These are defined inside the TypeScript backend, not in `Metano.Annotations`:

| Attribute | Purpose |
| --- | --- |
| `[Discriminator("Field")]` | Names a `[StringEnum]` discriminator; generated `isT` short-circuits on a literal compare before walking the shape (→ MS0011). |
| `[External]` | Ambient runtime-provided shape — no file emitted (→ MS0012). Combine with `[NoContainer]` for runtime globals. |
| `[StrictUnionGuard]` | On an abstract base, dispatch per-variant shape validation via the runtime `UnionGuardRegistry` (avoids ESM cycles). |
| `[JsTuple]` | On a positional record — lower to a JS array-tuple (array-shape sibling of `[PlainObject]`): tuple type alias `= [T0, T1]` standalone, erased with `[Import]`; `new`→array literal; positional member access → `[i]` (→ MS0027). |
| `[JsCallable]` | On an interface — erased JS callable; `Invoke(...)` lowers to direct receiver invocation (`recv.Invoke(a)`→`recv(a)`), overloaded `Invoke` supported; no declaration emitted (→ MS0028). |

> `[ObjectArgs]`, `[Queryable]`, and `[ImportAlias]` are confirmed present in `Metano.Annotations` and
> were absent or incomplete in the legacy quick-references — recorded in the reconciliation ledger.
