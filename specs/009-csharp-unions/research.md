# Phase 0 Research: C# 15 union declarations

Every item records a decision, the evidence behind it and the alternatives considered. The tags mean:

- **[V]**: verified by a probe or a re-run on SDK `11.0.100-rc.1.26425.128` / Roslyn 5.9.0 / STJ `11.0.0-rc.1`.
- **[I]**: inferred, not executed.

The probes come from the video-studio Phase 4 investigation. They are kept outside the repo, under the session scratchpad `wf34/{stj-union,roslyn-union-api*,host-net11,pattern-lowering-map,critic}`. Code citations were re-checked against `main` at `be31b84`, and again at `2339fff` for R10.

## R1: What a C# 15 union is

- [V] `public union Clip(ImageClip, SilenceClip);` lowers to a **sealed struct** (base `System.ValueType`) with:
  - `[System.Runtime.CompilerServices.Union]`;
  - an implementation of `IUnion`, whose only member is `object? Value { get; }`;
  - one public ctor per case (parameter name `value`);
  - a parameterless ctor.

  It has no `TryGet` or `Is*` members and no `ToString` override (`Console.WriteLine(clip)` prints `Clip`). `Equals` / `GetHashCode` are `ValueType`-structural over `Value`.
- [V] Generic (`Opt<T>(T, None)`), multi-parameter (`Pair<TA,TB>`) and nullable-case (`NId(int?, string)`) unions all compile. A union may also have a body.
- [V] `union Dup(int,int)` gives CS0111. `union N(string, null)` is a syntax error. `union class` is a syntax error, so a declared union is always a struct. `union S(IShape, Sq)` and `union E(object, string)` compile with no diagnostic. **Decision**: C# accepts overlapping cases silently, so Metano rejects them itself (MS0033).
- [V] A union with a case type to union converts implicitly; `(Clip)x` is the same conversion with `IsImplicit = false`. Boxing a `Clip` to `object` gives a boxed `Clip`: `o is ImageClip` is False.
- [V] A switch with no discard that covers every case compiles with no warning. A missing case gives CS8509. `Clip?` or a nullable case without a `null` arm gives CS8655. `default(Clip)` has `Value == null`, and the warning-free switch throws `SwitchExpressionException` on it.
- [V] Nested union: in `union Outer(Inner, A)`, `o is int` is CS0184 and `o is Inner` is True. STJ writes `5` for `Outer(Inner 5)` but cannot read `5` back. **Decision**: v1 rejects union-typed cases (MS0032) instead of flattening them.

## R2: Reading unions through Roslyn 5.9.0

- [V] Syntax: `UnionDeclarationSyntax`, a subclass of `TypeDeclarationSyntax` (`SyntaxKind.UnionDeclaration`). Its parameter-list entries have a type and **no identifier**, and `GetDeclaredSymbol(param)` returns null.
- [V] Symbol: `TypeKind.Struct`, `IsUnion = true`, `IsSealed = true`, implementing `IUnion` plus any declared interfaces. `UnionAttribute` is **absent** from `GetAttributes()` on a source union; it appears only from metadata.
- [V] Roslyn has no public case-type API (`UnionCaseTypes` is internal). The only public union members are `ITypeSymbol.IsUnion`, `Conversion.IsUnion`, the syntax node and kinds, and the visitors. All carry `[Experimental("RSEXPERIMENTAL006")]`, and using one without suppression is a **build error**.
- [V] **Decision (FR-006)**: read the cases from the public single-parameter instance constructors. In source they are `IsImplicitlyDeclared = true` with no syntax reference. From metadata they are `IsImplicitlyDeclared = false` and carry `[CompilerGenerated]`. Generic unions expose `Result(T)` / `Result(E)`. CS9374 forbids user-declared public one-parameter ctors in a `union` body, so for declared unions the rule is exact. Member order equals declaration order (probe `Z(SilenceClip, ImageClip)`).
- [V] Detection must use `IsUnion`, not the `IUnion` interface: a plain `struct : IUnion` reports `IsUnion = False`. A hand-written `[Union] struct/class : IUnion` reports True, and for the class, `TypeKind = Class`. **Decision (FR-005)**: detection runs before the `TypeKind` switch (`TypeTransformer.GetOrExtractIr`, `src/Metano.Compiler.TypeScript/Transformation/TypeTransformer.cs:1797-1803`; Dart `DartTransformer`).
- [V] Patterns over a union use the union as `InputType` and the case as `NarrowedType`. The IOperation tree has **no implicit `.Value` node**, so no rewrite is needed for patterns. `c is null` and `null =>` are constant patterns that test `Value`.
- [V] `ISwitchExpressionOperation.IsExhaustive` is **False** for a complete union switch (it becomes True only with `null` / `var`). **Decision**: never derive the `never` tail from `IsExhaustive`. Use FR-020's structural rule.
- [V] `SymbolHelper.GetPositionalPrimaryConstructor` (`src/Metano.Compiler/SymbolHelper.cs`) matches `TypeDeclarationSyntax { ParameterList: not null }`. It would also match a union, but returns null because the union ctors have no syntax. Union handling must not reach that path.
- [V] A metadata consumer needs `LangVersion preview`. With `Latest`, every union site reports CS8652.

## R3: Host framework vs parser (D6)

- [V] The released 2.6.1 tool (net10, Roslyn 5.3) already **loads** a net11.0 project. The tool itself runs on runtime 10. `MSBuildWorkspace` evaluates the project in its BuildHost child (`dotnet --roll-forward LatestMajor`), which runs on runtime 11 and loads SDK 11's MSBuild. Parsing `union` needs the Roslyn bundled with Metano, not a net11 host.
- [V] On a machine with **only** .NET 11, 2.6.1 fails to start ("You must install or update .NET … 10.0.0"). With `DOTNET_ROLL_FORWARD=Major` it runs and transpiles.
- [V] Multi-targeting `net10.0;net11.0` works, but breaks the in-repo build: `Metano.Build.targets` runs `dotnet run --project …` and gets 12× MSB3073 "Your project targets multiple frameworks".
- **Decision (FR-002)**: keep `net10.0` and add `<RollForward>Major</RollForward>`. **Alternative rejected**: multi-targeting. It costs the `--framework` fix, a bigger package and SDK 11 in release, and it gains only the .NET 11-only machine case, which RollForward already covers.
- [I] The csproj `<RollForward>Major</RollForward>` route was not built; only the environment variable was verified. The toolchain stage must check this with a local-feed install on a .NET 11-only `DOTNET_ROOT` (see quickstart).

## R4: Roslyn 5.3.0 → 5.9.0 (D8)

- [V] With the three `Microsoft.CodeAnalysis.*` packages at 5.9.0, a copy of the repo builds `Metano.slnx` with 0 warnings and 0 errors under `TreatWarningsAsErrors`. No API Metano uses is removed or obsoleted.
- [V] The test count at that time was 1198 total / 1191 passed / 7 skipped (`ExtensionIndexerTests`). The suite has grown since, and the toolchain PR must re-measure it. 5.9.0 parses extension indexers under Preview, but un-skipping those 7 tests still fails with no output. That is out of scope here and stays skipped.
- [V] Under SDK 11 the full solution builds, and the regenerated `targets/` output is byte-identical to the committed files. The only warning is the pre-existing MS0005 in SampleIssueTracker.

## R5: STJ 11 union wire format (D5)

- [V] STJ recognizes unions automatically, with no attribute, both by reflection and in source-gen (`JsonTypeInfoKind.Union`). Serialization writes the **bare payload**:
  - `{"Path":"a.png","Seconds":1.5}`, `{"Seconds":2}`, `5`, `"x"`;
  - `null` for `default`;
  - `{"Clip":{"Seconds":3},"Id":"abc"}` for a holder record;
  - `[{"Path":"p","Seconds":1},{"Seconds":2}]` for a list;
  - `{}` for `Opt<int>.None`.
- [V] Deserialization classifies by **token kind only**:
  - `Id(int,string)` reads `5` → Int32 and `"x"` → String;
  - `null` reads as Value null;
  - `5.5` gives `The JSON value could not be converted to System.Int32.`;
  - `true` gives `JSON token type 'True' is not supported for union type 'Id'.`;
  - `{}` gives the same message with `'StartObject'`.
- [V] Any object for `Clip` gives: `JSON value type 'Object' is ambiguous for union type 'Clip' because multiple case types can use this value type. Specify a custom type classifier to support deserialization.` The same error appears for `(int,double)`, `(int,long)`, `(DateTime,string)`, `(int[],List<string>)` and same-shape records.
- [V] The source generator warns at build time with `SYSLIB1227: Union type 'Clip': case types 'ImageClip', 'SilenceClip' all serialize as JSON value type 'Object'. …`. **Decision (FR-026)**: MS0036 is a warning, as SYSLIB1227 is.
- [V] `JsonUnionTypeStructuralClassifier` rejects `Clip` at metadata time, which also breaks serialization. Custom `JsonTypeClassifierFactory` instances work but are C# code over `Utf8JsonReader`. **Decision**: classifiers are untranspilable, so MS0032 is raised when the union is JSON-reachable.
- [V] A `[JsonPolymorphic]` abstract base used as a case round-trips through `$type`. **Decision**: this depends on #268 (OD-7).
- [V] STJ handles unions symmetrically as `Dictionary<string,U>`, `U[]` and `U?` elements, and as a default element (`[null]`): `{"Map":{"k":5,"s":"x"},"Arr":[1,"a"],"Opt":null}` round-trips. **Decision (FR-023)**: the union descriptor composes with the container descriptors.
- [V] Doubles are written in shortest form (`2`, `1.5`), which matches JS `Number#toString` for these values. [I] Exponent forms, `-0` and `NaN` are untested and excluded from fixtures.
- [V] Derived-first property order: STJ writes the most-derived type's properties **before** the inherited ones (stj-hierarchy). The TS runtime walks base-first. **Decision**: union fixtures use non-inheritance case records until that ordering is fixed (a #268 prerequisite).

## R6: Existing pattern lowering on `main`

- [V] Since #238, a bare type name in an arm (`ImageClip =>`) becomes `IrTypePattern` (`IrExpressionExtractor.cs:752`), and enums test with `typeof` (`IrToTsExpressionBridge.Patterns.cs:174-188`). The remaining fallback is `/* TODO: non-named type pattern */`.
- [V] PR #298 (#279, open) adds `IrRuntimeTypeTest.Of(IrTypeRef)` (`Instanceof` / `Typeof` / `Guard` / `None`), makes the TODO fallback unreachable, and reports MS0001 for `None`. **Decision (FR-019, NFR-004)**: unions reuse it and add no table of their own.
- [V] The switch-expression IIFE (`IrToTsExpressionBridge.SwitchExpression.cs:110-175`):
  - takes an **untyped** `$s`, so TS infers its type from the argument;
  - binds designators;
  - ends with `throw new Error("Non-exhaustive switch expression")` when there is no catch-all.

  Because the argument's static type is the union alias, `$s satisfies never` after the case tests type-checks without a parameter annotation. [V] tsgo strict accepts it and reports TS1360 for a missing case. Record-shaped classes with `equals`/`hashCode`/`with` do not collapse the narrowing.
- [V] `Clip | null` without a null arm reports TS1360 on `null`, so the tail is gated (FR-020, US2-4).
- [V] Switch statements: `IrSwitchCase(Labels, Body)` holds expressions only, and `ExtractSwitchSection` (`IrStatementExtractor.cs:367-384`) handles only `CaseSwitchLabelSyntax` / `DefaultSwitchLabelSyntax`. As a result, `case T x:` is silently dropped and becomes a `default:`. PR #298 lists this as untouched. **Decision (FR-008, FR-021)**: add pattern + `when` to the IR case and fix the lowering here.
- [V] The scanner (`IrRuntimeRequirementScanner.cs:169-197`) collects `is*` helpers only for dispatcher parameters. `ImportCollector.cs:315` skips `is*` names. **Decision (FR-011)**: the scanner walks patterns.
- [V] The runtime has `equals(a, b)` (`metano-runtime/src/system/equality.ts:33`) and the primitive checks `isInt32` / `isString` / `isFloat64` (`type-checking/primitive-type-checks.ts`). **Decision (FR-010)**: union equality lowers to `equals`.

## R7: Toolchain, CI and samples (D7, FR-003/FR-004/FR-029)

- [V] `ci.yml:20-21` and `release.yml:30-31` install only `10.0.x` (quality preview). `global.json` pins `10.0.0` / `latestMinor` with no prerelease. `Metano.Tests` targets `net10.0`.
- [V] The CI bun job lists the samples explicitly (`ci.yml:67-81`). A new `sample-unions` step must be added, or its parity tests never run.
- [I] CSharpier 1.2.6 (`dotnet-tools.json`, `rollForward: false`) bundles its own Roslyn, which may not parse `union`. If `dotnet csharpier check .` fails on union sources, `samples/SampleUnions/**/*.cs` and the union test fixtures go into `.csharpierignore` until CSharpier supports the syntax. The toolchain stage verifies this first (risk K3 in plan.md).
- [I] Inline TUnit compilations reference the running runtime's assemblies, and against net10 references a union gives CS0518 / CS0656. The polyfill (`IUnion` + `UnionAttribute` declared in the test source) was verified to bind on net10. The maintainer chose real net11 reference assemblies instead (D7). See OD-1 for how the test project gets them.

## R8: Batch-3 triage items that interact with this feature

- **json-collection-root**: `[JsonSerializable]` roots that are not objects (collections, primitives, nullable) go through the object-spec path and produce code that does not compile. A union root is the same kind of non-object root. **Decision (OD-8)**: defer union roots until the `ValueSpec` / `JsonTypeInfo<T>` root work lands. Its proposed "dispatch on `kind != Ref`" rule then covers unions for free.
- **branded-validating-factory**: a `[Branded]` case serializes as its underlying primitive token kind. `union U(Title, string)` with `Title` branded over `string` is therefore both MS0033 (same runtime test) and MS0036 (same token). The triage note proposed `MS0029 InvalidJsonFactory`, but that code is now taken by spec 008, so the JsonFactory FR must allocate a code after `MS0037` (from `MS0038`).

## R9: Diagnostic allocation (D9)

- [V] `MS0029` / `MS0030` belong to spec 008 and `MS0031` (`ExplicitImplementationConflict`) to #278, which merged into `main` while this spec was in review (`src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs:293-316`). The first draft of this spec reserved `MS0031`–`MS0036` and was renumbered by one on rebase. No open PR or remote branch uses `MS0032`+ (`git grep` over every remote branch, 2026-10-31).
- **Decision**: reserve **MS0032–MS0037** in the diagnostic catalog in this spec PR. A concurrent spec for #268 (polymorphic JSON) or JsonFactory must start at MS0038.

## R10: Review probes (2026-10-31)

Added while closing the spec-review gaps. One console probe (`net11.0`, `LangVersion preview`, SDK `11.0.100-rc.1.26425.128`, reflection-based STJ), kept in the session scratchpad `wf3/spec009/probe`. Every bullet is [V] unless tagged.

- **Relational patterns** (FR-033). With `union Id(int, string)`, `id switch { > 5 => "gt5", string s => "s:" + s, int => "int" }` returns `gt5` for `10`, `s:10` for `"10"`, and `int` for `3`. C# applies the relational pattern to `Value` with an implicit `int` test. [I] That Roslyn reports `int` as the relational pattern's `NarrowedType` is not yet probed at the API level.
- **Equality** (FR-016). `Id five = 5;` `five.Equals(5)` is `False`; `five.Equals((Id)5)` is `True`.
- **`IUnion` access** (FR-034). `((IUnion)five).Value` is `5`, a generic `T Read<T>(T t) where T : IUnion` reading `t.Value` returns `5`, and `object o = five; o is IUnion` is `True`.
- **`object` holding a case** (FR-016). `object o = 5; o is Id` is `False`.
- **STJ Web defaults** (FR-037). With `new JsonSerializerOptions(JsonSerializerDefaults.Web)`, `Deserialize<Id>("\"5\"")` throws `JSON value type 'String' is ambiguous for union type 'Id' because multiple case types can use this value type. …`. `AllowReadingFromString` makes the `int` case a `String` candidate. `Deserialize<Id>("5")` still yields `Int32 5`.
- **`byte[]` case** (FR-037). `union B(byte[], int)`: `Serialize<B>(new byte[] {1, 2})` writes `"AQI="`, and reading `"AQI="` yields `Byte[]`. The token is `String`.
- **Enum case** (FR-037). `union E(Color, string)` with a plain enum: `Serialize<E>(Color.Green)` writes `1`, and reading `1` yields `Color.Green`. The token is `Number`, so a string-enum case needs `JsonStringEnumConverter` to be a `String` token.
- **Union dictionary key** (FR-012, edge cases). `Serialize(new Dictionary<Id, int> { [5] = 1 })` and the matching `Deserialize` both throw `NotSupportedException: The type 'Id' is not a supported dictionary key using converter of type '…JsonUnionConverter`1[Id]'. …`.
- **Closed generic union** (FR-030). `union GOpt<T>(T, Img)`: `Serialize<GOpt<string>>("x")` writes `"x"`.
- **Union through a type argument** (FR-031). `GOpt<Id>` holding `(Id)5` compiles, a switch `{ Id i => …, Img => … }` picks `Id`, `Serialize` writes `5`, and `Deserialize<GOpt<Id>>("5")` throws `JSON token type 'Number' is not supported for union type 'GOpt`1[Id]'.`. This also fixes the STJ display name of a constructed generic union in messages: ``GOpt`1[Id]``. [I] The format for several or nested type arguments is not probed.
- **Duplicate cases after substitution** (FR-031). `union G<T>(T, string)`: the type reference `G<string>` compiles, but `G<string> g = "x";` is `CS0457: Ambiguous user defined conversions 'G<string>.G(string)' and 'G<string>.G(string)'`. A switch over `default(G<string>)` throws `SwitchExpressionException`.
- **Hand-written union** (FR-005, FR-012). `[Union] public class HU : IUnion { public HU(int v) { Value = v * 2; } public HU(string v) { Value = v.ToUpperInvariant(); } public object? Value { get; } }` is accepted by the compiler. `HU h = 3;` switches to `int 6`, `HU h = "ab"` to `str AB`, and STJ writes `6`. The user constructors run, so the identity rewrite would drop their logic.
- **Default sources** (edge cases). For `var arr = new Id[2];`, `arr[0].Value is null` is `True`. `Id? nd = default(Id); nd.HasValue` is `True`.
- **`DeserializeAsync`** (FR-037). `DeserializeAsync<Id>` over a stream holding `5` yields `5`, identical to the synchronous path.
- **net11 reference pack** (FR-036). `sdk/11.0.100-rc.1.26425.128/Microsoft.NETCoreSdk.BundledVersions.props` names `BundledNETCoreAppPackageVersion` `11.0.0-rc.1.26425.128`, the pack lives at `packs/Microsoft.NETCore.App.Ref/11.0.0-rc.1.26425.128/ref/net11.0/` (176 assemblies), and `IUnion` is defined in that pack's `System.Runtime.dll`.
- **PR #298 head** (FR-013, FR-019). `IrRuntimeTypeTest` is a closed record hierarchy: `Instanceof`, `Typeof(IrPrimitive)`, `Guard`, `None`. `int` and `double` classify as different `Typeof` values, collections and delegates as `None`, and temporal types as `Instanceof`. The PR is still open, so these names may change; the spec depends only on the capability.
- **Existing JSON classification** (FR-030, FR-037). On `main`, `JsonSerializerContextTransformer.ClassifyPropertyType` returns `Array` for `byte[]` (an `IArrayTypeSymbol`), `Enum` (string) for `[StringEnum]`, and `Ref` for `char` (it is not in the primitive list) and for a union struct. `IsDiscoverableObjectType` requires `IsGenericType: false` and `IsInCurrentAssembly`, and `BuildRefDescriptor` always points at `this.<getter>`.
