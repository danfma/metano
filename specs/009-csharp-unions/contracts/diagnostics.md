# Contract: Union diagnostics (`MS0031`–`MS0036`)

**Normative for**: FR-012–FR-016, FR-026; D9. This spec PR reserves the codes in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md). Symbolic names become constants in `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` when they are implemented. Each reason below needs one test that asserts the code, the severity and the location.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0031` | `UnsupportedUnionShape` | Error | union declaration (or the attribute) |
| `MS0032` | `IndistinguishableUnionCases` | Error | union declaration |
| `MS0033` | `UntestableUnionCase` | Error | union declaration, or the pattern / JSON use site for a type-parameter case |
| `MS0034` | `UnionDefaultValue` | Error (OD-3) | the `default` expression, or the uninitialized member's declaration |
| `MS0035` | `AmbiguousUnionJsonCases` | Warning | the `JsonSerializerContext` declaration that reaches the union |
| `MS0036` | `UnionErasureDivergence` | Warning | the expression |

## Reasons

**MS0031 — UnsupportedUnionShape**
1. The union declares a body member (method, property, field, event, operator, or a non-case constructor).
2. The union implements an interface other than `IUnion`.
3. A case type is itself a union (nested unions; research R1).
4. The union carries `[PlainObject]`, `[Branded]`, `[InlineWrapper]`, `[StringEnum]`, `[ExportedAsModule]` or `[JsTuple]`.
5. A JSON-reachable union uses `[JsonUnion(TypeClassifier = …)]`.
6. A JSON-reachable union has a `[JsonPolymorphic]` case (until #268, OD-7).
7. A hand-written `[Union]` type has user members beyond its case constructors and `Value`.
8. A JSON-reachable union is listed as a `[JsonSerializable]` root (until OD-8 is resolved).

Example message: `Union 'Clip' declares member 'Duration'; union body members are not supported yet (spec 009 OD-5).`

**MS0032 — IndistinguishableUnionCases**
1. Case A converts implicitly to case B (`union S(IShape, Square)`, `union E(object, string)`).
2. Two cases share one `IrRuntimeTypeTest` result: `int` / `double`, `int` / `long`, or numeric enum / `int`, all of which test as `typeof === "number"`.
3. Two cases share the string test: `char` / `string`, `Guid` / `string`, a `[Branded]` string / `string`, or a `[StringEnum]` / `string`.
4. Two `[StringEnum]` cases have overlapping string values.

Example message: `Union 'N': cases 'int' and 'double' have the same runtime test in TypeScript ('number'); the erased union cannot tell them apart.`

**MS0033 — UntestableUnionCase**
1. An interface case without `[GenerateGuard]`.
2. A `[PlainObject]` case without `[GenerateGuard]`.
3. A case that is an array, a collection, or a non-transpilable class without `[Import]` / `[External]`.
4. A pattern test or JSON use against an open type-parameter case (`Opt<T>` with `T v =>`).

**MS0034 — UnionDefaultValue**
1. `default(U)`.
2. A target-typed `default` in a `U` position.
3. A non-nullable `U` field or auto-property that has no initializer and is not assigned by any constructor. It holds `default(U)`. C# forbids reading an unassigned local (CS0165), so locals are not a case.

**MS0035 — AmbiguousUnionJsonCases**
1. Two or more object-kind cases (records, classes, `[PlainObject]`, maps).
2. Any other shared token kind: two number cases, two string cases, or two array cases. MS0032 usually catches these first; MS0035 is still reported, so the JSON consequence is explicit.

**MS0036 — UnionErasureDivergence**
1. `u.ToString()`, or a union value interpolated into a string or concatenated with one. C# prints the union name (`Clip`); TS prints the case value.
2. `u.GetType()`. C# returns the union type; TS sees the case.
3. `o is U`, `o as U`, or a `U` type pattern where `o`'s static type is not `U` / `U?`. A bare case boxed as `object` is not a `U` in C#.
4. (OD-6) The boxing conversion of a union to `object` / `ValueType` / an interface, followed by a case test.
