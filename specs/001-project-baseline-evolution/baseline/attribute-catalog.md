# Attribute Catalog (Baseline)

Metano's customization surface. Source of truth: `src/Metano/Annotations/*.cs` (the
`Metano.Annotations` namespace) plus a few **backend-specific** attributes that live inside a target
project. Migrated from `old-spec/09-attribute-catalog.md` and **corrected against the codebase**.

> Drift corrected during migration: `Metano.Annotations` ships **28 attribute types** today (the legacy
> catalog said "26"; the CLAUDE.md quick-table said "21"). Newly surfaced here: `[ObjectArgs]`,
> `[Queryable]`, `[ImportAlias]`. Two non-attribute support enums also live in the folder
> (`EmitTarget`, `TargetLanguage`) and are intentionally not counted as attributes.

## Core attributes (`Metano.Annotations`)

### Type selection & inclusion

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[Transpile]` | Mark a single type for transpilation. | `Annotations/TranspileAttribute.cs` |
| `[TranspileAssembly]` | Transpile all public types in the assembly (opt-out via `[Ignore]`). | `Annotations/TranspileAssemblyAttribute.cs` |
| `[Ignore]` | Mark a type/member as .NET-only (all targets, or one via `[Ignore(TargetLanguage.X)]`). Ignored types do not emit and may not be referenced by transpilable code (→ MS0013), except that an interface ignored for the active target in a transpilable type's base list is erased from `implements`/`extends` in every target it is ignored for (replaced by its non-ignored base interfaces), references inside attribute applications (e.g. `[JsonConverter(typeof(X))]`) are allowed because attributes are never emitted — `[JsonSerializable(typeof(X))]`, which drives emission, still raises — `nameof` arguments are allowed (they fold to a string constant), and source-generator output is not scanned. Replaces former `[NoTranspile]`/`[NoEmit]`. | `Annotations/IgnoreAttribute.cs` |

### Naming & emission shape

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[Name("x")]` | Override emitted type/member name. On one overload of a dispatched group (constructor, method, operator, module function) it pins only that overload's suffix (`[Name("cents")]` → `fromCents`, `byMoneyCents`, `addCents`); the dispatcher keeps the C# name; an override or implementation takes its root's (spec 013 FR-005); an invalid value is `MS0003`. A conversion operator's `[Name]` names its group; on `[External]`, `[JsCallable]` and `[PlainObject]` overloads and for the Dart target (until 013g) it renames the member. | `Annotations/NameAttribute.cs`, `Extraction/OverloadSuffixPin.cs`, `Extraction/OverloadEmittedName.cs` |
| `[StringEnum]` | Emit enum as a TS string union instead of numeric. | `Annotations/StringEnumAttribute.cs` |
| `[PlainObject]` | Emit DTO-style object shape without class wrapper. | `Annotations/PlainObjectAttribute.cs` |
| `[Branded]` | Branded/opaque wrapper over a primitive (zero-cost nominal type). Successor of `[InlineWrapper]`. | `Annotations/BrandedAttribute.cs` |
| `[InlineWrapper]` | Predecessor of `[Branded]`; kept working. Prefer `[Branded]`. | `Annotations/InlineWrapperAttribute.cs` |
| `[JsonFactory]` | On the one static method of a `[Branded]` / `[InlineWrapper]` type that the generated `JsonSerializerContext` descriptor calls instead of the non-validating `create`, so C# validation (`From`) also runs on JSON input; TypeScript-only (→ MS0034). | `Annotations/JsonFactoryAttribute.cs` |
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
| `[EmitPackage]` | Declare npm package identity for emitted output (SSOT for cross-package imports, ADR-0011). Optional `SubpathRoot` strips a namespace prefix from `package.json#exports` keys and consumer imports (spec 008; invalid → `MS0029`, colliding keys → `MS0030`). | `Annotations/EmitPackageAttribute.cs` |
| `[Import]` | Map a C# facade to an external JS/TS module import. | `Annotations/ImportAttribute.cs` |
| `[ImportAlias]` | Pin a deterministic alias when a local declaration shadows an import (silences MS0022). | `Annotations/ImportAliasAttribute.cs` |
| `[ExportFromBcl]` | Expose selected BCL-mapped behavior into emitted output. | `Annotations/ExportFromBclAttribute.cs` |

### Declarative lowering & mapping

| Attribute | Purpose | Code |
| --- | --- | --- |
| `[Emit("$0.foo($1)")]` | Inline a JS/TS template at the call site with arg placeholders (→ MS0023). | `Annotations/EmitAttribute.cs` |
| `[MapMethod]` | Declarative BCL method → JS method/template mapping. | `Annotations/MapMethodAttribute.cs` |
| `[MapProperty]` | Declarative BCL property → JS property/template mapping. | `Annotations/MapPropertyAttribute.cs` |

### Planned attributes (specified, not yet implemented)

Nothing in `Metano.Annotations` defines these yet; they are specified by [spec 011](../../011-runtime-collections-and-types/spec.md).

| Attribute | Target | Purpose | Spec |
| --- | --- | --- | --- |
| `[EmitRuntimeType]` | Type, or `[assembly: EmitRuntimeType]` for every public type | Forces runtime type-descriptor emission, i.e. marks the type as reached for `RuntimeTypeReachability` (FR-027, FR-053); for libraries whose consumers will ask runtime questions (OD-6). | 011 FR-027, FR-053 |
| `[assembly: DisableBoxing]` | Assembly | Disables boxing of enum / `[InlineWrapper]` values at C# boxing conversions (011 FR-050); with it, type questions on such values in `object` report `MS0058`. | 011 FR-050, OD-23 |

### Support enums (not attributes)

`EmitTarget` (`Annotations/EmitTarget.cs`) and `TargetLanguage` (`Annotations/TargetLanguage.cs`).

## System.Text.Json attributes (TypeScript `SerializerContext`)

Not Metano attributes: the TypeScript target reads these `System.Text.Json.Serialization`
attributes on a `JsonSerializerContext` subclass and on the types it reaches, and mirrors
System.Text.Json's behavior in the generated `SerializerContext` (spec 009, #268 / #269 / #270).
They are matched by metadata name (`Json/JsonSerializationAttributes.cs`,
`Json/JsonPolymorphismReader.cs`). Every attribute or option that changes the wire and is not
mirrored is reported with `MS0037`. User guide:
[`docs/serialization.md`](../../../docs/serialization.md#attribute-support). Paths are under
`src/Metano.Compiler.TypeScript/Transformation/`.

| Attribute | Effect in the generated context | Code |
| --- | --- | --- |
| `[JsonSerializable(typeof(T))]` (+ `TypeInfoPropertyName`) | A root: a `TypeSpec<T>` getter for an object type, a `ValueSpec<T>` for a collection, dictionary, set, primitive, enum, branded or nullable type, plus every object type reachable from it; the getter takes System.Text.Json's `JsonTypeInfo` name (→ MS0035, MS0036). | `Json/JsonContractRoots.cs`, `Json/JsonContractClosure.cs`, `Json/JsonTypeInfoNames.cs` |
| `[JsonSourceGenerationOptions]` | `PropertyNamingPolicy`, `JsonSerializerDefaults.Web`, `PropertyNameCaseInsensitive`, `NumberHandling = AllowReadingFromString` and `AllowOutOfOrderMetadataProperties` are mirrored (names at transpile time, the rest as the context's `readOptions`); any other option set to a non-default value → MS0037. | `Json/JsonOptionsReader.cs`, `Json/JsonReadOptionsGetterBuilder.cs`, `Json/JsonUnmirroredContextOptions.cs` |
| `[JsonPolymorphic]` / `[JsonDerivedType]` | On a class, record, interface or `[PlainObject]` base: a data-only `polymorphism` descriptor on the base spec (discriminator, string / int / untagged tags, the three unknown-type modes, ignore-unrecognized), every registered type added to the closure, synthesized `isT` guards for `[PlainObject]` variants (→ MS0032, MS0033). | `Json/JsonPolymorphismReader.cs`, `Json/JsonPolymorphismValidator.cs`, `Json/JsonPolymorphismSpecBuilder.cs`, `Json/JsonStructuralVariants.cs` |
| `[JsonPropertyName("x")]` | The member's wire name; wins over the naming policy. | `Json/JsonPropertyCollector.cs` |
| `[JsonPropertyOrder(n)]` | `PropertySpec.order`; the runtime sorts the whole hierarchy by it, stably. | `Json/JsonPropertyCollector.cs`, `Json/JsonSpecBuilder.cs` |
| `[JsonInclude]` | Serializes a field or an `internal` / `protected internal` property the source generator reaches; on a member it cannot reach, or on a read-only field → MS0037. | `Json/JsonMemberAccess.cs`, `Json/JsonUnmirroredAttributes.cs` |
| `[JsonIgnore]` | `Always` (the default) leaves the member out and, on an override, hides the base declarations (`ignoredProperties`); `Never` keeps it; the conditional values keep it and warn MS0037. | `Json/JsonPropertyCollector.cs`, `Json/JsonSpecBuilder.cs`, `Json/JsonUnmirroredAttributes.cs` |
| `[JsonConstructor]` | Mirrored on the constructor the factory calls; on another constructor → MS0037. | `Json/JsonUnmirroredAttributes.cs` |
| `[JsonRequired]` | Mirrored on a non-nullable member (already required on read); on a nullable member → MS0037. | `Json/JsonUnmirroredAttributes.cs` |
| `[JsonConverter]`, `[JsonExtensionData]`, `[JsonNumberHandling]`, `[JsonObjectCreationHandling(Populate)]`, `[JsonUnmappedMemberHandling(Disallow)]` | Not mirrored → MS0037 (`[JsonConverter]` on a `[Branded]` / `[InlineWrapper]` type is not reported); a `[JsonExtensionData]` member is left out of the spec. | `Json/JsonUnmirroredAttributes.cs` |

The Metano companion attribute is `[JsonFactory]` (above, under "Naming & emission shape").
Tests: `tests/Metano.Tests/JsonSerializerContextTests.cs`, `JsonPolymorphicContextTests.cs`,
`JsonStructuralPolymorphismTests.cs`, `JsonValueRootTests.cs`, `JsonFactoryTests.cs`, and
`tests/Metano.Tests/Json/` (`JsonMemberOptionsTests.cs`, `JsonUnmirroredOptionTests.cs`,
`JsonReadOptionsTests.cs`, `JsonWebNamingTests.cs`, `StjConformanceFixtureTests.cs`).

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
