# Contract: Union diagnostics (`MS0038`–`MS0043`)

**Normative for**: FR-012–FR-016, FR-026, FR-031, FR-034, FR-037–FR-042; D9–D13. This spec PR reserves the codes in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md). `MS0032`–`MS0037` belong to [009-polymorphic-json](../../009-polymorphic-json/contracts/diagnostics.md); the next free code after this block is `MS0044`. Symbolic names become constants in `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` when they are implemented. Each reason below needs one test that asserts the code, the severity and the location; a warning test also asserts that the output is emitted.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0038` | `UnsupportedUnionShape` | Error | union declaration (or the attribute, or the offending member); the first reference site for a constructed instance (FR-031); the conversion or type argument for `IUnion` and extra-interface access; the property or element for a JSON reason |
| `MS0039` | `IndistinguishableUnionCases` | **Warning** (D11) | union declaration, or the first reference site of a constructed generic instance (FR-031) |
| `MS0040` | `UntestableUnionCase` | Error | union declaration, or the pattern / JSON use site for a type-parameter case |
| `MS0041` | `UnionDefaultValue` | **Warning** (D10) | the `default` expression |
| `MS0042` | `AmbiguousUnionJsonCases` | Warning | the `JsonSerializerContext` declaration that reaches the union |
| `MS0043` | `UnionErasureDivergence` | Warning | the expression |

## Reasons

**MS0038 — UnsupportedUnionShape** (Error)

1. The union body declares a member kind that the companion namespace does not lower (FR-042): an indexer, an event, an operator, a user-defined conversion, a static constructor, an instance property with a setter or `init` accessor, or an override of `Equals` / `GetHashCode`. Methods, get-only properties, static fields, nested types, a `ToString()` override and extra-interface implementations are lowered, never reported.
2. The union is converted to one of its extra interfaces (assignment, argument, cast, `is`), or used as a type argument for a type parameter constrained to one (OQ-1).
3. A case type is itself a union (nested unions; research R1), including a union that becomes a case through a type argument (`Opt<Clip>`, checked once per constructed instance; FR-031).
4. The union carries `[PlainObject]`, `[Branded]`, `[InlineWrapper]`, `[StringEnum]`, `[ExportedAsModule]` or `[JsTuple]`.
5. A JSON-reachable union uses `[JsonUnion(TypeClassifier = …)]`.
6. A JSON-reachable union has a `[JsonPolymorphic]` case (until #268 lands through spec 009, OD-7).
7. The union is hand-written: `[Union]` + `IUnion` declared without `union` syntax in source, or a metadata union whose case constructors do not carry `[CompilerGenerated]` (FR-005). Its constructors and `Value` are user code (research R10).
8. A union is listed as a `[JsonSerializable]` root (until the #270 `ValueSpec` roots of spec 009 land, OD-8).
9. The union is converted to `IUnion` (other than the `((IUnion)u).Value` read that FR-034 rewrites), tested with `is IUnion`, or used as a type argument for a type parameter constrained to `IUnion`.
10. A JSON-reachable union is a dictionary key (STJ throws `NotSupportedException`; research R10).
11. A JSON-reachable union has a case whose STJ token Metano does not reproduce: `byte[]`, `char`, or a `[StringEnum]` enum without `JsonStringEnumConverter` (FR-037, OD-16).
12. A JSON-reachable union has a number case under `NumberHandling.WriteAsString` or `AllowNamedFloatingPointLiterals` (FR-037).
13. A JSON-reachable union has an object-kind case the context cannot describe: a closed generic object type, or an object type from another assembly (FR-030, FR-038, OD-13).

Example message: `Union 'Clip' declares indexer 'this[int]'; the companion namespace cannot lower indexers (spec 010 FR-042).`

**MS0039 — IndistinguishableUnionCases** (Warning)

The rule is over **erased JS value domains**, not test identity (FR-013): two different test expressions can still accept the same value. The output is emitted, and the first matching arm or case wins (FR-040). This **diverges from C#**, where a union case is nominal: `union N(int, double)` holding `2.5` matches `double d =>` in C# but the first `number` arm in TS.

1. Case A converts implicitly to case B (`union S(IShape, Square)`, `union E(object, string)`).
2. Two cases share the `number` domain: `int` / `double`, `int` / `long`, a numeric enum / `int`, a `[Branded]` number / `double`.
3. Two cases share the `string` domain: `char` / `string`, `Guid` / `string`, a `[Branded]` string / `string`, a `[StringEnum]` / `string`, or two `[StringEnum]` enums.
4. One case is a class and the other a subclass of it.
5. Two `structural` cases (interfaces or `[PlainObject]` types, with declared or synthesized guards), whatever their shapes, or a `structural` case next to a class or record case (OD-15).
6. Two `array` cases.
7. Any of the above after type-argument substitution of a constructed generic union (`G<string>` for `union G<T>(T, string)`; FR-031).

Example message: `Union 'N': cases 'int' and 'double' both erase to JS 'number'; the first matching arm or case ('int' in declaration order) wins, which differs from C#'s nominal case dispatch.`

**MS0040 — UntestableUnionCase** (Error)

An interface or `[PlainObject]` case without `[GenerateGuard]` is **not** a reason: its guard is synthesized (FR-041).

1. A pattern test or JSON use against a type-parameter case that is still open at that site (`Opt<T>` with `T v =>`). A closed instance (`Opt<int>` with `int n =>`) is not reported.
2. A delegate case, a case typed as TS `Iterable<T>` (`IEnumerable<T>`, `IReadOnlyCollection<T>`), or a non-transpilable class without `[Import]` / `[External]`. A single array-shaped case is testable (`Array.isArray`); two are the MS0039 warning.
3. A structural case for which `TypeGuardBuilder` produces no guard, so none can be synthesized.
4. A structural case from another assembly whose package exports no guard (OQ-2). The message suggests `[GenerateGuard]` on the library type.

**MS0041 — UnionDefaultValue** (Warning)

`default(U)` is valid C# and is emitted as the erased `null` (D10). A slot that holds it by its own declaration is widened to `U | null` with no diagnostic (FR-039). MS0041 is reported only where a default union value (`default(U)`, or a target-typed `default` converted to `U`) lands in a position whose TS type is the non-nullable `U`; the value is then emitted as `null as unknown as U`:

1. An argument for a `U` parameter: a method, a constructor, a positional record or an indexer.
2. A return value of a member, local function or lambda that returns `U`.
3. An element of an array, a collection initializer or a collection expression of `U`.
4. An assignment to, or an object-initializer value for, a `U` member that FR-039 does not widen (a member of another type, or of a type from another assembly).

Array elements created by `new U[n]`, `FirstOrDefault`-style APIs, `TryGetValue(out U)` and generic `default(T)` also produce `default(U)` and are documented as known divergences in the spec's Edge Cases; they are neither widened nor reported.

Example message: `'default(Clip)' is emitted as null where TypeScript expects 'Clip', which cannot hold null; it is asserted with 'null as unknown as Clip'. A switch over it throws, as in C#.`

**MS0042 — AmbiguousUnionJsonCases** (Warning)

1. Two or more object-kind cases (records, classes, `[PlainObject]`, maps).
2. Any other shared token kind: two number cases, two string cases, or two array cases. MS0039 usually reports these too; MS0042 is still reported, so the JSON consequence is explicit.
3. A number case and a string case under `AllowReadingFromString` (including `JsonSerializerDefaults.Web`): both read the `String` token (research R10).

**MS0043 — UnionErasureDivergence** (Warning)

Reasons 1 and 2 do not apply to a union whose body overrides `ToString()`: the call is rewritten to the companion function (FR-042). The boxing conversion of a union to `object` / `ValueType` / an interface is not detected in v1 (OD-6).

1. `u.ToString()`, or a union value interpolated into a string or concatenated with one. C# prints the union name (`Clip`); TS prints the case value.
2. `u.GetType()`. C# returns the union type; TS sees the case.
3. `o is U`, `o as U`, or a `U` type pattern where `o`'s static type is not `U` / `U?`. A bare case boxed as `object` is not a `U` in C#.
4. `u.Equals(x)` where `x` is not statically `U` / `U?`. C# returns false when the boxed argument is not a `U` (`id.Equals(5)` is false; research R10); the erased `equals(id, 5)` returns true.
5. `HasValue` on a `Nullable<U>`. A `U?` holding `default(U)` has `HasValue == true` in C# but erases to `null`.
