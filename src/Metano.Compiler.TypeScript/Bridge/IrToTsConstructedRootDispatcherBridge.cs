using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The member a class declares under a root fast-path name of an interface it
/// implements more than once (spec 013 DECIDED-30, FR-036):
/// <c>C : IConv&lt;int&gt;, IConv&lt;string&gt;</c> needs <c>convX</c> for both
/// <c>Conv(int x)</c> and <c>Conv(string x)</c>, so <c>convX</c> is a runtime
/// dispatcher over the class's implementations, with one signature per
/// construction. Each branch holds its implementation's body (NFR-003): no
/// per-implementation fast path is emitted. A class whose base already
/// declares the name overrides it and forwards the constructions it does not
/// implement to <c>super</c>.
/// </summary>
internal static class IrToTsConstructedRootDispatcherBridge
{
    /// <param name="name">The root's fast-path name, as its interface declares it.</param>
    /// <param name="root">The root and its constructions in the class.</param>
    /// <param name="implementations">The class's methods anchored on the root.</param>
    /// <param name="classTypeParameters">The class's type parameters, which a
    /// parameter's type check resolves against.</param>
    public static TsMethodMember Build(
        string name,
        IrConstructedRoot root,
        IReadOnlyList<IrMethodDeclaration> implementations,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        IrToTsBridgeContext context
    )
    {
        var typeOverrides = context.TypeOverrides;
        var sorted = OverloadDispatchOrder.Sort(implementations, m => m.Parameters);
        var isAsync = sorted.Any(method => method.Semantics.IsAsync);
        var classScope = new TypeCheckScope(classTypeParameters, typeOverrides);

        var body = sorted
            .Select(method => (TsStatement)BuildBranch(method, classScope, context))
            .Append(
                root.IsInherited
                    ? IrToTsOverloadDispatcherBridge.ForwardToBase(name, root.Constructions)
                    : IrToTsOverloadDispatcherBridge.NoMatchingOverload(name)
            )
            .ToList();

        return new TsMethodMember(
            name,
            IrToTsOverloadDispatcherBridge.DispatcherParameters(),
            IrToTsOverloadDispatcherBridge.ResolveCommonReturnType(
                [.. root.Constructions, .. sorted],
                isAsync,
                typeOverrides
            ),
            body,
            Async: isAsync,
            Overloads: root.Constructions.Select(construction =>
                    SignatureOf(construction, typeOverrides)
                )
                .ToList(),
            Override: root.IsInherited
        );
    }

    private static TsMethodOverload SignatureOf(
        IrMethodDeclaration construction,
        IrToTsTypeOverrides? typeOverrides
    ) =>
        new(
            IrToTsOverloadDispatcherBridge.SignatureParameters(construction, typeOverrides),
            IrToTsTypeMapper.Map(construction.ReturnType, typeOverrides),
            IrToTsOverloadDispatcherBridge.MapTypeParameters(construction.TypeParameters)
        );

    /// <summary>
    /// <c>if (guard) { const x = args[0] as number; …body…; }</c>, ending in
    /// <c>return;</c> for a <c>void</c> implementation, so a later branch
    /// never runs on the same call.
    /// </summary>
    private static TsIfStatement BuildBranch(
        IrMethodDeclaration method,
        TypeCheckScope classScope,
        IrToTsBridgeContext context
    )
    {
        var arguments = new OverloadArgumentShape(method.Parameters);

        var scope = TypeCheckScope.ForMethod(
            method,
            classScope.TypeParameters,
            classScope.TypeOverrides
        );

        var statements = method
            .Parameters.Select(
                (parameter, index) =>
                    (TsStatement)
                        new TsVariableDeclaration(
                            IrToTsNamingPolicy.ToParameterName(parameter.Name),
                            new TsCastExpression(
                                BindArgument(arguments, parameter, index, context),
                                IrToTsTypeMapper.MapParameterType(parameter, context.TypeOverrides)
                            )
                        )
            )
            .Concat(IrToTsStatementBridge.MapBody(method.Body ?? [], context))
            .ToList();

        if (method.ReturnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void })
            statements.Add(new TsReturnStatement());

        return new TsIfStatement(arguments.Guard(scope), statements);
    }

    /// <summary>
    /// The value bound to the parameter at <paramref name="index"/>, or its C#
    /// default when the call omitted it.
    /// </summary>
    private static TsExpression BindArgument(
        OverloadArgumentShape arguments,
        IrParameter parameter,
        int index,
        IrToTsBridgeContext context
    )
    {
        var argument = arguments.ValueOf(index);

        if (parameter.DefaultValue is not { } defaultValue)
            return argument;

        return new TsConditionalExpression(
            OverloadArgumentShape.IsOmitted(argument),
            IrToTsExpressionBridge.Map(defaultValue, context),
            argument
        );
    }
}
