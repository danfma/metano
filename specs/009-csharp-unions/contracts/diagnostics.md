# Contract: Union diagnostics (`MS0032`–`MS0037`)

**Normative for**: FR-012–FR-016, FR-026, FR-031, FR-034, FR-037, FR-038; D9. This spec PR reserves the codes in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md). Symbolic names become constants in `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` when they are implemented. Each reason below needs one test that asserts the code, the severity and the location.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0032` | `UnsupportedUnionShape` | Error | union declaration (or the attribute); the first reference site for a constructed instance (FR-031); the conversion or type argument for `IUnion` access; the property or element for a JSON reason |
| `MS0033` | `IndistinguishableUnionCases` | Error | union declaration, or the first reference site of a constructed generic instance (FR-031) |
| `MS0034` | `UntestableUnionCase` | Error | union declaration, or the pattern / JSON use site for a type-parameter case |
| `MS0035` | `UnionDefaultValue` | Error (OD-3) | the `default` expression, or the uninitialized member's declaration |
| `MS0036` | `AmbiguousUnionJsonCases` | Warning | the `JsonSerializerContext` declaration that reaches the union |
| `MS0037` | `UnionErasureDivergence` | Warning | the expression |

## Reasons

**MS0032 — UnsupportedUnionShape**
1. The union declares a body member (method, property, field, event, operator, or a non-case constructor).
2. The union implements an interface other than `IUnion`.
3. A case type is itself a union (nested unions; research R1), including a union that becomes a case through a type argument (`Opt<Clip>`, checked once per constructed instance; FR-031).
4. The union carries `[PlainObject]`, `[Branded]`, `[InlineWrapper]`, `[StringEnum]`, `[ExportedAsModule]` or `[JsTuple]`.
5. A JSON-reachable union uses `[JsonUnion(TypeClassifier = …)]`.
6. A JSON-reachable union has a `[JsonPolymorphic]` case (until #268, OD-7).
7. The union is hand-written: `[Union]` + `IUnion` declared without `union` syntax in source, or a metadata union whose case constructors do not carry `[CompilerGenerated]` (FR-005). Its constructors and `Value` are user code (research R10).
8. A JSON-reachable union is listed as a `[JsonSerializable]` root (until OD-8 is resolved).
9. The union is converted to `IUnion` (other than the `((IUnion)u).Value` read that FR-034 rewrites), tested with `is IUnion`, or used as a type argument for a type parameter constrained to `IUnion`.
10. A JSON-reachable union is a dictionary key (STJ throws `NotSupportedException`; research R10).
11. A JSON-reachable union has a case whose STJ token Metano does not reproduce: `byte[]`, `char`, or a `[StringEnum]` enum without `JsonStringEnumConverter` (FR-037, OD-16).
12. A JSON-reachable union has a number case under `NumberHandling.WriteAsString` or `AllowNamedFloatingPointLiterals` (FR-037).
13. A JSON-reachable union has an object-kind case the context cannot describe: a closed generic object type, or an object type from another assembly (FR-030, FR-038, OD-13).

Example message: `Union 'Clip' declares member 'Duration'; union body members are not supported yet (spec 009 OD-5).`

The rule is over **erased JS value domains**, not test identity (FR-013): two different test expressions can still accept the same value.

1. Case A converts implicitly to case B (`union S(IShape, Square)`, `union E(object, string)`).
2. Two cases share the `number` domain: `int` / `double`, `int` / `long`, a numeric enum / `int`, a `[Branded]` number / `double`.
3. Two cases share the `string` domain: `char` / `string`, `Guid` / `string`, a `[Branded]` string / `string`, a `[StringEnum]` / `string`, or two `[StringEnum]` enums.
4. One case is a class and the other a subclass of it.
5. Two `structural` cases (guarded interfaces or `[PlainObject]` types), whatever their shapes, or a `structural` case next to a class or record case (OD-15).
6. Two `array` cases.
7. Any of the above after type-argument substitution of a constructed generic union (`G<string>` for `union G<T>(T, string)`; FR-031).

Example message: `Union 'N': cases 'int' and 'double' both erase to JS 'number'; the erased union cannot tell them apart.`

**MS0034 — UntestableUnionCase**
1. An interface case without `[GenerateGuard]`.
2. A `[PlainObject]` case without `[GenerateGuard]`.
3. A delegate case, a case typed as TS `Iterable<T>` (`IEnumerable<T>`, `IReadOnlyCollection<T>`), or a non-transpilable class without `[Import]` / `[External]`. A single array-shaped case is testable (`Array.isArray`); two are MS0033.
4. A pattern test or JSON use against a type-parameter case that is still open at that site (`Opt<T>` with `T v =>`). A closed instance (`Opt<int>` with `int n =>`) is not reported.

**MS0035 — UnionDefaultValue**

The sources below are the only ones v1 diagnoses. Array elements, `FirstOrDefault`-style APIs, `TryGetValue(out U)` and generic `default(T)` also produce `default(U)` and are documented as known divergences in the spec's Edge Cases.

1. `default(U)`.
2. A target-typed `default` in a `U` position.
3. A non-nullable `U` field or auto-property that has no initializer and is not assigned by any constructor. It holds `default(U)`. C# forbids reading an unassigned local (CS0165), so locals are not a case.

**MS0036 — AmbiguousUnionJsonCases**
1. Two or more object-kind cases (records, classes, `[PlainObject]`, maps).
2. Any other shared token kind: two number cases, two string cases, or two array cases. MS0033 usually catches these first; MS0036 is still reported, so the JSON consequence is explicit.
3. A number case and a string case under `AllowReadingFromString` (including `JsonSerializerDefaults.Web`): both read the `String` token (research R10).

**MS0037 — UnionErasureDivergence**
1. `u.ToString()`, or a union value interpolated into a string or concatenated with one. C# prints the union name (`Clip`); TS prints the case value.
2. `u.GetType()`. C# returns the union type; TS sees the case.
3. `o is U`, `o as U`, or a `U` type pattern where `o`'s static type is not `U` / `U?`. A bare case boxed as `object` is not a `U` in C#.
4. (OD-6) The boxing conversion of a union to `object` / `ValueType` / an interface, followed by a case test.
5. `u.Equals(x)` where `x` is not statically `U` / `U?`. C# returns false when the boxed argument is not a `U` (`id.Equals(5)` is false; research R10); the erased `equals(id, 5)` returns true.
6. `HasValue` on a `Nullable<U>`. A `U?` holding `default(U)` has `HasValue == true` in C# but erases to `null`.
