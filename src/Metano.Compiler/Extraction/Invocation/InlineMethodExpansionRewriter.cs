using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Lowers a call to an <c>[Inline]</c> method by β-reducing its
/// body at the call site (textual substitution) or wrapping it as
/// an IIFE so each argument evaluates once. The heavy lifting —
/// cycle detection, sub-extractor construction, body discovery,
/// recursion-name allocation for direct self-references — stays on
/// the extractor's <c>InlineExpansion</c> partial because it shares
/// the mutable <c>_inlineExpanding</c> recursion guard with the
/// member-access inline path (<c>TryExpandInlineAccess</c>), and
/// the sub-extractor factory captures the same extractor state.
/// The guard's acquire/release lives in
/// <c>TryExpandInlineMethod</c>'s <c>try/finally</c> on
/// <c>IrExpressionExtractor.InlineExpansion.cs</c>.
/// <para>
/// This rewriter is a deliberately thin adapter: the chain entry
/// gates on <see cref="SymbolHelper.IsInlineMember"/> and forwards
/// to the extractor-owned <c>TryExpandInlineMethod</c> via the
/// injected delegate. The structural win is moving the cascade
/// branch out of <c>ExtractInvocation</c>, not relocating the
/// machinery — that would split the recursion guard across two
/// owners and force a state-handoff that complicates the cycle
/// invariant <c>InlineExpansion</c> already documents.
/// </para>
/// <para>
/// Slot order: this rewriter sits AFTER
/// <see cref="EmitOrImportRewriter"/>. <c>[Inline]</c> methods that
/// also carry <c>[Emit]</c> or <c>[Import]</c> resolve through the
/// template branch first, matching the legacy cascade order — the
/// attribute system doesn't ban the combination, and the template
/// authoring case wins over β-reduction whenever both apply.
/// </para>
/// </summary>
public sealed class InlineMethodExpansionRewriter(
    Func<InvocationExpressionSyntax, IMethodSymbol, IrExpression?> expandInlineMethod
) : IInvocationRewriter
{
    private readonly Func<
        InvocationExpressionSyntax,
        IMethodSymbol,
        IrExpression?
    > _expandInlineMethod = expandInlineMethod;

    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (context.Symbol is not { } symbol)
            return null;
        if (!SymbolHelper.IsInlineMember(symbol))
            return null;
        return _expandInlineMethod(context.Invocation, symbol);
    }
}
