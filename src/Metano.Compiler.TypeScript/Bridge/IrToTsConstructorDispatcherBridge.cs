using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Collapses an <see cref="IrConstructorDeclaration"/> whose
/// <see cref="IrConstructorDeclaration.Overloads"/> carries every sibling
/// constructor into a single TS constructor shaped like:
/// <code>
/// constructor(a: number);
/// constructor(a: string);
/// constructor(...args: unknown[]) {
///   if (args.length === 1 &amp;&amp; isInt32(args[0])) { super(...); /* body */ return; }
///   if (args.length === 1 &amp;&amp; isString(args[0])) { super(...); /* body */ return; }
///   throw new Error("No matching constructor");
/// }
/// </code>
/// The overload signatures mirror every constructor's parameter list in the
/// order of <see cref="OverloadDispatchOrder"/>, each branch matching the
/// arguments as <see cref="OverloadArgumentShape"/> describes. Each branch
/// opens with the matching <c>super(...)</c> call (when the IR has base
/// arguments), then inlines the constructor body via
/// <see cref="IrToTsStatementBridge"/>.
/// <para>
/// Spec 013 adds an addressable form of each overload (FR-009): a signature
/// <c>constructor(overload: typeof T.byTX, x: number)</c> per overload ahead of
/// the plain ones, and a branch per overload ahead of the fallback ones,
/// guarded by <c>typeof args[0] === "symbol" &amp;&amp; args[0] === T.byTX</c>
/// and binding the parameters from <c>args[1..]</c> with no other check — the
/// branch the overload's factory reaches. A struct also gets its implicit
/// parameterless constructor (FR-027): a <c>constructor()</c> signature and a
/// first branch that gives every instance field its default value.
/// </para>
/// </summary>
public static class IrToTsConstructorDispatcherBridge
{
    private const string SelectorParameter = "overload";

    /// <summary>
    /// Builds the dispatcher constructor. Returns <c>null</c> when the IR
    /// carries a single constructor (no dispatcher needed).
    /// </summary>
    /// <param name="primary">The primary constructor carrying the overloads.</param>
    /// <param name="baseCall">What every branch's base call depends on.</param>
    /// <param name="typeParameters">The class's type parameters, which a
    /// parameter's type check resolves its type parameters against.</param>
    /// <param name="selectors">The claimed selectors the addressable
    /// signatures and branches test.</param>
    /// <param name="implicitParameterlessBody">A struct's implicit
    /// parameterless constructor (<see cref="IrToTsStructDefaultConstructorBridge"/>);
    /// <c>null</c> for any other type.</param>
    /// <param name="signatureJSDoc">The JSDoc of each overload signature: the
    /// tag its group gives the constructor it declares; none when omitted.
    /// The implementation carries none.</param>
    internal static TsConstructor? Build(
        IrConstructorDeclaration primary,
        DispatcherBaseCall baseCall,
        IReadOnlyList<IrTypeParameter> typeParameters,
        ConstructorSelectors selectors,
        IReadOnlyList<TsStatement>? implicitParameterlessBody,
        IrToTsBridgeContext context,
        Func<IrConstructorDeclaration, TsJSDoc?>? signatureJSDoc = null
    )
    {
        if (primary.Overloads is not { Count: > 0 })
            return null;

        var sorted = OverloadDispatchOrder.Sort(
            [primary, .. primary.Overloads],
            ctor => ctor.PlainParameters
        );

        var declared = primary.DeclarationsInOrder;

        IReadOnlyList<TsConstructorOverload> implicitParameterless = implicitParameterlessBody
            is null
            ? []
            : [new TsConstructorOverload([])];

        List<TsConstructorOverload> overloads =
        [
            .. declared.Select(ctor =>
                BuildSelectorSignature(ctor, selectors, context, signatureJSDoc)
            ),
            .. implicitParameterless,
            .. sorted.Select(ctor => new TsConstructorOverload(
                SignatureParameters(ctor, context),
                signatureJSDoc?.Invoke(ctor)
            )),
        ];

        var body = new List<TsStatement>();

        if (implicitParameterlessBody is not null)
            body.Add(BuildImplicitParameterlessBranch(implicitParameterlessBody));

        foreach (var ctor in declared)
            body.Add(BuildSelectorBranch(ctor, baseCall, selectors, context));

        foreach (var ctor in sorted)
            body.Add(BuildBranch(ctor, baseCall, typeParameters, context));

        // Unmatched — match the legacy behavior: the bare TS `throw` so a
        // caller that passes an unsupported signature fails loudly at runtime.
        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [new TsStringLiteral("No matching constructor")]
                )
            )
        );

        return new TsConstructor(
            [
                new TsConstructorParam(
                    "args",
                    new TsNamedType("unknown[]"),
                    Accessibility: TsAccessibility.None,
                    Rest: true
                ),
            ],
            body,
            overloads
        );
    }

    /// <summary>
    /// A signature's parameters. Overload signatures are declaration-only —
    /// they do not introduce parameter properties, so each parameter is plain
    /// (<c>n: number</c>, never the invalid <c>public n: number</c>), and a
    /// default cannot appear there either, so a parameter with one is
    /// optional (<c>n?: number</c>). (#25.2)
    /// </summary>
    private static List<TsConstructorParam> SignatureParameters(
        IrConstructorDeclaration ctor,
        IrToTsBridgeContext context
    ) =>
        ctor
            .Parameters.Select(p => new TsConstructorParam(
                IrToTsNamingPolicy.ToParameterName(p.Parameter.Name),
                IrToTsTypeMapper.MapParameterType(p.Parameter, context.TypeOverrides),
                Accessibility: TsAccessibility.None,
                Rest: p.Parameter.IsParams,
                Optional: p.Parameter.CanBeOmitted
            ))
            .ToList();

    /// <summary>
    /// <c>constructor(overload: typeof T.byTX, x: number)</c>: the overload
    /// addressed by its selector. The selector parameter takes a name none of
    /// the overload's parameters has.
    /// </summary>
    private static TsConstructorOverload BuildSelectorSignature(
        IrConstructorDeclaration ctor,
        ConstructorSelectors selectors,
        IrToTsBridgeContext context,
        Func<IrConstructorDeclaration, TsJSDoc?>? signatureJSDoc
    )
    {
        var parameters = SignatureParameters(ctor, context);

        var selector = new TsConstructorParam(
            SelectorParameterName(parameters),
            selectors.TypeQueryOf(ctor),
            Accessibility: TsAccessibility.None
        );

        return new TsConstructorOverload([selector, .. parameters], signatureJSDoc?.Invoke(ctor));
    }

    private static string SelectorParameterName(IReadOnlyList<TsConstructorParam> parameters) =>
        TsFreeName.FirstFree(
            SelectorParameter,
            name => parameters.Any(parameter => parameter.Name == name)
        );

    /// <summary>
    /// The branch the overload's selector reaches:
    /// <c>if (typeof args[0] === "symbol" &amp;&amp; args[0] === T.byTX)</c>,
    /// binding the parameters from <c>args[1..]</c>. The <c>typeof</c> guard
    /// keeps a selector read before its initialization (<c>undefined</c>)
    /// from matching (DECIDED-19).
    /// </summary>
    private static TsIfStatement BuildSelectorBranch(
        IrConstructorDeclaration ctor,
        DispatcherBaseCall baseCall,
        ConstructorSelectors selectors,
        IrToTsBridgeContext context
    )
    {
        var selectorArgument = OverloadArgumentShape.ArgumentAt(0);

        var guard = new TsBinaryExpression(
            new TsBinaryExpression(
                new TsUnaryExpression("typeof ", selectorArgument),
                "===",
                new TsStringLiteral("symbol")
            ),
            "&&",
            new TsBinaryExpression(selectorArgument, "===", selectors.ReferenceTo(ctor))
        );

        var arguments = OverloadArgumentShape.AfterSelector(ctor.PlainParameters);

        return new TsIfStatement(guard, BuildBranchBody(ctor, arguments, baseCall, context));
    }

    /// <summary>
    /// A struct's implicit parameterless constructor:
    /// <c>if (args.length === 0) { …defaults…; return; }</c>.
    /// </summary>
    private static TsIfStatement BuildImplicitParameterlessBranch(
        IReadOnlyList<TsStatement> parameterlessBody
    ) =>
        new(
            new OverloadArgumentShape([]).Guard(TypeCheckScope.Empty),
            [.. parameterlessBody, new TsReturnStatement()]
        );

    private static TsIfStatement BuildBranch(
        IrConstructorDeclaration ctor,
        DispatcherBaseCall baseCall,
        IReadOnlyList<IrTypeParameter> typeParameters,
        IrToTsBridgeContext context
    )
    {
        var arguments = new OverloadArgumentShape(ctor.PlainParameters);

        return new TsIfStatement(
            arguments.Guard(new TypeCheckScope(typeParameters, context.TypeOverrides)),
            BuildBranchBody(ctor, arguments, baseCall, context)
        );
    }

    /// <summary>
    /// A branch's statements: the parameters bound from <paramref name="arguments"/>,
    /// the constructor's invocation and the <c>return</c> that keeps later
    /// branches from running on the same call.
    /// </summary>
    private static List<TsStatement> BuildBranchBody(
        IrConstructorDeclaration ctor,
        OverloadArgumentShape arguments,
        DispatcherBaseCall baseCall,
        IrToTsBridgeContext context
    )
    {
        var branch = new List<TsStatement>();

        // Bring the original parameter names back into scope as
        // `const <name> = args[i] as <Type>`. The dispatcher signature is
        // `(...args)`, so the lowered super(...) and body — which still
        // reference the source parameter names — would otherwise target
        // undefined identifiers. A single up-front rebind per branch keeps
        // the downstream lowering untouched and matches the legacy output
        // semantically.
        for (var i = 0; i < ctor.Parameters.Count; i++)
        {
            var param = ctor.Parameters[i];

            branch.Add(
                new TsVariableDeclaration(
                    IrToTsNamingPolicy.ToParameterName(param.Parameter.Name),
                    new TsCastExpression(
                        BindArgument(arguments, param, i, context),
                        IrToTsTypeMapper.Map(param.Parameter.Type, context.TypeOverrides)
                    )
                )
            );
        }

        branch.AddRange(LowerConstructorInvocation(ctor, chainDepth: 0, baseCall, context));
        branch.Add(new TsReturnStatement());

        return branch;
    }

    /// <summary>
    /// The value bound to the parameter at <paramref name="index"/>: its
    /// argument (the trailing ones for <c>params</c>), or the C# default when
    /// the call omitted it (<c>args[i] === undefined ? default : args[i]</c>).
    /// </summary>
    private static TsExpression BindArgument(
        OverloadArgumentShape arguments,
        IrConstructorParameter parameter,
        int index,
        IrToTsBridgeContext context
    )
    {
        var argument = arguments.ValueOf(index);

        if (IrToTsClassBridge.ResolveCtorParamDefault(parameter, context) is not { } defaultValue)
            return argument;

        return new TsConditionalExpression(
            OverloadArgumentShape.IsOmitted(argument),
            defaultValue,
            argument
        );
    }

    /// <summary>
    /// The statements that run <paramref name="ctor"/> once its parameters are
    /// in scope: either its <c>: this(...)</c> delegation (see
    /// <see cref="LowerThisChain"/>) or its base call — the matching
    /// <c>super(...)</c> after any argument temporaries — followed by its
    /// body. Every path therefore reaches exactly one <c>super(...)</c> when
    /// the chain ends in a base call.
    /// </summary>
    private static IReadOnlyList<TsStatement> LowerConstructorInvocation(
        IrConstructorDeclaration ctor,
        int chainDepth,
        DispatcherBaseCall baseCall,
        IrToTsBridgeContext context
    )
    {
        var statements = new List<TsStatement>();

        if (ctor.ThisChain is { } chain)
            statements.AddRange(LowerThisChain(chain, chainDepth, baseCall, context));
        else if (SuperCallOf(ctor, baseCall, context) is { } superCall)
            statements.AddRange(superCall.ToStatements());

        statements.AddRange(IrToTsClassBridge.BuildCapturedAssignments(ctor));

        // A record positional or C# 12 primary constructor promotes its
        // parameters; the dispatcher's `(...args)` cannot declare parameter
        // properties, so the branch assigns them before the moved initializers.
        if (ctor.IsPrimary)
            statements.AddRange(IrToTsClassBridge.BuildPromotedAssignments(ctor));

        // The body references the original parameter names, which the caller
        // already brought into scope.
        foreach (var statement in ctor.Body ?? [])
            statements.Add(IrToTsStatementBridge.Map(statement, context));

        return statements;
    }

    /// <summary>
    /// Inlines a <c>: this(...)</c> delegation, since a TS constructor cannot
    /// call a sibling overload. The chained arguments are evaluated once, in
    /// source order, into temporaries; a nested block then binds the target's
    /// parameters to them (named arguments by name, missing ones to their
    /// default) and runs the target — recursively, so multi-level chains end
    /// in the single base call. The block keeps the target's parameter names
    /// from clashing with the caller's.
    /// </summary>
    private static IReadOnlyList<TsStatement> LowerThisChain(
        IrConstructorChain chain,
        int chainDepth,
        DispatcherBaseCall baseCall,
        IrToTsBridgeContext context
    )
    {
        var temporaries = chain
            .Arguments.Select(
                (argument, index) =>
                    new TsVariableDeclaration(
                        $"__chain{chainDepth}Arg{index}",
                        IrToTsExpressionBridge.Map(argument.Value, context)
                    )
            )
            .ToList();

        var targetScope = new List<TsStatement>();
        var target = chain.Target;

        for (var index = 0; index < target.Parameters.Count; index++)
        {
            var parameter = target.Parameters[index].Parameter;

            targetScope.Add(
                new TsVariableDeclaration(
                    IrToTsNamingPolicy.ToParameterName(parameter.Name),
                    BindChainedParameter(chain, temporaries, parameter, index, context)
                )
            );
        }

        targetScope.AddRange(LowerConstructorInvocation(target, chainDepth + 1, baseCall, context));

        return [.. temporaries, new TsBlockStatement(targetScope)];
    }

    private static TsExpression BindChainedParameter(
        IrConstructorChain chain,
        IReadOnlyList<TsVariableDeclaration> temporaries,
        IrParameter parameter,
        int parameterIndex,
        IrToTsBridgeContext context
    )
    {
        var argumentIndex = ChainedArgumentIndex(chain.Arguments, parameter.Name, parameterIndex);

        if (argumentIndex >= 0)
            return new TsIdentifier(temporaries[argumentIndex].Name);

        return parameter.DefaultValue is { } defaultValue
            ? IrToTsExpressionBridge.Map(defaultValue, context)
            : new TsIdentifier("undefined");
    }

    /// <summary>
    /// The index of the chained argument bound to the parameter: a named
    /// argument by name, otherwise the positional argument at
    /// <paramref name="parameterIndex"/>; <c>-1</c> when the parameter is
    /// omitted.
    /// </summary>
    private static int ChainedArgumentIndex(
        IReadOnlyList<IrArgument> arguments,
        string parameterName,
        int parameterIndex
    )
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Name == parameterName)
                return index;
        }

        var isPositional =
            parameterIndex < arguments.Count && arguments[parameterIndex].Name is null;

        return isPositional ? parameterIndex : -1;
    }

    /// <summary>
    /// The <c>super(...)</c> call of <paramref name="ctor"/>; a bare
    /// <c>super()</c> when the class extends a base and the constructor passes
    /// nothing to it, since TypeScript requires the call in a derived class.
    /// </summary>
    private static TsBaseConstructorCall? SuperCallOf(
        IrConstructorDeclaration ctor,
        DispatcherBaseCall baseCall,
        IrToTsBridgeContext context
    ) =>
        IrToTsClassBridge.BuildBaseCall(
            ctor,
            baseCall.BaseClass,
            baseCall.KeepsBaseMessage,
            context
        )
        ?? (
            baseCall.ExtendsBase
                ? new TsBaseConstructorCall(
                    Temporaries: [],
                    Arguments: [],
                    KeepsBaseMessage: baseCall.KeepsBaseMessage
                )
                : null
        );
}

/// <summary>
/// What every branch of one constructor dispatcher needs for its base call:
/// the emitted base class the class extends, if any (so a branch without base
/// arguments still calls <c>super()</c>, and a selected base overload is
/// qualified by it), and whether it keeps its base message (see
/// <see cref="IrToTsClassBridge.BuildBaseCall"/>).
/// </summary>
internal sealed record DispatcherBaseCall(TsType? BaseClass, bool KeepsBaseMessage)
{
    public bool ExtendsBase => BaseClass is not null;
}

/// <summary>
/// The selectors of one class's constructor overloads (spec 013 FR-008,
/// FR-009): the class's emitted name, which qualifies them, and the claim
/// results that name them.
/// </summary>
internal sealed record ConstructorSelectors(string TypeName, TsOverloadClaims Claims)
{
    /// <summary>The selector of <paramref name="ctor"/> as a value: <c>T.byTX</c>.</summary>
    public TsPropertyAccess ReferenceTo(IrConstructorDeclaration ctor) =>
        new(new TsIdentifier(TypeName), SelectorOf(ctor));

    /// <summary>The selector's type: <c>typeof T.byTX</c>.</summary>
    public TsTypeQuery TypeQueryOf(IrConstructorDeclaration ctor) =>
        new($"{TypeName}.{SelectorOf(ctor)}");

    private string SelectorOf(IrConstructorDeclaration ctor) => Claims.NamesOf(ctor).Selector;
}
