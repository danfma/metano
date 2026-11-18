using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Analysis;
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
///   <c>isType(v, IFoo$runtimeType)</c> (D29);</item>
///   <item>an enum tests its values inline (<see cref="IrToTsEnumValueTest"/>),
///   reading no descriptor;</item>
///   <item>a primitive uses the validating <c>metano-runtime</c> check of
///   <see cref="TsRuntimePrimitive"/> (<c>isInt32</c>, <c>isUInt32</c>,
///   <c>isDecimal</c>, …; FR-022) unless the static type already decides the
///   kind, where <c>typeof</c> suffices; a branded wrapper tests its
///   underlying primitive;</item>
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
    /// object without a guard): each caller applies its own policy then. An
    /// interface's descriptor read is checked against
    /// <paramref name="reachability"/> when one is given.
    /// </summary>
    public static TsExpression? Render(
        TsExpression value,
        IrTypeRef type,
        IrTypeRef? staticType,
        RuntimeTypeReachabilityResult reachability
    ) => Render(value, type, Classify(value, type, staticType), reachability);

    private static TsExpression? Render(
        TsExpression value,
        IrTypeRef type,
        IrRuntimeTypeTest test,
        RuntimeTypeReachabilityResult reachability
    ) =>
        test switch
        {
            IrRuntimeTypeTest.Constant { Result: var result } => new TsLiteral(
                result ? "true" : "false"
            ),
            IrRuntimeTypeTest.NominalClass => ClassTest(value, type),
            IrRuntimeTypeTest.NominalInterface => DescriptorTest(value, type, reachability),
            IrRuntimeTypeTest.ErasedShape => ShapeTest(value, type, reachability),
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

    /// <summary>
    /// <c>isType(value, descriptor)</c>: the nominal interface test. An
    /// interface of a referenced library that this compilation's
    /// <c>[PlainObject]</c> types implement is augmented with them at the test
    /// site, <c>withImplementers(IFoo$runtimeType, [Dto$runtimeType])</c>
    /// (FR-019): no registration, no mutation of the library descriptor.
    /// </summary>
    private static TsExpression? DescriptorTest(
        TsExpression value,
        IrTypeRef type,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (IrToTsRuntimeTypeReferences.Of(type, "type test", reachability) is not { } descriptor)
            return null;

        if (
            type is IrNamedTypeRef { Origin: not null } named
            && reachability.LibraryPlainImplementers.TryGetValue(
                named.SupertypeKey,
                out var implementers
            )
        )
        {
            descriptor = new TsCallExpression(
                new TsRuntimeHelperReference("withImplementers"),
                [
                    descriptor,
                    new TsArrayLiteral([
                        .. implementers.Select(IrToTsRuntimeTypeReferences.LocalExportReference),
                    ]),
                ]
            );
        }

        return IsTypeCall(value, descriptor);
    }

    /// <summary><c>isType(value, descriptor)</c>.</summary>
    public static TsExpression IsTypeCall(TsExpression value, TsExpression descriptor) =>
        new TsCallExpression(new TsRuntimeHelperReference("isType"), [value, descriptor]);

    /// <summary>
    /// The test of <paramref name="type"/> as a function value, for a site that
    /// stores it (a polymorphic JSON case's <c>test</c>, NFR-004): the guard
    /// itself when the test calls one on the value (<c>isPoint</c>), else
    /// <c>(v: unknown): boolean =&gt; …</c> over the same rendering.
    /// </summary>
    public static TsExpression? TestFunction(
        IrTypeRef type,
        RuntimeTypeReachabilityResult reachability
    )
    {
        var parameter = new TsIdentifier("v");

        return Render(parameter, type, staticType: null, reachability) switch
        {
            null => null,
            TsCallExpression { Arguments: [TsIdentifier { Name: "v" }] } call => call.Callee,
            var test => new TsArrowFunction(
                [new TsParameter(parameter.Name, new TsNamedType("unknown"))],
                [new TsReturnStatement(test)]
            ),
        };
    }

    /// <summary>
    /// A <c>[PlainObject]</c> tests its shape (FR-020): the structural guard
    /// synthesized for it, <c>isDto(value)</c>, a referenced library's
    /// imported from its package. A guard the reachability phase did not ask
    /// for is an internal error, as a descriptor read is.
    /// </summary>
    private static TsExpression? ShapeTest(
        TsExpression value,
        IrTypeRef type,
        RuntimeTypeReachabilityResult reachability
    )
    {
        if (type is not IrNamedTypeRef named)
            return null;

        if (named.Origin is null)
            IrToTsRuntimeTypeReferences.AssertGuarded(named, reachability);

        return new TsCallExpression(GuardReference(named), [value]);
    }

    /// <summary>
    /// The synthesized guard of <paramref name="named"/>, named as
    /// <see cref="Transformation.TypeGuardBuilder"/> declares it: <c>isPoint</c>
    /// (a local import, or from the type's package); a nested type's,
    /// <c>Shape.isCircle</c>, sits in its companion namespace.
    /// </summary>
    public static TsExpression GuardReference(IrNamedTypeRef named)
    {
        var mapped = IrToTsTypeMapper.Map(named) as TsNamedType;
        var segments = (mapped?.Name ?? named.Name).Split('.');
        var guardName = $"is{segments[^1]}";
        var origin = mapped?.Origin;

        if (segments.Length == 1)
            return origin is null
                ? new TsIdentifier(guardName)
                : new TsTypeReference(guardName, origin);

        TsExpression root = origin is null
            ? new TsIdentifier(segments[0])
            : new TsTypeReference(segments[0], origin);

        var container = segments[1..^1]
            .Aggregate(root, (outer, segment) => new TsPropertyAccess(outer, segment));

        return new TsPropertyAccess(container, guardName);
    }

    /// <summary>
    /// An enum tests its values inline (<see cref="IrToTsEnumValueTest"/>); any
    /// other value of the category tests its primitive.
    /// </summary>
    private static TsExpression PrimitiveOrEnumTest(
        TsExpression value,
        IrTypeRef type,
        IrRuntimeTypeTest.Primitive primitive
    )
    {
        if (
            type is IrNamedTypeRef
            {
                Semantics.Kind: IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum,
            } enumType
        )
            return IrToTsEnumValueTest.Of(value, enumType);

        var runtime = TsRuntimePrimitive.Of(primitive.PrimitiveKind, primitive.ExactKind);

        return primitive.StaticPrimitive is null
            ? ValidatingTest(value, runtime, primitive.PrimitiveKind)
            : KindTest(value, runtime, primitive.PrimitiveKind);
    }

    /// <summary>The validating check of a value of <paramref name="primitive"/>.</summary>
    public static TsExpression PrimitiveTest(TsExpression value, IrPrimitive primitive) =>
        ValidatingTest(value, TsRuntimePrimitive.Of(primitive), primitive);

    /// <summary>
    /// The validating check of a value whose static type cannot decide its kind
    /// (FR-022): the integral kinds check their range too.
    /// </summary>
    public static TsExpression ValidatingTest(
        TsExpression value,
        TsRuntimePrimitive? runtime,
        IrPrimitive primitive
    ) =>
        runtime?.Validator is { } validator
            ? new TsCallExpression(new TsRuntimeHelperReference(validator), [value])
            : throw new InvalidOperationException(
                $"Primitive '{primitive}' lowers to an object, not a primitive value."
            );

    /// <summary>
    /// The test of a value whose static type already decides its kind (a
    /// nullable of it): <c>typeof</c>, or the validating check of a kind whose
    /// value is an object (<c>decimal</c>).
    /// </summary>
    private static TsExpression KindTest(
        TsExpression value,
        TsRuntimePrimitive? runtime,
        IrPrimitive primitive
    ) =>
        runtime?.TypeofKind is { } kind
            ? TypeofTest(value, kind)
            : ValidatingTest(value, runtime, primitive);

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
