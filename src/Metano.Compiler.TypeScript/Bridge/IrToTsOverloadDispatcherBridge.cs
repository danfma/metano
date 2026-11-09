using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a dispatched method group (<see cref="IrMethodGroup"/>) into the
/// canonical TypeScript dispatcher shape (spec 013 FR-015, FR-030):
/// <list type="number">
///   <item>An addressable fast-path method per overload, holding its body
///   (<see cref="IrToTsStatementBridge"/>), named as the class's claim pass
///   named it — an anchored overload under its root's name — with its own
///   overload's visibility (#445's member mapping), <c>override</c> for an
///   override and <c>abstract</c> for an abstract overload. A fast path that
///   implements further interface roots gets one forwarding member per
///   further name.</item>
///   <item>A dispatcher method taking <c>...args: unknown[]</c> that
///   branches on argument count + runtime type checks and delegates to the
///   matching fast path with cast arguments. It takes the accessibility of the
///   group's most permissive overload; each overload signature carries the
///   JSDoc its group gives it
///   (<see cref="IrToTsVisibilityMapper.SignatureJSDocOf"/>), so
///   <c>stripInternal</c> drops only the signatures no other package can
///   reach: the <c>internal</c> ones and the <c>private</c> ones of a wider
///   group. A group that only overrides an inherited dispatcher's overloads
///   emits none; one that adds overloads to it emits an <c>override</c>
///   dispatcher whose signatures are the inherited ones plus its own and
///   whose unmatched calls go to <c>super</c>.</item>
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
    /// Builds the fast-path members and the dispatcher of a dispatched group;
    /// nothing for a method outside one, which the caller lowers as a single
    /// method.
    /// </summary>
    /// <param name="primary">
    /// The method that carries the group (<see cref="IrMethodGroup"/>).
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
    /// <param name="names">
    /// The fast-path and forwarding names of each overload, as the class's
    /// claim pass named them (<see cref="TsOverloadClaims"/>).
    /// </param>
    /// <param name="owner">
    /// The class that declares the group, whose constructed roots
    /// (<see cref="IrClassDeclaration.ConstructedRoots"/>) take the overloads
    /// anchored on them as branches of a runtime dispatcher
    /// (<see cref="IrToTsConstructedRootDispatcherBridge"/>), not fast paths.
    /// </param>
    internal static IReadOnlyList<TsClassMember> BuildMethod(
        IrMethodDeclaration primary,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims names,
        IrToTsBridgeContext context,
        IrClassDeclaration? owner = null
    )
    {
        var group = IrMethodGroup.Of(primary);

        if (!group.IsDispatched)
            return [];

        var isStatic = primary.IsStatic;
        var members = new List<TsClassMember>();

        foreach (var overload in OverloadDispatchOrder.Sort(group.Members, m => m.Parameters))
        {
            if (owner?.ConstructedRootOf(overload) is not null)
                continue;

            var fastPathName = names.FastPathNameOf(overload);

            members.Add(
                BuildFastPathMember(
                    overload,
                    fastPathName,
                    isStatic,
                    names.OverridesInheritedFastPath(overload),
                    context
                )
            );

            members.AddRange(
                names
                    .ForwardingNamesOf(overload)
                    .Select(forwarderName =>
                        BuildForwardingMember(
                            overload,
                            forwarderName,
                            fastPathName,
                            names.OverridesInheritedMember(overload, forwarderName),
                            context
                        )
                    )
            );
        }

        if (owner is not null)
            members.AddRange(ConstructedRootDispatchers(group, owner, names, context));

        var dispatcher = BuildDispatcher(
            group,
            containingTypeName,
            classTypeParameters,
            names,
            context
        );

        if (dispatcher is not null)
            members.Add(dispatcher);

        return members;
    }

    /// <summary>
    /// One runtime dispatcher per constructed root the group's overloads are
    /// anchored on (FR-036); every implementation of one root shares its name.
    /// </summary>
    private static IEnumerable<TsMethodMember> ConstructedRootDispatchers(
        IrMethodGroup group,
        IrClassDeclaration owner,
        TsOverloadClaims names,
        IrToTsBridgeContext context
    ) =>
        group
            .Members.Select(overload =>
                (Overload: overload, Root: owner.ConstructedRootOf(overload))
            )
            .Where(entry => entry.Root is not null)
            .GroupBy(entry => entry.Root!)
            .Select(implementations =>
                IrToTsConstructedRootDispatcherBridge.Build(
                    names.FastPathNameOf(implementations.First().Overload),
                    implementations.Key,
                    implementations.Select(entry => entry.Overload).ToList(),
                    owner.TypeParameters ?? [],
                    context
                )
            );

    /// <summary>
    /// The group's dispatcher as <see cref="IrMethodGroup.DispatcherKind"/>
    /// decides it; <c>null</c> when it emits none.
    /// </summary>
    private static TsMethodMember? BuildDispatcher(
        IrMethodGroup group,
        string containingTypeName,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims names,
        IrToTsBridgeContext context
    )
    {
        var kind = group.DispatcherKind;

        if (kind is IrDispatcherKind.None)
            return null;

        var typeOverrides = context.TypeOverrides;
        var inherited = group.Primary.InheritedOverloads ?? [];
        var branches = OverloadDispatchOrder.Sort(group.Branches, m => m.Parameters);

        var signatures = OverloadDispatchOrder
            .Sort(inherited, m => m.Parameters)
            .Concat(branches)
            .ToList();

        var plan = PlanOverloadGroup(signatures, typeOverrides, sort: false);
        var overridesInherited = kind is IrDispatcherKind.OverridesInherited;
        var name = TypeScriptNaming.ToCamelCaseMember(group.Primary.Name);

        var body = BuildDispatcherBody(
            branches,
            branches.Select(names.FastPathNameOf).ToList(),
            DispatchReceiver(group.Primary.IsStatic, containingTypeName),
            new TypeCheckScope(classTypeParameters, typeOverrides),
            overridesInherited ? ForwardToBase(name, inherited) : NoMatchingOverload(name)
        );

        return new TsMethodMember(
            name,
            DispatcherParameters(),
            plan.CommonReturn,
            body,
            Static: group.Primary.IsStatic,
            Async: plan.IsAsync,
            // The implementation is untagged: each overload signature
            // carries the JSDoc its group gives it (SignatureJSDocOf).
            Accessibility: plan.Accessibility,
            Overloads: plan.Signatures,
            Override: overridesInherited
        );
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
        if (overloads.Count < 2)
            throw new ArgumentException(
                "An overload group needs two or more methods.",
                nameof(overloads)
            );

        var plan = PlanOverloadGroup(overloads, typeOverrides: null, sort: true);

        var fastPathNames = plan
            .Sorted.Select(overload =>
                ClaimName(OverloadFastPathName.Of(functionName, overload), takenNames)
            )
            .ToList();

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
                    TypeCheckScope.Empty,
                    NoMatchingOverload(TypeScriptNaming.ToCamelCaseMember(plan.Sorted[0].Name))
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
    /// dispatch order, the async flag, the
    /// implementation return type, the overload signatures and the
    /// accessibility they share: the most permissive of the group.
    /// </summary>
    private sealed record OverloadGroupPlan(
        IReadOnlyList<IrMethodDeclaration> Sorted,
        bool IsAsync,
        TsType CommonReturn,
        IReadOnlyList<TsMethodOverload> Signatures,
        TsAccessibility Accessibility
    );

    /// <summary>
    /// Plans the dispatcher over <paramref name="overloads"/>, sorted into
    /// dispatch order unless <paramref name="sort"/> is off (the caller
    /// ordered them already).
    /// </summary>
    private static OverloadGroupPlan PlanOverloadGroup(
        IReadOnlyList<IrMethodDeclaration> overloads,
        IrToTsTypeOverrides? typeOverrides,
        bool sort
    )
    {
        var sorted = sort ? OverloadDispatchOrder.Sort(overloads, m => m.Parameters) : overloads;
        var isAsync = sorted.Any(m => m.Semantics.IsAsync);

        var accessibility = IrToTsVisibilityMapper.MostPermissiveOf(
            sorted.Select(method => IrToTsVisibilityMapper.Map(method.Visibility).Accessibility)
        );

        return new OverloadGroupPlan(
            sorted,
            isAsync,
            ResolveCommonReturnType(sorted, isAsync, typeOverrides),
            sorted
                .Select(method => BuildOverloadSignature(method, accessibility, typeOverrides))
                .ToList(),
            accessibility
        );
    }

    /// <summary>
    /// The implementation signature takes <c>...args: unknown[]</c> and can't
    /// name the overloads' method type parameters, so a group with any
    /// generic overload — or differing return types — falls back to an
    /// erased return type; the overload signatures keep the precise one.
    /// </summary>
    internal static TsType ResolveCommonReturnType(
        IReadOnlyList<IrMethodDeclaration> sorted,
        bool isAsync,
        IrToTsTypeOverrides? typeOverrides
    )
    {
        var returns = sorted
            .Select(m => IrToTsTypeMapper.Map(m.ReturnType, typeOverrides))
            .ToList();

        var isGeneric = sorted.Any(method => IsGeneric(method.TypeParameters));

        return CommonReturnTypeOf(returns, isGeneric, isAsync);
    }

    /// <summary>
    /// The one return type a dispatcher implementation can declare for its
    /// overloads' <paramref name="returns"/>: theirs when they all agree and
    /// none is generic, else the erased one.
    /// </summary>
    internal static TsType CommonReturnTypeOf(
        IReadOnlyList<TsType> returns,
        bool isGeneric,
        bool isAsync
    )
    {
        if (!isGeneric && returns.All(t => t.Equals(returns[0])))
            return returns[0];

        return isAsync
            ? new TsNamedType("Promise", [new TsNamedType("unknown")])
            : (TsType)new TsAnyType();
    }

    /// <summary>
    /// Whether the member declares method type parameters: the dispatcher
    /// body can't name them, so a generic overload erases its return type
    /// and casts its delegated arguments to <c>any</c>.
    /// </summary>
    private static bool IsGeneric(IReadOnlyList<IrTypeParameter>? typeParameters) =>
        typeParameters is { Count: > 0 };

    private static TsMethodOverload BuildOverloadSignature(
        IrMethodDeclaration method,
        TsAccessibility groupAccessibility,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new(
            SignatureParameters(method, typeOverrides),
            IrToTsTypeMapper.Map(method.ReturnType, typeOverrides),
            MapTypeParameters(method.TypeParameters),
            IrToTsVisibilityMapper.SignatureJSDocOf(
                IrToTsVisibilityMapper.Map(method.Visibility),
                groupAccessibility
            )
        );

    internal static List<TsParameter> DispatcherParameters() =>
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

    /// <summary>
    /// One overload's fast path, with its overload's own visibility (FR-015):
    /// an override's is its root's, which the extractor already maps
    /// (<c>IrVisibilityMapper.MapMember</c>). An abstract overload declares an
    /// <c>abstract</c> signature, whose parameters can carry no initializer.
    /// </summary>
    private static TsMethodMember BuildFastPathMember(
        IrMethodDeclaration method,
        string fastPathName,
        bool isStatic,
        bool overridesInheritedFastPath,
        IrToTsBridgeContext context
    )
    {
        var visibility = IrToTsVisibilityMapper.Map(method.Visibility);
        var isAbstract = method.Semantics.IsAbstract;

        return new TsMethodMember(
            fastPathName,
            isAbstract
                ? SignatureParameters(method, context.TypeOverrides)
                : FastPathParameters(method, context),
            IrToTsTypeMapper.Map(method.ReturnType, context.TypeOverrides),
            isAbstract ? [] : IrToTsStatementBridge.MapBody(method.Body ?? [], context),
            Static: isStatic,
            Async: method.Semantics.IsAsync && !isAbstract,
            Accessibility: visibility.Accessibility,
            // Propagate method type parameters so a generic overload like
            // `Identity<T>(T value)` emits as `identityT<T>(value: T): T`
            // instead of referencing an undeclared T.
            TypeParameters: MapTypeParameters(method.TypeParameters),
            Abstract: isAbstract,
            Override: IrMethodGroup.IsOverride(method) || overridesInheritedFastPath,
            JSDoc: visibility.JSDoc
        );
    }

    /// <summary>
    /// A further root's name of an overload that implements several
    /// (naming.md § Root anchoring): it forwards to the fast path, which
    /// holds the body (NFR-003).
    /// </summary>
    private static TsMethodMember BuildForwardingMember(
        IrMethodDeclaration method,
        string forwarderName,
        string fastPathName,
        bool overridesInherited,
        IrToTsBridgeContext context
    )
    {
        var parameters = SignatureParameters(method, context.TypeOverrides);

        var call = new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("this"), fastPathName),
            parameters
                .Select(parameter =>
                    parameter.Rest
                        ? new TsSpreadExpression(new TsIdentifier(parameter.Name))
                        : (TsExpression)new TsIdentifier(parameter.Name)
                )
                .ToList()
        );

        return new TsMethodMember(
            forwarderName,
            parameters,
            IrToTsTypeMapper.Map(method.ReturnType, context.TypeOverrides),
            [new TsReturnStatement(call)],
            TypeParameters: MapTypeParameters(method.TypeParameters),
            Override: overridesInherited
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
    internal static List<TsParameter> SignatureParameters(
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

    internal static IReadOnlyList<TsTypeParameter>? MapTypeParameters(
        IReadOnlyList<IrTypeParameter>? typeParameters
    ) => IrToTsTypeParameterMapper.Convert(typeParameters);

    private static List<TsStatement> BuildDispatcherBody(
        IReadOnlyList<IrMethodDeclaration> sorted,
        IReadOnlyList<string> fastPathNames,
        TsExpression? receiver,
        TypeCheckScope classScope,
        TsStatement unmatched
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
                DelegatedArguments(
                    method.Parameters,
                    method.TypeParameters,
                    arguments,
                    classScope.TypeOverrides
                )
            );

            body.Add(
                new TsIfStatement(
                    arguments.Guard(scope),
                    BuildBranchStatements(method, delegateCall)
                )
            );
        }

        body.Add(unmatched);

        return body;
    }

    /// <summary>
    /// The end of a dispatcher no branch matched: the legacy error, which
    /// names the camelCased public method the way callers spell it.
    /// </summary>
    internal static TsStatement NoMatchingOverload(string methodName) =>
        new TsThrowStatement(
            new TsNewExpression(
                new TsIdentifier("Error"),
                [new TsStringLiteral($"No matching overload for {methodName}")]
            )
        );

    /// <summary>
    /// The end of an overriding dispatcher: every call it does not handle
    /// goes to the inherited one (FR-030),
    /// <c>return super.add(...(args as [never]));</c>. The arguments are cast
    /// to a tuple of one inherited overload's arity, which TypeScript accepts
    /// as a spread into it; an overload with only a rest parameter takes
    /// <c>never[]</c>.
    /// </summary>
    internal static TsStatement ForwardToBase(
        string dispatcherName,
        IReadOnlyList<IrMethodDeclaration> inherited
    ) =>
        new TsReturnStatement(
            new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("super"), dispatcherName),
                [
                    new TsSpreadExpression(
                        new TsCastExpression(
                            new TsIdentifier("args"),
                            ForwardedArgumentsType(inherited)
                        )
                    ),
                ]
            )
        );

    private static TsType ForwardedArgumentsType(IReadOnlyList<IrMethodDeclaration> inherited)
    {
        var fixedArity = inherited.FirstOrDefault(overload =>
            !overload.Parameters.Any(parameter => parameter.IsParams)
        );

        if (fixedArity is null)
            return new TsArrayType(new TsNamedType("never"));

        return new TsTupleType(
            fixedArity.Parameters.Select(_ => (TsType)new TsNamedType("never")).ToList()
        );
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
    /// Shared by the class and the module dispatcher bodies, so a generic
    /// overload's arguments cast to <c>any</c> the same way at either call
    /// site.
    /// </summary>
    internal static List<TsExpression> DelegatedArguments(
        IReadOnlyList<IrParameter> parameters,
        IReadOnlyList<IrTypeParameter>? typeParameters,
        OverloadArgumentShape arguments,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        parameters
            .Select(
                (parameter, index) =>
                {
                    var value = new TsCastExpression(
                        arguments.ValueOf(index),
                        DispatchArgumentType(typeParameters, parameter, typeOverrides)
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
        IReadOnlyList<IrTypeParameter>? typeParameters,
        IrParameter parameter,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        IsGeneric(typeParameters)
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
}
