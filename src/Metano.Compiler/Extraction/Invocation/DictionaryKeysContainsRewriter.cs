using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// <c>dictionary.Keys.Contains(key)</c> → <c>dictionary.ContainsKey(key)</c>:
/// the same lookup, answered by the dictionary without building or scanning
/// its keys, so the backends apply the <c>ContainsKey</c> mapping. Covers the
/// instance <c>Contains</c> of <c>Dictionary&lt;K,V&gt;.KeyCollection</c> and
/// of the <c>ICollection&lt;K&gt;</c> that <c>IDictionary&lt;K,V&gt;.Keys</c>
/// returns. <c>IReadOnlyDictionary&lt;K,V&gt;.Keys</c> is an
/// <c>IEnumerable&lt;K&gt;</c> whose <c>Contains</c> is the LINQ extension,
/// which the LINQ chain pre-pass lowers before any rewriter runs.
/// <para>
/// No other rewriter claims these <c>Contains</c> calls; what matters is that
/// the chain runs before the generic call lowering at the end of
/// <c>ExtractInvocation</c>, which keeps the <c>KeyCollection.Contains</c>
/// origin (mapped to <c>includes</c> on the array copy, which scans every
/// key). Pinned by <c>InvocationRewriterCanaryTests</c>.
/// </para>
/// </summary>
public sealed class DictionaryKeysContainsRewriter(InvocationLoweringHelpers helpers)
    : IInvocationRewriter
{
    private readonly InvocationLoweringHelpers _helpers = helpers;

    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (!IsInstanceContains(context.Symbol))
            return null;

        if (context.Invocation.Expression is not MemberAccessExpressionSyntax containsAccess)
            return null;

        if (
            DictionaryViewRead.Match(containsAccess.Expression, context.SemanticModel)
            is not { IsKeys: true } keysView
        )
            return null;

        if (FindContainsKey(keysView.DictionaryType) is not { } containsKey)
            return null;

        var key = context.ExtractArgument(context.Invocation.ArgumentList.Arguments[0]);

        return new IrCallExpression(
            new IrMemberAccess(context.Recurse(keysView.Dictionary), containsKey.Name),
            [key],
            Origin: _helpers.BuildOrigin(containsKey)
        );
    }

    private static IMethodSymbol? FindContainsKey(INamedTypeSymbol dictionaryType) =>
        dictionaryType
            .GetMembers("ContainsKey")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 1);

    private static bool IsInstanceContains(IMethodSymbol? method) =>
        method is { Name: "Contains", IsStatic: false, ReducedFrom: null, Parameters.Length: 1 };
}
