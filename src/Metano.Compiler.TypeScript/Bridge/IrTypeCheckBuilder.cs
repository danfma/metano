using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The overload dispatcher's runtime check that a value is a
/// <see cref="IrTypeRef"/>: only the dispatcher's own policy lives here, and
/// the concrete test of a type is <see cref="IrToTsRuntimeTestBridge"/>'s, the
/// one a pattern or a cast renders for the same type (spec 011 NFR-004).
/// <para>
/// The policy: <c>null</c> means "any value"; a nullable type also accepts
/// <c>null</c>; a type parameter has no test of its own, so its check never
/// rejects a value C# would accept (an unconstrained <c>T</c>, or a
/// <c>struct</c> one, has none, and a constrained one combines the checks of
/// its constraints); <c>object</c> accepts every value; the
/// <c>[ExportFromBcl]</c> type overrides test their class; a delegate checks
/// its arity, which tells delegate overloads apart; a type with no runtime
/// test, or a BCL class this compilation cannot name, falls back to
/// <c>typeof === "object"</c>.
/// </para>
/// </summary>
internal static class IrTypeCheckBuilder
{
    /// <summary>
    /// The check that <paramref name="value"/> is a <paramref name="type"/>,
    /// or <c>null</c> when any value matches.
    /// </summary>
    public static TsExpression? Generate(
        IrTypeRef type,
        TsExpression value,
        TypeCheckScope scope
    ) =>
        type switch
        {
            IrNullableTypeRef nullable => NullableCheck(nullable, value, scope),
            IrTypeParameterRef typeParameter => TypeParameterCheck(typeParameter, value, scope),
            // `object` accepts every value, a string or a number included.
            _ when type.IsObject => null,
            // A decimal tests like everywhere else (`isDecimal`, spec 011
            // NFR-004), whatever [ExportFromBcl] class it lowers to.
            IrPrimitiveTypeRef { Primitive: IrPrimitive.Decimal } => IrToTsRuntimeTestBridge.Render(
                value,
                type,
                staticType: null,
                scope.RuntimeTypes
            ),
            _ when scope.TypeOverrides?.TryResolve(type) is TsNamedType exported =>
                new TsBinaryExpression(
                    value,
                    "instanceof",
                    // The origin carries the export's package to the import
                    // collector; a bare identifier would leave the check
                    // without its import (issue #477).
                    exported.Origin
                        is { } origin
                        ? new TsTypeReference(exported.Name, origin)
                        : new TsIdentifier(exported.Name)
                ),
            IrFunctionTypeRef => FunctionCheck(value, scope.DelegateArityFloor),
            IrCollectionTypeRef collection => IrToTsRuntimeTestBridge.DispatchCollectionTest(
                value,
                collection
            ),
            IrNamedTypeRef named when HasNoNameableRuntimeClass(named) => TypeofObject(value),
            _ => IrToTsRuntimeTestBridge.Render(value, type, staticType: null, scope.RuntimeTypes)
                ?? TypeofObject(value),
        };

    /// <summary>
    /// Whether a value of <paramref name="type"/> has a runtime check at all;
    /// <c>false</c> when any value matches.
    /// </summary>
    public static bool HasCheck(IrTypeRef type, TypeCheckScope scope) =>
        Generate(
            type,
            new TsIdentifier("value"),
            scope with
            {
                RuntimeTypes = Analysis.RuntimeTypeReachabilityResult.Unchecked,
            }
        )
            is not null;

    /// <summary>
    /// A class of the BCL or of another non-transpiled assembly, and a BCL
    /// exception without a runtime class: an <c>instanceof</c> would name an
    /// identifier the output does not declare.
    /// </summary>
    private static bool HasNoNameableRuntimeClass(IrNamedTypeRef named) =>
        named.Semantics switch
        {
            { Kind: IrNamedTypeKind.Exception } => BclExceptionClasses.IsBclException(named)
                && !IrToTsTypeMapper.ExceptionClasses.IsMapped(named),
            {
                IsTranspilable: false,
                Kind: IrNamedTypeKind.Class or IrNamedTypeKind.Record or IrNamedTypeKind.Struct,
            } => true,
            _ => false,
        };

    /// <summary>
    /// Matches either <c>null</c> or the inner check; a nullable type
    /// parameter without a check matches any value.
    /// </summary>
    private static TsExpression? NullableCheck(
        IrNullableTypeRef nullable,
        TsExpression value,
        TypeCheckScope scope
    ) =>
        Generate(nullable.Inner, value, scope) is { } innerCheck
            ? new TsBinaryExpression(
                new TsBinaryExpression(value, "===", new TsLiteral("null")),
                "||",
                innerCheck
            )
            : null;

    /// <summary>
    /// The conjunction of the checks of the type parameter's constraints: a
    /// <c>class</c> constraint admits any reference value, a base class or
    /// an emitted interface keeps its own check, and every other constraint
    /// (<c>struct</c>, a BCL interface a primitive may implement, …) admits
    /// any value.
    /// </summary>
    private static TsExpression? TypeParameterCheck(
        IrTypeParameterRef typeParameter,
        TsExpression value,
        TypeCheckScope scope
    )
    {
        var declaration = scope.TypeParameters.FirstOrDefault(p => p.Name == typeParameter.Name);

        if (declaration is null)
            return null;

        var constraintChecks = (declaration.Constraints ?? []).Select(constraint =>
            ConstraintCheck(constraint, value, scope)
        );

        return AllOf([KindConstraintCheck(declaration.KindConstraint, value), .. constraintChecks]);
    }

    private static TsExpression? KindConstraintCheck(
        IrTypeKindConstraint kindConstraint,
        TsExpression value
    ) => kindConstraint is IrTypeKindConstraint.ReferenceType ? ReferenceCheck(value) : null;

    private static TsExpression? ConstraintCheck(
        IrTypeRef constraint,
        TsExpression value,
        TypeCheckScope scope
    ) =>
        constraint switch
        {
            IrTypeParameterRef typeParameter => TypeParameterCheck(typeParameter, value, scope),
            IrNamedTypeRef { Semantics.IsTranspilable: true } named => Generate(
                named,
                value,
                scope
            ),
            _ => null,
        };

    /// <summary>
    /// A C# reference value: an object (or <c>null</c>, which
    /// <c>where T : class?</c> admits and <c>typeof</c> reports as
    /// <c>"object"</c>), a function, or a string, the one reference type that
    /// lowers to a JS primitive. Known gap: a boxed primitive
    /// (<c>R&lt;object&gt;(5)</c>) lowers to the bare primitive and fails it.
    /// </summary>
    private static TsExpression ReferenceCheck(TsExpression value) =>
        new TsBinaryExpression(
            new TsBinaryExpression(
                TypeofObject(value),
                "||",
                IrToTsRuntimeTestBridge.TypeofTest(value, "function")
            ),
            "||",
            IrToTsRuntimeTestBridge.TypeofTest(value, "string")
        );

    /// <summary>
    /// The conjunction of the present <paramref name="checks"/>, or
    /// <c>null</c> when none is.
    /// </summary>
    internal static TsExpression? AllOf(IReadOnlyList<TsExpression?> checks) =>
        checks
            .OfType<TsExpression>()
            .Aggregate((TsExpression?)null, (all, check) =>
                all is null ? check : new TsBinaryExpression(all, "&&", check)
            );

    /// <summary>
    /// A delegate parameter: any function, or, when the dispatcher tells
    /// delegates of different arities apart at this position, a function of
    /// at least <paramref name="arityFloor"/> parameters (tried before the
    /// delegate of fewest parameters, which takes any function).
    /// <c>Function.length</c> leaves out ignored, defaulted and rest
    /// parameters (<c>() =&gt; 42</c> is a valid <c>(arg: number) =&gt;
    /// number</c>), so it is never an exact test, and delegates of one arity
    /// share the same test (#778, <see cref="ShadowedDispatchOverloads"/>).
    /// </summary>
    private static TsExpression FunctionCheck(TsExpression value, int? arityFloor)
    {
        var isFunction = IrToTsRuntimeTestBridge.TypeofTest(value, "function");

        if (arityFloor is not { } floor)
            return isFunction;

        var arityCheck = new TsBinaryExpression(
            new TsPropertyAccess(value, "length"),
            ">=",
            new TsLiteral(floor.ToString(System.Globalization.CultureInfo.InvariantCulture))
        );

        return new TsBinaryExpression(isFunction, "&&", arityCheck);
    }

    private static TsExpression TypeofObject(TsExpression value) =>
        IrToTsRuntimeTestBridge.TypeofTest(value, "object");
}
