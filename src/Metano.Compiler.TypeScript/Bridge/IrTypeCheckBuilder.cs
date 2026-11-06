using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Produces the runtime <see cref="TsExpression"/> that narrows a value to a
/// specific <see cref="IrTypeRef"/> in the overload dispatcher body.
/// <para>
/// Emits <c>metano-runtime</c> type-check helpers (<c>isInt32</c>,
/// <c>isFloat64</c>, <c>isBool</c>, …) so numeric overloads like
/// <c>M(int)</c>/<c>M(double)</c> stay distinguishable at runtime. Shapes
/// the IR models at a higher level than raw primitives (arrays, maps, sets,
/// named types) use structural checks; anything unmappable collapses to
/// <c>typeof value === "object"</c>.
/// </para>
/// <para>
/// A type parameter has no runtime test of its own, so its check never
/// rejects a value C# would accept: an unconstrained <c>T</c> (or a
/// <c>struct</c> one, whose arguments include primitives) has no check, and
/// a constrained one combines the checks of its constraints. A <c>null</c>
/// result means "any value".
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
            _ => ConcreteCheck(type, value),
        };

    /// <summary>
    /// Whether a value of <paramref name="type"/> has a runtime check at all;
    /// <c>false</c> when any value matches.
    /// </summary>
    public static bool HasCheck(IrTypeRef type, TypeCheckScope scope) =>
        Generate(type, new TsIdentifier("value"), scope) is not null;

    private static TsExpression ConcreteCheck(IrTypeRef type, TsExpression value) =>
        type switch
        {
            IrPrimitiveTypeRef primitive => PrimitiveCheck(primitive.Primitive, value),
            IrFunctionTypeRef func => FunctionCheck(func, value),
            IrArrayTypeRef => new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                [value]
            ),
            IrMapTypeRef => new TsBinaryExpression(value, "instanceof", new TsIdentifier("Map")),
            IrSetTypeRef => new TsBinaryExpression(
                value,
                "instanceof",
                new TsRuntimeHelperReference("HashSet")
            ),
            // Iterable (IReadOnlyCollection<T>) lowers to structural "any object" — legacy
            // emitted the same fallback because it couldn't produce a cheaper runtime test.
            IrIterableTypeRef or IrIteratorTypeRef => TypeofObject(value),
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
            IrNamedTypeRef { Semantics.IsTranspilable: true } named => NamedTypeCheck(named, value),
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
            new TsBinaryExpression(TypeofObject(value), "||", TypeofTest(value, "function")),
            "||",
            TypeofTest(value, "string")
        );

    /// <summary>
    /// The conjunction of the present <paramref name="checks"/>, or
    /// <c>null</c> when none is.
    /// </summary>
    internal static TsExpression? AllOf(IReadOnlyList<TsExpression?> checks) =>
        checks
            .OfType<TsExpression>()
            .Aggregate(
                (TsExpression?)null,
                (all, check) => all is null ? check : new TsBinaryExpression(all, "&&", check)
            );

    private static TsExpression NamedTypeCheck(IrNamedTypeRef named, TsExpression value) =>
        named.Semantics?.Kind switch
        {
            IrNamedTypeKind.StringEnum => StringEnumCheck(named, value),
            IrNamedTypeKind.NumericEnum => RuntimeIs("isInt32", value),
            IrNamedTypeKind.Interface => TypeofObject(value),
            IrNamedTypeKind.Branded => BrandedCheck(named, value),
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
        return values
            .Select<string, TsExpression>(v => new TsBinaryExpression(
                value,
                "===",
                new TsStringLiteral(v)
            ))
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));
    }

    private static TsExpression BrandedCheck(IrNamedTypeRef named, TsExpression value) =>
        IrRuntimeTypeTest.Of(named) is IrRuntimeTypeTest.Typeof { Primitive: var primitive }
            ? TypeofTest(value, TypeofKind(primitive))
            : TypeofObject(value);

    /// <summary>
    /// The <c>typeof</c> result of the JS value a primitive that
    /// <see cref="IrRuntimeTypeTest.Typeof"/> classifies lowers to. Numeric
    /// kinds all share <c>"number"</c>, so a <c>typeof</c> test cannot tell
    /// <c>int</c> from <c>double</c> — except the 64-bit kinds in the staging
    /// BigInt mode (spec 012 DECIDED-22, FR-048): there they lower to
    /// <c>bigint</c>, so <c>o is long</c> tests <c>typeof o === "bigint"</c>
    /// and answers a raw <c>bigint</c> where <c>Number</c> mode answers a
    /// number.
    /// </summary>
    internal static string TypeofKind(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.String or IrPrimitive.Char or IrPrimitive.Guid => "string",
            IrPrimitive.Boolean => "boolean",
            IrPrimitive.Int64 or IrPrimitive.UInt64 when Int64RepresentationScope.IsBigInt =>
                "bigint",
            IrPrimitive.Int32
            or IrPrimitive.Int64
            or IrPrimitive.UInt64
            or IrPrimitive.Int16
            or IrPrimitive.Byte
            or IrPrimitive.Float32
            or IrPrimitive.Float64
            or IrPrimitive.Decimal => "number",
            IrPrimitive.BigInteger => "bigint",
            _ => throw new InvalidOperationException(
                $"Primitive '{primitive}' lowers to an object, not a typeof-testable value."
            ),
        };

    internal static TsExpression TypeofTest(TsExpression value, string kind) =>
        new TsBinaryExpression(
            new TsUnaryExpression("typeof ", value),
            "===",
            new TsStringLiteral(kind)
        );

    /// <summary>
    /// The runtime test for a <c>decimal</c> value: an <c>instanceof Decimal</c>
    /// against the decimal.js class the type lowers to. Every other numeric
    /// check (<c>typeof === "number"</c>) would match plain JS numbers no C#
    /// decimal ever produces and reject every real decimal. The right-hand
    /// side carries the export's origin (#477): the collector imports
    /// decimal.js for exactly this node, and a project type named Decimal
    /// cannot capture the test.
    /// </summary>
    internal static TsExpression DecimalInstanceof(TsExpression value) =>
        new TsBinaryExpression(
            value,
            "instanceof",
            IrToTsExpressionBridge.BclDecimalValueReference()
        );

    /// <summary>
    /// Maps a semantic <see cref="IrPrimitive"/> to the same <c>metano-runtime</c>
    /// helper the legacy dispatcher uses, so same-arity numeric overloads
    /// (e.g. <c>M(int)</c> vs <c>M(double)</c>) remain distinguishable at
    /// runtime. <c>decimal</c> piggy-backs on <c>isFloat64</c> because the type
    /// mapper lowers it to <c>number</c> at the signature level. The 64-bit
    /// kinds read the staging switch through <see cref="Int64TypeCheckName"/>:
    /// the BigInt mode dispatches with the <c>bigint</c> range checks, the
    /// Number mode with the shipped <c>number</c> checks (FR-048).
    /// </summary>
    private static TsExpression PrimitiveCheck(IrPrimitive primitive, TsExpression value) =>
        primitive switch
        {
            IrPrimitive.Char => RuntimeIs("isChar", value),
            IrPrimitive.String => RuntimeIs("isString", value),
            IrPrimitive.Byte => RuntimeIs("isByte", value),
            IrPrimitive.Int16 => RuntimeIs("isInt16", value),
            IrPrimitive.Int32 => RuntimeIs("isInt32", value),
            IrPrimitive.Int64 => RuntimeIs(Int64TypeCheckName.OfInt64, value),
            IrPrimitive.UInt64 => RuntimeIs(Int64TypeCheckName.OfUInt64, value),
            IrPrimitive.Float32 => RuntimeIs("isFloat32", value),
            IrPrimitive.Float64 => RuntimeIs("isFloat64", value),
            IrPrimitive.Boolean => RuntimeIs("isBool", value),
            // Decimal always lowers to a decimal.js `Decimal` instance (the
            // shipped [ExportFromBcl] mapping in Metano.Runtime), so the
            // runtime test is instanceof — the one check that distinguishes a
            // decimal overload from a same-arity int one (a boxed decimal is
            // not an int in C#). Generate resolves the [ExportFromBcl]
            // override to the same test when the scope carries it; scopes
            // without one (companion namespaces) reach this arm.
            IrPrimitive.Decimal => DecimalInstanceof(value),
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

    private static TsExpression FunctionCheck(IrFunctionTypeRef func, TsExpression value)
    {
        var typeofCheck = new TsBinaryExpression(
            new TsUnaryExpression("typeof ", value),
            "===",
            new TsStringLiteral("function")
        );

        var arity = func.Parameters.Count;
        var arityCheck = new TsBinaryExpression(
            new TsPropertyAccess(value, "length"),
            "===",
            new TsLiteral(arity.ToString())
        );

        return new TsBinaryExpression(typeofCheck, "&&", arityCheck);
    }

    /// <summary>
    /// The call goes through <see cref="TsRuntimeHelperReference"/>, not a bare
    /// identifier: when a user declaration in the module claims the helper's
    /// name, the import collector aliases the runtime import and the printer
    /// routes the call through the alias instead of silently calling the user's
    /// function (issue #484).
    /// </summary>
    private static TsExpression RuntimeIs(string helper, TsExpression value) =>
        new TsCallExpression(new TsRuntimeHelperReference(helper), [value]);

    private static TsExpression TypeofObject(TsExpression value) => TypeofTest(value, "object");
}
