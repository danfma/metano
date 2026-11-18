using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one TypeScript rendering of a runtime type test category
/// (<see cref="IrRuntimeTypeTest"/>, spec 011 FR-021, NFR-004): type patterns,
/// <c>is</c>, <c>as</c>, catch arms and overload dispatchers all render the
/// concrete test of a type here, and the guard builder's own checks share its
/// primitives. The rules:
/// <list type="bullet">
///   <item>a class tests <c>instanceof</c> its runtime class (a BCL exception
///   its runtime exception class, a temporal type its <c>Temporal</c> class,
///   the 2.x <c>Map</c> / <c>HashSet</c> / <c>Promise</c> their class);</item>
///   <item>an interface tests nominally through its descriptor,
///   <c>isType(v, IFoo$runtimeType)</c> (D29), and an enum by membership,
///   <c>isType(v, E$runtimeType)</c> (FR-049), when the type has a descriptor;</item>
///   <item>a primitive uses the validating <c>metano-runtime</c> helper
///   (<c>isInt32</c>, <c>isString</c>, …; FR-022) unless the static type
///   already decides the kind, where <c>typeof</c> suffices; <c>decimal</c>
///   tests its decimal.js class; a branded wrapper tests its underlying
///   primitive;</item>
///   <item>a <c>[PlainObject]</c> calls its <c>[GenerateGuard]</c> guard; a
///   <c>[JsTuple]</c> checks the array shape and length; an array
///   <c>Array.isArray</c>; an iterable keeps the 2.x <c>typeof "object"</c>;
///   <c>object</c> is <c>!= null</c>;</item>
///   <item>a constant category folds to its literal when reading the value has
///   no side effect.</item>
/// </list>
/// </summary>
internal static class IrToTsRuntimeTestBridge
{
    /// <summary>
    /// The test of <paramref name="value"/> against <paramref name="type"/>
    /// whose static type is <paramref name="staticType"/>, or <c>null</c> when
    /// the category has no test (<see cref="IrRuntimeTypeTest.None"/>, a plain
    /// object without a guard): each caller applies its own policy then.
    /// </summary>
    public static TsExpression? Render(
        TsExpression value,
        IrTypeRef type,
        IrTypeRef? staticType = null
    ) => Render(value, type, Classify(value, type, staticType));

    private static TsExpression? Render(
        TsExpression value,
        IrTypeRef type,
        IrRuntimeTypeTest test
    ) =>
        test switch
        {
            IrRuntimeTypeTest.Constant { Result: var result } => new TsLiteral(
                result ? "true" : "false"
            ),
            IrRuntimeTypeTest.NominalClass => ClassTest(value, type),
            IrRuntimeTypeTest.NominalInterface => DescriptorTest(value, type),
            IrRuntimeTypeTest.ErasedShape => GuardTest(value, type),
            IrRuntimeTypeTest.Primitive primitive => PrimitiveOrEnumTest(value, type, primitive),
            IrRuntimeTypeTest.FixedTuple { Arity: var arity } => FixedTupleTest(value, arity),
            IrRuntimeTypeTest.Array => ArrayTest(value),
            IrRuntimeTypeTest.Iterable => TypeofTest(value, "object"),
            IrRuntimeTypeTest.Null => new TsBinaryExpression(value, "===", new TsLiteral("null")),
            IrRuntimeTypeTest.Object => new TsBinaryExpression(value, "!=", new TsLiteral("null")),
            _ => null,
        };

    /// <summary>
    /// The category of the test; a constant fold is kept only when the value
    /// is read without side effects, since the folded test does not read it.
    /// </summary>
    private static IrRuntimeTypeTest Classify(
        TsExpression value,
        IrTypeRef type,
        IrTypeRef? staticType
    ) =>
        IrRuntimeTypeTest.Of(type, staticType) is IrRuntimeTypeTest.Constant constant
            ? IsPureRead(value)
                ? constant
                : IrRuntimeTypeTest.Of(type)
            : IrRuntimeTypeTest.Of(type, staticType);

    private static bool IsPureRead(TsExpression value) =>
        value switch
        {
            TsIdentifier or TsLiteral or TsStringLiteral => true,
            TsPropertyAccess { Optional: false } access => IsPureRead(access.Object),
            _ => false,
        };

    /// <summary><c>value instanceof C</c> against the class the type lowers to.</summary>
    private static TsExpression ClassTest(TsExpression value, IrTypeRef type) =>
        type switch
        {
            IrMapTypeRef => InstanceofTest(value, new TsIdentifier("Map")),
            IrSetTypeRef => InstanceofTest(value, new TsRuntimeHelperReference("HashSet")),
            IrPromiseTypeRef => InstanceofTest(value, new TsIdentifier("Promise")),
            IrNamedTypeRef named when BclExceptionClasses.IsBclException(named) =>
                IrToTsTypeMapper.ExceptionClasses.InstanceofTest(value, named),
            _ => InstanceofTest(value, ClassValueOf(type)),
        };

    private static TsExpression ClassValueOf(IrTypeRef type) =>
        IrToTsTypeMapper.Map(type) is TsNamedType mapped
            ? IrToTsExpressionBridge.TypeValueReference(mapped.Name, mapped.Origin)
            : throw new InvalidOperationException(
                $"Type '{type}' tests nominally but maps to no TypeScript class."
            );

    /// <summary><c>isType(value, descriptor)</c>: the nominal interface and enum test.</summary>
    private static TsExpression? DescriptorTest(TsExpression value, IrTypeRef type) =>
        IrToTsRuntimeTypeReferences.Of(type, "type test") is { } descriptor
            ? IsTypeCall(value, descriptor)
            : null;

    /// <summary><c>isType(value, descriptor)</c>.</summary>
    public static TsExpression IsTypeCall(TsExpression value, TsExpression descriptor) =>
        new TsCallExpression(new TsRuntimeHelperReference("isType"), [value, descriptor]);

    private static TsExpression? GuardTest(TsExpression value, IrTypeRef type) =>
        type is IrNamedTypeRef { Semantics.HasGeneratedGuard: true } named
            ? new TsCallExpression(new TsIdentifier($"is{GuardTypeName(named)}"), [value])
            : null;

    private static string GuardTypeName(IrNamedTypeRef named) =>
        IrToTsTypeMapper.Map(named) is TsNamedType mapped ? mapped.Name : named.Name;

    /// <summary>
    /// An enum tests membership in its declared values: through its descriptor
    /// when it has one, inline for a string enum without one (a type of
    /// another assembly whose values the reference knows); any other value of
    /// the category tests its primitive.
    /// </summary>
    private static TsExpression PrimitiveOrEnumTest(
        TsExpression value,
        IrTypeRef type,
        IrRuntimeTypeTest.Primitive primitive
    )
    {
        if (
            type
                is IrNamedTypeRef
                {
                    Semantics.Kind: IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum,
                } enumType
            && EnumMembershipTest(value, enumType) is { } membership
        )
            return membership;

        return PrimitiveTest(value, primitive.PrimitiveKind, primitive.StaticPrimitive is not null);
    }

    private static TsExpression? EnumMembershipTest(TsExpression value, IrNamedTypeRef enumType) =>
        DescriptorTest(value, enumType)
        ?? (
            enumType.Semantics?.StringEnumValues is { Count: > 0 } values
                ? values
                    .Select(member =>
                        (TsExpression)
                            new TsBinaryExpression(value, "===", new TsStringLiteral(member))
                    )
                    .Aggregate((left, right) => new TsBinaryExpression(left, "||", right))
                : null
        );

    /// <summary>
    /// The test of a value of <paramref name="primitive"/>: <c>typeof</c> when
    /// the static type already decides the kind, otherwise the validating
    /// helper that also checks the range (FR-022). A <c>decimal</c> is a
    /// decimal.js instance either way.
    /// </summary>
    public static TsExpression PrimitiveTest(
        TsExpression value,
        IrPrimitive primitive,
        bool staticTypeDecides = false
    )
    {
        if (primitive is IrPrimitive.Decimal)
            return InstanceofTest(value, IrToTsExpressionBridge.BclDecimalValueReference());

        return staticTypeDecides
            ? TypeofTest(value, TypeofKind(primitive))
            : new TsCallExpression(
                new TsRuntimeHelperReference(ValidatingHelperOf(primitive)),
                [value]
            );
    }

    private static string ValidatingHelperOf(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Char => "isChar",
            IrPrimitive.String or IrPrimitive.Guid => "isString",
            IrPrimitive.Byte => "isByte",
            IrPrimitive.Int16 => "isInt16",
            IrPrimitive.Int32 => "isInt32",
            IrPrimitive.Int64 => "isInt64",
            IrPrimitive.UInt64 => "isUInt64",
            IrPrimitive.Float32 => "isFloat32",
            IrPrimitive.Float64 => "isFloat64",
            IrPrimitive.Boolean => "isBool",
            IrPrimitive.BigInteger => "isBigInt",
            _ => throw new InvalidOperationException(
                $"Primitive '{primitive}' lowers to an object, not a primitive value."
            ),
        };

    /// <summary>
    /// The <c>typeof</c> result of the JS value a primitive lowers to; the
    /// numeric kinds share <c>"number"</c> and the 64-bit kinds
    /// <c>"bigint"</c> (spec 012).
    /// </summary>
    public static string TypeofKind(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.String or IrPrimitive.Char or IrPrimitive.Guid => "string",
            IrPrimitive.Boolean => "boolean",
            IrPrimitive.Int64 or IrPrimitive.UInt64 or IrPrimitive.BigInteger => "bigint",
            IrPrimitive.Int32
            or IrPrimitive.Int16
            or IrPrimitive.Byte
            or IrPrimitive.Float32
            or IrPrimitive.Float64
            or IrPrimitive.Decimal => "number",
            _ => throw new InvalidOperationException(
                $"Primitive '{primitive}' lowers to an object, not a typeof-testable value."
            ),
        };

    /// <summary><c>typeof value === "kind"</c>.</summary>
    public static TsExpression TypeofTest(TsExpression value, string kind) =>
        new TsBinaryExpression(
            new TsUnaryExpression("typeof ", value),
            "===",
            new TsStringLiteral(kind)
        );

    /// <summary><c>value instanceof target</c>.</summary>
    public static TsBinaryExpression InstanceofTest(TsExpression value, TsExpression target) =>
        new(value, "instanceof", target);

    /// <summary><c>Array.isArray(value)</c>.</summary>
    public static TsCallExpression ArrayTest(TsExpression value) =>
        new(new TsPropertyAccess(new TsIdentifier("Array"), "isArray"), [value]);

    /// <summary>
    /// The structural <see cref="IrRuntimeTypeTest.FixedTuple"/> test: an array
    /// of the tuple's length.
    /// </summary>
    public static TsBinaryExpression FixedTupleTest(TsExpression value, int arity) =>
        new(
            ArrayTest(value),
            "&&",
            new TsBinaryExpression(
                new TsPropertyAccess(value, "length"),
                "===",
                new TsLiteral(arity.ToString(CultureInfo.InvariantCulture))
            )
        );
}
