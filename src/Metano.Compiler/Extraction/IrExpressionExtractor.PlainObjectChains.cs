using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Constructions of a <c>[PlainObject]</c> type through a constructor that
/// chains with <c>: this(...)</c> (#441). The object literal a backend builds
/// is keyed by the parameters of the constructor that ends the chain, so the
/// chain is resolved here: each hop's arguments are extracted with the
/// previous hop's parameters bound to the values they received, down to the
/// constructor that does not chain.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The construction <paramref name="ctor"/> stands for once its
    /// <c>: this(...)</c> chain is resolved: the constructor that ends the
    /// chain, the arguments it receives in parameter order, and the bindings
    /// that evaluate the caller's arguments once, in source order, ahead of
    /// it. <c>null</c> when <paramref name="ctor"/> does not chain, or a hop
    /// has no source or binds a <c>params</c> array, in which case the
    /// construction keeps its own arguments.
    /// </summary>
    private PlainObjectChain? TryResolvePlainObjectChain(
        IMethodSymbol ctor,
        IReadOnlyList<IrArgument> args
    )
    {
        var bindings = new List<(string Name, IrExpression Value)>();
        var current = ctor;
        var currentArgs = args;
        var resolved = false;

        while (ChainedCallOf(current) is { } chained)
        {
            if (ResolveHop(current, currentArgs, chained, bindings) is not { } hopArgs)
                return null;

            current = chained.TargetMethod;
            currentArgs = hopArgs;
            resolved = true;
        }

        return resolved ? new PlainObjectChain(current, currentArgs, bindings) : null;
    }

    /// <summary>
    /// The arguments <paramref name="chained"/> passes to its target, with
    /// <paramref name="ctor"/>'s parameters bound to <paramref name="args"/>:
    /// substituted directly when every argument is side-effect free,
    /// otherwise each non-literal argument is bound once
    /// (<paramref name="bindings"/>) so the caller's arguments keep their
    /// evaluation order and count.
    /// </summary>
    private IReadOnlyList<IrArgument>? ResolveHop(
        IMethodSymbol ctor,
        IReadOnlyList<IrArgument> args,
        IInvocationOperation chained,
        List<(string Name, IrExpression Value)> bindings
    )
    {
        if (ctor.Parameters.Any(parameter => parameter.IsParams))
            return null;

        var values = ctor
            .Parameters.Select(
                (parameter, index) =>
                    index < args.Count ? args[index].Value : BuildDefaultArgument(parameter)
            )
            .ToList();

        var bindsEach = !values.All(IrEvaluationTraits.IsSideEffectFree);
        var substitutions = new Dictionary<ISymbol, IrExpression>(SymbolEqualityComparer.Default);

        for (var i = 0; i < values.Count; i++)
            substitutions[ctor.Parameters[i]] = BindHopValue(values[i], bindsEach, bindings);

        var model = SymbolHelper.TryGetSemanticModel(
            _semantic.Compilation,
            chained.Syntax.SyntaxTree
        );

        if (model is null)
            return null;

        var hopExtractor = new IrExpressionExtractor(
            model,
            _context,
            LocalBindingDisambiguator.ForMember(ctor, _context),
            _inlineExpanding,
            inlineParameterSubs: substitutions,
            temporaries: _temporaries
        );

        return hopExtractor.ExtractChainedArguments(chained);
    }

    private IrExpression BindHopValue(
        IrExpression value,
        bool bindsEach,
        List<(string Name, IrExpression Value)> bindings
    )
    {
        if (!bindsEach || value is IrLiteral)
            return value;

        var name = NextReceiverTempName();

        bindings.Add((name, value));

        return new IrIdentifier(name);
    }

    /// <summary>
    /// The arguments of a <c>: this(...)</c> call in its target's parameter
    /// order, an omitted optional parameter taking its default value;
    /// <c>null</c> when one binds a <c>params</c> array.
    /// </summary>
    private IReadOnlyList<IrArgument>? ExtractChainedArguments(IInvocationOperation chained)
    {
        var arguments = chained.Arguments.OrderBy(argument => argument.Parameter?.Ordinal).ToList();

        if (arguments.Any(argument => argument.ArgumentKind == ArgumentKind.ParamArray))
            return null;

        return arguments
            .Select(argument => new IrArgument(ChainedArgumentValue(argument)))
            .ToList();
    }

    private IrExpression ChainedArgumentValue(IArgumentOperation argument) =>
        argument.ArgumentKind == ArgumentKind.DefaultValue || argument.Syntax is not ArgumentSyntax
            ? BuildDefaultArgument(argument.Parameter!)
            : Extract(((ArgumentSyntax)argument.Syntax).Expression);

    /// <summary>
    /// The source order a construction keeps: none once a chain is resolved,
    /// since its arguments are no longer the caller's (the chain's bindings
    /// evaluate those in parameter order).
    /// </summary>
    private static IReadOnlyList<int>? ChainedOrNot(
        PlainObjectChain? chain,
        IReadOnlyList<int>? sourceOrder
    ) => chain is null ? sourceOrder : null;

    /// <summary>The <c>: this(...)</c> call <paramref name="ctor"/> chains to, if any.</summary>
    private static IInvocationOperation? ChainedCallOf(IMethodSymbol ctor)
    {
        if (
            ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is not ConstructorDeclarationSyntax
            {
                Initializer.ThisOrBaseKeyword.RawKind: (int)SyntaxKind.ThisKeyword,
            } declaration
        )
            return null;

        return
            ModelOf(ctor, declaration)?.GetOperation(declaration)
                is IConstructorBodyOperation
                {
                    Initializer: IExpressionStatementOperation
                    {
                        Operation: IInvocationOperation call
                    },
                }
            ? call
            : null;
    }

    private static SemanticModel? ModelOf(IMethodSymbol ctor, SyntaxNode declaration) =>
        ctor.ContainingAssembly is ISourceAssemblySymbol source
            ? source.Compilation.GetSemanticModel(declaration.SyntaxTree)
            : null;

    /// <summary>
    /// A resolved <c>: this(...)</c> chain: the constructor that ends it, the
    /// arguments that constructor receives, and the bindings that evaluate
    /// the caller's arguments ahead of the construction.
    /// </summary>
    private sealed record PlainObjectChain(
        IMethodSymbol Target,
        IReadOnlyList<IrArgument> Arguments,
        IReadOnlyList<(string Name, IrExpression Value)> Bindings
    )
    {
        /// <summary><paramref name="construction"/> inside the chain's bindings, outermost first.</summary>
        public IrExpression Bind(IrExpression construction) =>
            Bindings
                .Reverse()
                .Aggregate(construction, (body, binding) =>
                    new IrLetExpression(binding.Name, binding.Value, body)
                );
    }
}
