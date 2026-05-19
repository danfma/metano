using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// `[Inline]` method expansion machinery: textual-substitution and
/// IIFE-wrapping strategies, plus the lambda-materialisation +
/// body-discovery helpers they share. Field/property inline access
/// (<see cref="TryExpandInlineAccess"/>) stays on the main partial
/// since it sits inside the identifier extraction path.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Expands a call to an <c>[Inline]</c> method into the method's
    /// body with each parameter substituted by the caller's argument
    /// IR. Returns <c>null</c> when the method shape is not supported
    /// (multi-statement body, no declaring syntax, cycle).
    /// </summary>
    private IrExpression? TryExpandInlineMethod(
        InvocationExpressionSyntax inv,
        IMethodSymbol symbol
    )
    {
        // For a reduced extension call (`receiver.Method(args)`) Roslyn
        // hides the static declaration behind `ReducedFrom`; its first
        // parameter is the receiver slot the body references. Use that
        // form so parameter-symbol identity lines up with the body's
        // identifiers.
        var declaringMethod = symbol.ReducedFrom ?? symbol.OriginalDefinition;
        var cycleKey = declaringMethod.OriginalDefinition;
        if (!_inlineExpanding.Add(cycleKey))
            return null;
        try
        {
            var bodyExpr = TryFindInlineMethodBody(declaringMethod);
            if (bodyExpr is null)
                return null;

            var semanticModel = SymbolHelper.TryGetSemanticModel(
                _semantic.Compilation,
                bodyExpr.SyntaxTree
            );
            if (semanticModel is null)
                return null;

            // [Inline(InlineMode.Substitute)] opts into textual β-reduction:
            // caller arguments substitute directly into the body. The default
            // [Inline] (Materialize) wraps the body as an IIFE so every
            // argument evaluates exactly once.
            if (SymbolHelper.GetInlineMode(declaringMethod) == InlineMode.Substitute)
                return ExpandInlineAsTextualSubstitution(
                    inv,
                    symbol,
                    declaringMethod,
                    bodyExpr,
                    semanticModel
                );

            return ExpandInlineAsIife(inv, symbol, declaringMethod, bodyExpr, semanticModel);
        }
        finally
        {
            _inlineExpanding.Remove(cycleKey);
        }
    }

    private IrExpression? ExpandInlineAsTextualSubstitution(
        InvocationExpressionSyntax inv,
        IMethodSymbol symbol,
        IMethodSymbol declaringMethod,
        ExpressionSyntax bodyExpr,
        SemanticModel semanticModel
    )
    {
        var subs = new Dictionary<ISymbol, IrExpression>(SymbolEqualityComparer.Default);
        var parameters = declaringMethod.Parameters;
        var argIndex = 0;

        if (
            symbol.ReducedFrom is not null
            && inv.Expression is MemberAccessExpressionSyntax memberAccess
        )
        {
            if (parameters.Length == 0)
                return null;
            subs[parameters[0]] = Extract(memberAccess.Expression);
            argIndex = 1;
        }

        var positionalArgs = new List<ArgumentSyntax>();
        var namedArgs = new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal);
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (arg.NameColon is { } nc)
                namedArgs[nc.Name.Identifier.ValueText] = arg;
            else
                positionalArgs.Add(arg);
        }

        for (var i = argIndex; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var syntaxIndex = i - argIndex;
            ArgumentSyntax? matched =
                syntaxIndex < positionalArgs.Count
                    ? positionalArgs[syntaxIndex]
                    : namedArgs.GetValueOrDefault(parameter.Name);

            if (matched is not null)
            {
                subs[parameter] = Extract(matched.Expression);
                continue;
            }

            if (parameter.HasExplicitDefaultValue)
            {
                subs[parameter] = BuildLiteralForDefault(
                    parameter.ExplicitDefaultValue,
                    parameter.Type
                );
                continue;
            }

            return null;
        }

        var extractor = new IrExpressionExtractor(
            semanticModel,
            _originResolver,
            _target,
            _inlineExpanding,
            inlineParameterSubs: subs
        );
        return extractor.Extract(bodyExpr);
    }

    private IrExpression? ExpandInlineAsIife(
        InvocationExpressionSyntax inv,
        IMethodSymbol symbol,
        IMethodSymbol declaringMethod,
        ExpressionSyntax bodyExpr,
        SemanticModel semanticModel
    )
    {
        var lambda = BuildInlineLambda(declaringMethod, bodyExpr, semanticModel);
        if (lambda is null)
            return null;

        var args = new List<IrArgument>();
        var argIndex = 0;

        if (
            symbol.ReducedFrom is not null
            && inv.Expression is MemberAccessExpressionSyntax memberAccess
        )
        {
            args.Add(new IrArgument(Extract(memberAccess.Expression)));
            argIndex = 1;
        }

        var parameters = declaringMethod.Parameters;
        var positionalArgs = new List<ArgumentSyntax>();
        var namedArgs = new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal);
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (arg.NameColon is { } nc)
                namedArgs[nc.Name.Identifier.ValueText] = arg;
            else
                positionalArgs.Add(arg);
        }

        for (var i = argIndex; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var syntaxIndex = i - argIndex;
            ArgumentSyntax? matched =
                syntaxIndex < positionalArgs.Count
                    ? positionalArgs[syntaxIndex]
                    : namedArgs.GetValueOrDefault(parameter.Name);

            if (matched is not null)
            {
                args.Add(new IrArgument(Extract(matched.Expression)));
                continue;
            }

            if (parameter.HasExplicitDefaultValue)
            {
                args.Add(
                    new IrArgument(
                        BuildLiteralForDefault(parameter.ExplicitDefaultValue, parameter.Type)
                    )
                );
                continue;
            }

            return null;
        }

        return new IrCallExpression(lambda, args);
    }

    private IrExpression? TryMaterializeInlineMethodAsLambda(IMethodSymbol method)
    {
        var declaringMethod = method.ReducedFrom ?? method.OriginalDefinition;
        var bodyExpr = TryFindInlineMethodBody(declaringMethod);
        if (bodyExpr is null)
            return null;
        var semanticModel = SymbolHelper.TryGetSemanticModel(
            _semantic.Compilation,
            bodyExpr.SyntaxTree
        );
        if (semanticModel is null)
            return null;
        return BuildInlineLambda(declaringMethod, bodyExpr, semanticModel);
    }

    private IrLambdaExpression? BuildInlineLambda(
        IMethodSymbol declaringMethod,
        ExpressionSyntax bodyExpr,
        SemanticModel semanticModel
    )
    {
        // Direct recursion: the body references its own declaring symbol.
        // The erased declaration leaves no callable name in TS, so the
        // lambda must lower as a JavaScript named function expression
        // and rewrite self-references inside the body to the internal
        // name. Indirect recursion stays out of scope — `_inlineExpanding`
        // already breaks A→B→A cycles at the inner expansion site. (#194)
        var selfName = DetectsDirectInlineRecursion(declaringMethod, bodyExpr, semanticModel)
            ? $"inline${SymbolHelper.ToCamelCase(declaringMethod.Name)}"
            : null;

        IReadOnlyDictionary<ISymbol, IrExpression>? bodySubs = null;
        if (selfName is not null)
        {
            var subs = new Dictionary<ISymbol, IrExpression>(SymbolEqualityComparer.Default)
            {
                [declaringMethod.OriginalDefinition] = new IrIdentifier(selfName),
            };
            bodySubs = subs;
        }

        var bodyExtractor = new IrExpressionExtractor(
            semanticModel,
            _originResolver,
            _target,
            _inlineExpanding,
            inlineParameterSubs: bodySubs
        );
        var bodyIr = bodyExtractor.Extract(bodyExpr);

        var parameters = declaringMethod
            .Parameters.Select(p => new IrParameter(
                p.Name,
                IrTypeRefMapper.Map(p.Type, _originResolver, _target),
                HasDefaultValue: p.HasExplicitDefaultValue,
                DefaultValue: p.HasExplicitDefaultValue
                    ? BuildLiteralForDefault(p.ExplicitDefaultValue, p.Type)
                    : null,
                IsParams: p.IsParams
            ))
            .ToList();

        var returnType = declaringMethod.ReturnsVoid
            ? null
            : IrTypeRefMapper.Map(declaringMethod.ReturnType, _originResolver, _target);

        return new IrLambdaExpression(
            parameters,
            returnType,
            [new IrReturnStatement(bodyIr)],
            Name: selfName
        );
    }

    private static bool DetectsDirectInlineRecursion(
        IMethodSymbol declaringMethod,
        ExpressionSyntax bodyExpr,
        SemanticModel semanticModel
    )
    {
        var target = declaringMethod.OriginalDefinition;
        foreach (var node in bodyExpr.DescendantNodesAndSelf())
        {
            ISymbol? symbol = node switch
            {
                IdentifierNameSyntax id => semanticModel.GetSymbolInfo(id).Symbol,
                MemberAccessExpressionSyntax ma => semanticModel.GetSymbolInfo(ma).Symbol,
                _ => null,
            };
            if (
                symbol is IMethodSymbol method
                && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, target)
            )
                return true;
        }
        return false;
    }

    private static ExpressionSyntax? TryFindInlineMethodBody(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not MethodDeclarationSyntax decl)
                continue;
            if (decl.ExpressionBody?.Expression is { } arrow)
                return arrow;
            if (
                decl.Body is { Statements: { Count: 1 } statements }
                && statements[0] is ReturnStatementSyntax { Expression: { } returnExpr }
            )
                return returnExpr;
        }
        return null;
    }
}
