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

## Stable codes (`MS0001`–`MS0031`)

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
| `MS0013` | `IgnoreReferencedByTranspiledCode` | Transpilable code references a type marked `[Ignore]` for the active target. Not raised for: (a) source-generator output (e.g. System.Text.Json `*.g.cs`, as reported by the build workspace) — only user-authored code is scanned; this does not filter emission, so generator-added members of a transpilable partial type may still be emitted; (b) an interface ignored for the active target listed in a transpilable type's base list — it is erased from `implements`/`extends` in every target it is ignored for and replaced by its own non-ignored base interfaces; (c) references inside an attribute application, e.g. `[JsonConverter(typeof(MyConverter<T>))]` or an `[Ignore]` attribute class — attributes are never emitted — except attributes the compiler reads for emission (`[JsonSerializable(typeof(T))]`), which still raise; (d) `nameof` arguments, which fold to a string constant. Every other position still raises (#277): parameters, return types, fields, properties (once per property, not again per accessor), events, generic constraints, type arguments — including an `[Ignore]` type argument of a base interface — delegate signatures, and every type name inside the body of an emitted member, a field/property/event initializer, parameter default, or constructor-initializer argument: locals, `new`, casts, `is`/`as`, all pattern kinds (declaration, type, recursive, property subpatterns, switch arms, `case` labels), `catch` declarations, local functions, explicitly typed lambda parameters, `foreach` variables, `default(T)`, `typeof`. Reading or calling an `[Ignore]` member raises as well. Bodies that are not emitted (static constructors, explicit interface implementations, custom event accessors) are not scanned. |
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
| `MS0031` | `ExplicitImplementationConflict` | An explicit interface implementation (`string IPlain.Name`) cannot be declared under its interface member's name because another emitted member of the type or a base class (an implicit `Name`, or `IOther.Name`) with the same static-ness already claims it (case-insensitive, `[Name]`-aware). Only for interfaces of the transpiled contract (source, `[Transpile]`, or a `[TranspileAssembly]` package); a BCL bridge such as `IEnumerable.GetEnumerator` is dropped silently. The explicit member is dropped; rename one, implement implicitly, or mark the explicit member `[Ignore]` (#278). |

## Reserved codes (specified, not yet implemented)

Codes reserved in a spec PR before any implementation. Nothing raises them yet. A code moves to the table above when its implementation lands. New specs allocate from the next code after the last reserved one (currently `MS0057`: `MS0038`–`MS0043` are reserved by the C# unions specification, PR #333, `MS0044` is taken by the package-root fix #334 / PR #355, and `MS0045`–`MS0056` by the runtime collections and type descriptors specification, issue #335).

| Code | Symbolic name | Severity | Meaning | Spec |
| --- | --- | --- | --- | --- |
| `MS0032` | `InvalidJsonPolymorphism` | Error | A `[JsonDerivedType]` registration the generated TS context cannot honor: not assignable to the base, not transpilable, duplicate type or tag, abstract without `FallBackToNearestAncestor`, interface-typed entry, generic derived type, a property named like the discriminator, an integer-like JSON name, or two `[PlainObject]` variants whose shapes cannot be told apart on write. | [009](../../009-polymorphic-json/contracts/diagnostics.md) |
| `MS0033` | `LossyJsonPolymorphism` | Warning | An untagged registration (reads back as the base, as in STJ) or a derived type that is itself polymorphic (not re-dispatched, as in STJ). | [009](../../009-polymorphic-json/contracts/diagnostics.md) |
| `MS0034` | `InvalidJsonFactory` | Error | An invalid `[JsonFactory]` method on a `[Branded]` type (not a single-parameter named static returning the type, overloaded, not emitted, or named like a companion member), or `[JsonFactory]` on a non-branded type. | [009](../../009-polymorphic-json/contracts/diagnostics.md) |
| `MS0035` | `InvalidJsonSerializableRoot` | Error | A `[JsonSerializable]` root the context cannot describe: open generic, error type, `object`, non-collection interface, non-transpilable object kind, or an array of rank > 1. | [009](../../009-polymorphic-json/contracts/diagnostics.md) |
| `MS0036` | `JsonTypeInfoNameCollision` | Error / Warning | Two types map to one context member name (Error when the loser is referenced, Warning for an unreferenced root), or a name equals a `SerializerContext` member. | [009](../../009-polymorphic-json/contracts/diagnostics.md) |
| `MS0037` | `UnmirroredJsonOption` | Warning | A wire-affecting `[JsonSourceGenerationOptions]` option the generated context does not mirror, a `[JsonInclude]` member, a `byte[]` member, or `[JsonPropertyOrder]`. | [009](../../009-polymorphic-json/contracts/diagnostics.md) |
| `MS0038` | `UnsupportedUnionShape` | Error | A C# 15 `union` uses a shape the erased TS union cannot represent: a body member kind the companion namespace does not lower (indexer, event, operator, conversion, static constructor, setter, `Equals`/`GetHashCode` override), a conversion to an extra interface, a union-typed case (also through a type argument), an unsupported Metano attribute, a hand-written `[Union]` type, `IUnion` access, or a JSON-reachable custom type classifier / `[JsonPolymorphic]` case / dictionary key / case whose STJ wire form Metano does not reproduce / union root. | [010](../../010-csharp-unions/contracts/diagnostics.md) |
| `MS0039` | `IndistinguishableUnionCases` | Warning | Two union cases have overlapping erased JS value domains: assignable to each other, the same primitive domain (`int`/`double`, `Guid`/`string`, …), a class and its subclass, two structural cases or a structural case next to a class, or two array cases. The output is emitted and the first matching arm or case wins, which differs from C#. | [010](../../010-csharp-unions/contracts/diagnostics.md) |
| `MS0040` | `UntestableUnionCase` | Error | A union case has no runtime test (a delegate, an `Iterable<T>` sequence, a non-transpilable class, a structural case with no possible guard, an open type parameter at a test site). Structural cases without `[GenerateGuard]` get a synthesized guard instead. | [010](../../010-csharp-unions/contracts/diagnostics.md) |
| `MS0041` | `UnionDefaultValue` | Warning | `default(U)` or a target-typed `default` lands in a TS position typed `U` that cannot hold `null` (argument, return, element, non-widened member); emitted as `null as unknown as U`. Slots that hold `default(U)` by declaration are widened to `U \| null` without a diagnostic. | [010](../../010-csharp-unions/contracts/diagnostics.md) |
| `MS0042` | `AmbiguousUnionJsonCases` | Warning | A JSON-reachable union has cases with the same JSON token kind (the analogue of STJ SYSLIB1227); deserialization throws, as STJ does. | [010](../../010-csharp-unions/contracts/diagnostics.md) |
| `MS0043` | `UnionErasureDivergence` | Warning | `ToString`/interpolation (without a `ToString` override in the union body), `GetType`, `is U` over a non-union input, `Equals` with a non-union argument, or `HasValue` on `U?` behaves differently after erasure. | [010](../../010-csharp-unions/contracts/diagnostics.md) |
| `MS0045` | `UnanswerableRuntimeTypeTest` | Error | An `is` / `as` / pattern / dispatch test against a type with no runtime test after 3.0.0: a delegate, a pointer, `void`, a non-transpilable class with no runtime class, a `[PlainObject]` with no possible guard, or a referenced-package interface with no emitted descriptor. Replaces the MS0001 warning + placeholder for runtime tests; includes a BCL exception outside the runtime set at a `catch` / `is` site. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0046` | `ErasedTypeArgumentTest` | Warning | A test or cast against `T[]` / `JsArray<T>` (non-`object` element; answered by `asArray`) or against a runtime collection whose element descriptor may be `Unknown` (treated as a wildcard). | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0047` | `PlainObjectShapeOverlap` | Warning | `is` / `as` / pattern against a `[PlainObject]` while another reachable `[PlainObject]` covers its shape; the structural guard answers for both. Mirrors MS0039 for unions. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0048` | `RuntimeTypeOfPlainObject` | Warning | `GetType()` on an `object` / `dynamic` / interface / unconstrained-`T` receiver that may hold a `[PlainObject]`; the runtime answer for a plain object is `Object`. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0049` | `UnsupportedReflection` | Error | A `System.Type` member outside the supported identity surface, `Activator.CreateInstance`, `Type.GetType(string)`, `typeof(void)` / an open generic definition / a pointer, or `GetType()` on `null`. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0050` | `ReifiedParameterOnInteropSurface` | Warning | A hidden type-descriptor parameter was added to a member JS callers can reach (public in an `[EmitPackage]` library, `[ExportedAsModule]`, a `[JsxComponentBuilder]` member, a `[JsCallable]` conversion). `UnknownType` is the wildcard argument. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0051` | `TargetSpecificInterop` | Error | `Js.Code` / `Metano.Interop.Js` / `JsArray<T>` reached by a non-TypeScript emission (or a `Metano.Interop.Dart` member by a non-Dart one) without `[Ignore(<target>)]` on the containing member or type. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0052` | `InvalidInlineCode` | Error | The `Js.Code` argument is not a constant string or an interpolated string with constant literal parts, or a hole uses `ref` / `out`, an unsupported dynamic operation or `await`. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0053` | `UnsupportedDynamicOperation` | Error | A `dynamic` construct that cannot pass through: a collection / object initializer on a `dynamic` receiver, `ref` / `out` / named arguments on a dynamic call, dynamic `await` / `foreach` / `using` / events, a user-defined operator with a `dynamic` operand or an explicit cast to a type with user-defined conversions. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0054` | `UnreifiableTypeParameter` | Error | A runtime question about an open `T` (`is T`, `as T`, `typeof(T)`, `new T()`, `default(T)`) in a body that cannot carry a hidden descriptor parameter (an `[Emit]` / `[Import]` extern, an `[External]` interface, a `[JsCallable]` body, a `[ModuleEntryPoint]`, an operator, a `[Queryable]` lambda), in an override group that contains one, or in a group whose member comes from a referenced package without the parameter. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0055` | `UncheckedCast` | Warning | An explicit cast `(T)x` whose target the descriptors cannot verify (an erased array element, an `UnknownType` collection argument, a delegate, a non-reified `T`); emitted as `x as unknown as T`. `x.UnsafeCast<T>()` is the warning-free spelling. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |
| `MS0056` | `InlineCodeHoleInStringLiteral` | Warning | A `Js.Code` interpolation hole falls inside a JS string literal of the inline text, so the value is spliced as code rather than quoted; the output is emitted. | [011](../../011-runtime-collections-and-types/contracts/diagnostics.md) |

## Build integration (`Metano.Build`)

Raised by the MSBuild target, not by the compiler, so it lives outside the `MS` code space.

| Code | Raised by | Meaning |
| --- | --- | --- |
| `METANOBUILD001` | `Metano.Build.targets` | The `metano-typescript` .NET tool is not available; the message lists the local-manifest / global install commands and `dotnet tool restore`. Opt out with `MetanoSkipToolCheck=true` (FR-035). |

## Related

- `docs/adr/0010-metano-diagnostics.md`
- [feature-support-matrix.md](./feature-support-matrix.md) · [attribute-catalog.md](./attribute-catalog.md)
