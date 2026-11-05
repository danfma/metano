using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrMethodDeclaration"/> whose
/// <see cref="IrMethodDeclaration.Overloads"/> carries the overload set into the
/// canonical TypeScript dispatcher shape:
/// <list type="number">
///   <item>A private fast-path method per overload (real body via
///   <see cref="IrToTsStatementBridge"/>), named uniquely within the group.</item>
///   <item>A public dispatcher method taking <c>...args: unknown[]</c> that
///   branches on argument count + runtime type checks and delegates to the
///   matching fast path with cast arguments.</item>
/// </list>
/// The branches follow <see cref="OverloadDispatchOrder"/> and match the
/// arguments as <see cref="OverloadArgumentShape"/> describes. An omitted
/// optional argument reaches its fast path as <c>undefined</c>, where the
/// parameter's own default applies; the overload signatures mark it
/// optional (<c>a?: number</c>), since they cannot carry the default.
/// </summary>
public static class IrToTsOverloadDispatcherBridge
{
    /// <summary>
    /// Builds the dispatcher + fast-path members for a group of overloads.
    /// </summary>
    /// <param name="primary">
    /// The method that groups the overloads. Its
    /// <see cref="IrMethodDeclaration.Overloads"/> carries the siblings. When
    /// <c>Overloads</c> is null/empty this helper returns an empty list —
    /// callers should pick the regular single-method path in that case.
    /// </param>
    /// <param name="containingTypeName">
    /// Name of the class that hosts the overloads — used as the static
    /// receiver (<c>ClassName.fastPath(…)</c>). For instance overloads the
    /// dispatcher uses <c>this</c>, so the argument only matters when
    /// <paramref name="primary"/> is static.
    /// </param>
    /// <param name="classTypeParameters">
    /// The containing class's type parameters, which a parameter's type check
    /// resolves its type parameters against after the method's own.
    /// </param>
    internal static IReadOnlyList<TsClassMember> BuildMethod(
        IrMethodDeclaration primary,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IrToTsBridgeContext context
    )
    {
        var primaryName = TypeScriptNaming.ToCamelCaseMember(primary.Name);
        var typeOverrides = context.TypeOverrides;
        var plan = PlanOverloadGroup(CollectOverloads(primary), primaryName, typeOverrides);

        if (plan is null)
            return [];

        var isStatic = primary.IsStatic;
        var members = new List<TsClassMember>();

        for (var i = 0; i < plan.Sorted.Count; i++)
        {
            var fastPath = BuildFastPathMember(
                plan.Sorted[i],
                plan.FastPathNames[i],
                isStatic,
                context
            );

            if (fastPath is not null)
                members.Add(fastPath);
        }

        var receiver = DispatchReceiver(isStatic, containingTypeName);

        members.Add(
            new TsMethodMember(
                primaryName,
                DispatcherParameters(),
                plan.CommonReturn,
                BuildDispatcherBody(
                    plan.Sorted,
                    plan.FastPathNames,
                    receiver,
                    new TypeCheckScope(classTypeParameters, typeOverrides)
                ),
                Static: isStatic,
                Async: plan.IsAsync,
                Accessibility: VisibilityToAccessibility(primary.Visibility),
                Overloads: plan.Signatures
            )
        );

        return members;
    }

    /// <summary>
    /// Builds the dispatcher + fast paths for a group of static overloads
    /// that lowers to free functions inside a TS namespace (the
    /// <c>[Branded]</c> companion) instead of class members. The fast paths
    /// are non-exported namespace-local functions called by bare name; the
    /// exported dispatcher carries the overload signatures.
    /// </summary>
    /// <param name="overloads">
    /// The overload group, two or more methods — the caller lowers a single
    /// method through its regular path.
    /// </param>
    /// <param name="functionName">
    /// Emitted dispatcher name — already reserved-word escaped, since
    /// namespace function declarations can't use reserved words.
    /// </param>
    /// <param name="takenNames">
    /// Function names already claimed in the namespace. Namespace functions
    /// share one scope, so a fast-path name that collides with one of them
    /// (or with a sibling fast path) gets a <c>$</c> suffix; the chosen names
    /// are added to the set.
    /// </param>
    /// <param name="bclRegistry">
    /// Optional BCL mapping registry threaded through to the body bridge.
    /// </param>
    public static IReadOnlyList<TsFunction> BuildNamespaceFunctions(
        IReadOnlyList<IrMethodDeclaration> overloads,
        string functionName,
        ISet<string> takenNames,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        var plan =
            PlanOverloadGroup(overloads, functionName, typeOverrides: null)
            ?? throw new ArgumentException(
                "An overload group needs two or more methods.",
                nameof(overloads)
            );

        var fastPathNames = plan.FastPathNames.Select(name => ClaimName(name, takenNames)).ToList();

        var functions = plan
            .Sorted.Select(
                (method, i) => BuildFastPathFunction(method, fastPathNames[i], bclRegistry)
            )
            .ToList();

        functions.Add(
            new TsFunction(
                functionName,
                DispatcherParameters(),
                plan.CommonReturn,
                BuildDispatcherBody(
                    plan.Sorted,
                    fastPathNames,
                    receiver: null,
                    TypeCheckScope.Empty
                ),
                Exported: true,
                Async: plan.IsAsync,
                Overloads: plan.Signatures
            )
        );

        return functions;
    }

    // ── Phases ───────────────────────────────────────────────────────────

    /// <summary>
    /// The target-shape-independent part of a dispatcher: the overloads in
    /// dispatch order, their fast-path names, the async flag, the
    /// implementation return type and the public overload signatures.
    /// </summary>
    private sealed record OverloadGroupPlan(
        IReadOnlyList<IrMethodDeclaration> Sorted,
        IReadOnlyList<string> FastPathNames,
        bool IsAsync,
        TsType CommonReturn,
        IReadOnlyList<TsMethodOverload> Signatures
    );

    /// <summary>
    /// Plans the dispatcher for <paramref name="overloads"/>, or returns
    /// <c>null</c> when there are fewer than two (no dispatch needed).
    /// </summary>
    private static OverloadGroupPlan? PlanOverloadGroup(
        IReadOnlyList<IrMethodDeclaration> overloads,
        string dispatcherName,
        IrToTsTypeOverrides? typeOverrides
    )
    {
        if (overloads.Count < 2)
            return null;

        var sorted = OverloadDispatchOrder.Sort(overloads, m => m.Parameters);
        var isAsync = sorted.Any(m => m.Semantics.IsAsync);

        return new OverloadGroupPlan(
            sorted,
            ComputeFastPathNames(dispatcherName, sorted),
            isAsync,
            ResolveCommonReturnType(sorted, isAsync, typeOverrides),
            sorted.Select(method => BuildOverloadSignature(method, typeOverrides)).ToList()
        );
    }

    private static List<IrMethodDeclaration> CollectOverloads(IrMethodDeclaration primary)
    {
        var list = new List<IrMethodDeclaration> { primary };
        if (primary.Overloads is { Count: > 0 } others)
            list.AddRange(others);
        return list;
    }

    /// <summary>
    /// The implementation signature takes <c>...args: unknown[]</c> and can't
    /// name the overloads' method type parameters, so a group with any
    /// generic overload — or differing return types — falls back to an
    /// erased return type; the overload signatures keep the precise one.
    /// </summary>
    private static TsType ResolveCommonReturnType(
        IReadOnlyList<IrMethodDeclaration> sorted,
        bool isAsync,
        IrToTsTypeOverrides? typeOverrides
    )
    {
        var returns = sorted
            .Select(m => IrToTsTypeMapper.Map(m.ReturnType, typeOverrides))
            .ToList();
        var isGeneric = sorted.Any(IsGeneric);

        if (!isGeneric && returns.All(t => t.Equals(returns[0])))
            return returns[0];

        return isAsync
            ? new TsNamedType("Promise", [new TsNamedType("unknown")])
            : (TsType)new TsAnyType();
    }

    private static bool IsGeneric(IrMethodDeclaration method) =>
        method.TypeParameters is { Count: > 0 };

    private static TsMethodOverload BuildOverloadSignature(
        IrMethodDeclaration method,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new(
            SignatureParameters(method, typeOverrides),
            IrToTsTypeMapper.Map(method.ReturnType, typeOverrides),
            MapTypeParameters(method.TypeParameters)
        );

    private static List<TsParameter> DispatcherParameters() =>
        [new TsParameter("args", new TsNamedType("unknown[]"), Rest: true)];

    private static TsExpression DispatchReceiver(bool isStatic, string containingTypeName) =>
        isStatic ? new TsIdentifier(containingTypeName) : new TsIdentifier("this");

    private static string ClaimName(string candidate, ISet<string> takenNames)
    {
        var name = candidate;

        while (!takenNames.Add(name))
            name += "$";

        return name;
    }

    private static TsMethodMember? BuildFastPathMember(
        IrMethodDeclaration method,
        string fastPathName,
        bool isStatic,
        IrToTsBridgeContext context
    )
    {
        if (method.Body is null)
            return null;

        return new TsMethodMember(
            fastPathName,
            FastPathParameters(method, context),
            IrToTsTypeMapper.Map(method.ReturnType, context.TypeOverrides),
            IrToTsStatementBridge.MapBody(method.Body, context),
            Static: isStatic,
            Async: method.Semantics.IsAsync,
            // Fast paths are private — the dispatcher is the public entry point.
            Accessibility: TsAccessibility.Private,
            // Propagate method type parameters so a generic overload like
            // `Identity<T>(T value)` emits as `identityValue<T>(value: T): T`
            // instead of referencing an undeclared T.
            TypeParameters: MapTypeParameters(method.TypeParameters)
        );
    }

    /// <summary>
    /// A namespace fast path is always emitted — the dispatcher branches to it
    /// by bare name — so a body-less overload lowers to the same
    /// not-implemented stub a single companion function gets.
    /// </summary>
    private static TsFunction BuildFastPathFunction(
        IrMethodDeclaration method,
        string fastPathName,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var context = IrToTsBridgeContext.For(bclRegistry);

        return new(
            fastPathName,
            FastPathParameters(method, context),
            IrToTsTypeMapper.Map(method.ReturnType),
            IrToTsBodyHelpers.LowerOrNotImplemented(method.Body, method.Name, context),
            // Namespace-local: the exported dispatcher is the public entry point.
            Exported: false,
            Async: method.Semantics.IsAsync,
            TypeParameters: MapTypeParameters(method.TypeParameters)
        );
    }

    /// <summary>Maps a method's parameters to TS parameters (local-binding names, rest for <c>params</c>).</summary>
    internal static List<TsParameter> MapParameters(
        IrMethodDeclaration method,
        IrToTsTypeOverrides? typeOverrides = null
    ) =>
        method
            .Parameters.Select(p => new TsParameter(
                IrToTsNamingPolicy.ToParameterName(p.Name),
                IrToTsTypeMapper.MapParameterType(p, typeOverrides),
                Rest: p.IsParams
            ))
            .ToList();

    /// <summary>
    /// The parameters of an overload signature, where TypeScript forbids an
    /// initializer: a parameter a call may omit is optional instead.
    /// </summary>
    private static List<TsParameter> SignatureParameters(
        IrMethodDeclaration method,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        MapParameters(method, typeOverrides)
            .Select(
                (parameter, index) =>
                    parameter with
                    {
                        Optional = method.Parameters[index].CanBeOmitted,
                    }
            )
            .ToList();

    /// <summary>
    /// The parameters of a fast path, carrying the C# defaults the dispatcher
    /// leaves to it when a call omits an argument.
    /// </summary>
    private static List<TsParameter> FastPathParameters(
        IrMethodDeclaration method,
        IrToTsBridgeContext context
    ) =>
        MapParameters(method, context.TypeOverrides)
            .Select(
                (parameter, index) =>
                    parameter with
                    {
                        Optional = method.Parameters[index].CanBeOmitted,
                        DefaultValue = FastPathDefault(method.Parameters[index], context),
                    }
            )
            .ToList();

    /// <summary>
    /// The parameter's C# default as a TS initializer; <c>null</c> without one.
    /// </summary>
    private static TsExpression? FastPathDefault(
        IrParameter parameter,
        IrToTsBridgeContext context
    ) =>
        parameter.DefaultValue is { } defaultValue
            ? IrToTsExpressionBridge.Map(defaultValue, context)
            : null;

    private static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    ) => IrToTsTypeParameterMapper.Convert(typeParameters);

    private static List<TsStatement> BuildDispatcherBody(
        IReadOnlyList<IrMethodDeclaration> sorted,
        IReadOnlyList<string> fastPathNames,
        TsExpression? receiver,
        TypeCheckScope classScope
    )
    {
        var body = new List<TsStatement>();

        for (var i = 0; i < sorted.Count; i++)
        {
            var method = sorted[i];
            var arguments = new OverloadArgumentShape(method.Parameters);
            var scope = TypeCheckScope.ForMethod(
                method,
                classScope.TypeParameters,
                classScope.TypeOverrides
            );

            var delegateCall = new TsCallExpression(
                FastPathCallee(receiver, fastPathNames[i]),
                DelegatedArguments(method, arguments, classScope.TypeOverrides)
            );

            body.Add(
                new TsIfStatement(
                    arguments.Guard(scope),
                    BuildBranchStatements(method, delegateCall)
                )
            );
        }

        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [
                        new TsStringLiteral(
                            // Legacy dispatcher emits the camelCased public name so the
                            // runtime message matches how callers spell the method.
                            $"No matching overload for {TypeScriptNaming.ToCamelCaseMember(sorted[0].Name)}"
                        ),
                    ]
                )
            )
        );
        return body;
    }

    /// <summary>
    /// The branch that delegates to the fast path: <c>call; return;</c> for a
    /// <c>void</c> overload, <c>return call;</c> otherwise.
    /// </summary>
    private static List<TsStatement> BuildBranchStatements(
        IrMethodDeclaration method,
        TsCallExpression delegateCall
    ) =>
        method.ReturnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void }
            ? [new TsExpressionStatement(delegateCall), new TsReturnStatement()]
            : [new TsReturnStatement(delegateCall)];

    /// <summary>
    /// The fast path's arguments: each <c>args[i]</c> cast to its parameter
    /// type, and the trailing arguments of a <c>params</c> parameter spread
    /// back into its rest parameter (<c>...(args.slice(i) as T[])</c>).
    /// </summary>
    private static List<TsExpression> DelegatedArguments(
        IrMethodDeclaration method,
        OverloadArgumentShape arguments,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        method
            .Parameters.Select(
                (parameter, index) =>
                {
                    var value = new TsCastExpression(
                        arguments.ValueOf(index),
                        DispatchArgumentType(method, parameter, typeOverrides)
                    );

                    return parameter.IsParams ? new TsSpreadExpression(value) : (TsExpression)value;
                }
            )
            .ToList();

    /// <summary>
    /// The cast applied to <c>args[i]</c> before delegating. The dispatcher
    /// body can't name a generic overload's method type parameters, so those
    /// arguments are cast to <c>any</c> and the fast path infers its own. A
    /// <c>params</c> parameter takes its rest-parameter type, never
    /// <c>null</c>, since its arguments are spread.
    /// </summary>
    private static TsType DispatchArgumentType(
        IrMethodDeclaration method,
        IrParameter parameter,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        IsGeneric(method)
            ? new TsAnyType()
            : IrToTsTypeMapper.MapParameterType(parameter, typeOverrides);

    /// <summary>
    /// A null receiver means the fast path is a namespace-local function
    /// reachable by bare name.
    /// </summary>
    private static TsExpression FastPathCallee(TsExpression? receiver, string fastPathName) =>
        receiver is null
            ? new TsIdentifier(fastPathName)
            : new TsPropertyAccess(receiver, fastPathName);

    // ── Fast-path naming ─────────────────────────────────────────────────

    /// <summary>
    /// First attempt uses parameter names (<c>addTitle</c> from <c>Add(string title)</c>);
    /// on collision (two overloads sharing parameter names is impossible in C# with
    /// different signatures, but a zero-arg overload + any single named one collide on
    /// the base name), fall back to a type-based suffix.
    /// </summary>
    private static List<string> ComputeFastPathNames(
        string baseName,
        IReadOnlyList<IrMethodDeclaration> overloads
    )
    {
        var byParamName = overloads
            .Select(m => baseName + string.Concat(m.Parameters.Select(p => Capitalize(p.Name))))
            .ToList();
        if (byParamName.Distinct(StringComparer.Ordinal).Count() == byParamName.Count)
            return byParamName;

        return overloads
            .Select(m =>
                baseName
                + string.Concat(m.Parameters.Select(p => Capitalize(SimpleTypeName(p.Type))))
            )
            .ToList();
    }

    private static string SimpleTypeName(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef p => PrimitiveSuffix(p.Primitive),
            IrArrayTypeRef a => SimpleTypeName(a.ElementType) + "Array",
            IrNullableTypeRef n => SimpleTypeName(n.Inner) + "OrNull",
            IrNamedTypeRef n => n.Name,
            _ => "Value",
        };

    /// <summary>
    /// C#-alias spelling of a primitive used in fast-path helper names when
    /// param-name naming isn't usable (Int, Long, ULong, Float, ...).
    /// </summary>
    private static string PrimitiveSuffix(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Int32 => "Int",
            IrPrimitive.Int64 => "Long",
            IrPrimitive.UInt64 => "ULong",
            IrPrimitive.String => "String",
            IrPrimitive.Boolean => "Bool",
            IrPrimitive.Float64 => "Double",
            IrPrimitive.Float32 => "Float",
            _ => primitive.ToString(),
        };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static TsAccessibility VisibilityToAccessibility(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private => TsAccessibility.Private,
            IrVisibility.Protected => TsAccessibility.Protected,
            IrVisibility.Internal => TsAccessibility.Private,
            _ => TsAccessibility.Public,
        };
}
