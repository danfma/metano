using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Produces the runtime <see cref="TsExpression"/> that narrows <c>args[i]</c>
/// to a specific <see cref="IrTypeRef"/> in the overload dispatcher body.
/// <para>
/// Emits <c>metano-runtime</c> type-check helpers (<c>isInt32</c>,
/// <c>isFloat64</c>, <c>isBool</c>, …) so numeric overloads like
/// <c>M(int)</c>/<c>M(double)</c> stay distinguishable at runtime. Shapes
/// the IR models at a higher level than raw primitives (arrays, maps, sets,
/// named types) use structural checks; anything unmappable collapses to
/// <c>typeof value === "object"</c>.
/// </para>
/// </summary>
internal static class IrTypeCheckBuilder
{
    public static TsExpression GenerateForParam(IrTypeRef parameterType, int index) =>
        Generate(parameterType, ArgAccess(index));

    private static TsExpression Generate(IrTypeRef type, TsExpression value) =>
        type switch
        {
            // Nullable → matches either null or the inner check.
            IrNullableTypeRef nullable => new TsBinaryExpression(
                new TsBinaryExpression(value, "===", new TsLiteral("null")),
                "||",
                Generate(nullable.Inner, value)
            ),
            IrPrimitiveTypeRef primitive => PrimitiveCheck(primitive.Primitive, value),
            IrArrayTypeRef => new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                [value]
            ),
            IrMapTypeRef => new TsBinaryExpression(value, "instanceof", new TsIdentifier("Map")),
            IrSetTypeRef => new TsBinaryExpression(
                value,
                "instanceof",
                new TsIdentifier("HashSet")
            ),
            // Iterable (IReadOnlyCollection<T>) lowers to structural "any object" — legacy
            // emitted the same fallback because it couldn't produce a cheaper runtime test.
            IrIterableTypeRef => TypeofObject(value),
            IrPromiseTypeRef => new TsBinaryExpression(
                value,
                "instanceof",
                new TsIdentifier("Promise")
            ),
            // Named types: branch on the semantic kind so enums and interfaces
            // don't produce an impossible `instanceof` against something that
            // isn't a runtime class. Classes/records fall through to the
            // legacy shape.
            IrNamedTypeRef named => NamedTypeCheck(named, value),
            _ => TypeofObject(value),
        };

    private static TsExpression NamedTypeCheck(IrNamedTypeRef named, TsExpression value) =>
        named.Semantics?.Kind switch
        {
            IrNamedTypeKind.StringEnum => StringEnumCheck(named, value),
            IrNamedTypeKind.NumericEnum => RuntimeIs("isInt32", value),
            IrNamedTypeKind.Interface => TypeofObject(value),
            IrNamedTypeKind.InlineWrapper => InlineWrapperCheck(named, value),
            // Classes / records / structs / exceptions — use instanceof when
            // the type is in our compilation (and thus has a runtime class);
            // otherwise fall back to typeof object so we don't reference an
            // undefined identifier.
            IrNamedTypeKind.Class
            or IrNamedTypeKind.Record
            or IrNamedTypeKind.Struct
            or IrNamedTypeKind.Exception when named.Semantics is { IsTranspilable: true } =>
                new TsBinaryExpression(value, "instanceof", new TsIdentifier(named.Name)),
            // No semantics attached (older call sites) keep the legacy
            // `instanceof` shape so behavior doesn't regress.
            null => new TsBinaryExpression(value, "instanceof", new TsIdentifier(named.Name)),
            _ => TypeofObject(value),
        };

    private static TsExpression StringEnumCheck(IrNamedTypeRef named, TsExpression value)
    {
        if (named.Semantics?.StringEnumValues is not { Count: > 0 } values)
            return RuntimeIs("isString", value);
        var check = values
            .Select<string, TsExpression>(v => new TsBinaryExpression(
                value,
                "===",
                new TsStringLiteral(v)
            ))
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));
        return new TsParenthesized(check);
    }

    private static TsExpression InlineWrapperCheck(IrNamedTypeRef named, TsExpression value) =>
        named.Semantics?.InlineWrappedPrimitive switch
        {
            IrPrimitive.String or IrPrimitive.Char or IrPrimitive.Guid => new TsBinaryExpression(
                new TsUnaryExpression("typeof ", value),
                "===",
                new TsStringLiteral("string")
            ),
            IrPrimitive.Boolean => new TsBinaryExpression(
                new TsUnaryExpression("typeof ", value),
                "===",
                new TsStringLiteral("boolean")
            ),
            IrPrimitive.Int32
            or IrPrimitive.Int64
            or IrPrimitive.Int16
            or IrPrimitive.Byte
            or IrPrimitive.Float32
            or IrPrimitive.Float64
            or IrPrimitive.Decimal => new TsBinaryExpression(
                new TsUnaryExpression("typeof ", value),
                "===",
                new TsStringLiteral("number")
            ),
            IrPrimitive.BigInteger => new TsBinaryExpression(
                new TsUnaryExpression("typeof ", value),
                "===",
                new TsStringLiteral("bigint")
            ),
            _ => TypeofObject(value),
        };

    /// <summary>
    /// Maps a semantic <see cref="IrPrimitive"/> to the same <c>metano-runtime</c>
    /// helper the legacy dispatcher uses, so same-arity numeric overloads
    /// (e.g. <c>M(int)</c> vs <c>M(double)</c>) remain distinguishable at
    /// runtime. <c>decimal</c> piggy-backs on <c>isFloat64</c> because the type
    /// mapper lowers it to <c>number</c> at the signature level.
    /// </summary>
    private static TsExpression PrimitiveCheck(IrPrimitive primitive, TsExpression value) =>
        primitive switch
        {
            IrPrimitive.Char => RuntimeIs("isChar", value),
            IrPrimitive.String => RuntimeIs("isString", value),
            IrPrimitive.Byte => RuntimeIs("isByte", value),
            IrPrimitive.Int16 => RuntimeIs("isInt16", value),
            IrPrimitive.Int32 => RuntimeIs("isInt32", value),
            IrPrimitive.Int64 => RuntimeIs("isInt64", value),
            IrPrimitive.Float32 => RuntimeIs("isFloat32", value),
            IrPrimitive.Float64 => RuntimeIs("isFloat64", value),
            IrPrimitive.Boolean => RuntimeIs("isBool", value),
            // Decimal surfaces in TS as `number` (see IrToTsTypeMapper), so the
            // runtime guard must also be `isFloat64` — instanceof Decimal would
            // never match the values the signature advertises.
            IrPrimitive.Decimal => RuntimeIs("isFloat64", value),
            IrPrimitive.BigInteger => RuntimeIs("isBigInt", value),
            IrPrimitive.Guid => RuntimeIs("isString", value),
            // DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan surface in TS as
            // concrete Temporal subclasses (PlainDate, ZonedDateTime, …), each
            // with its own runtime constructor. Without a stable class name in
            // IR we mirror the legacy fallback: typeof === "object". This keeps
            // the dispatcher selecting the correct arity and lets TS's
            // structural typing do the rest at the call site.
            IrPrimitive.DateTime
            or IrPrimitive.DateTimeOffset
            or IrPrimitive.DateOnly
            or IrPrimitive.TimeOnly
            or IrPrimitive.TimeSpan => TypeofObject(value),
            IrPrimitive.Object or IrPrimitive.Void => TypeofObject(value),
            _ => TypeofObject(value),
        };

    private static TsExpression RuntimeIs(string helper, TsExpression value) =>
        new TsCallExpression(new TsIdentifier(helper), [value]);

    private static TsExpression TypeofObject(TsExpression value) =>
        new TsBinaryExpression(
            new TsUnaryExpression("typeof ", value),
            "===",
            new TsStringLiteral("object")
        );

    private static TsExpression ArgAccess(int index) => new TsIdentifier($"args[{index}]");
}
