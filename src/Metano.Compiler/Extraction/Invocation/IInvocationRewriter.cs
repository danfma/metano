using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// One rewrite path that the invocation cascade in
/// <c>IrExpressionExtractor.ExtractInvocation</c> can take.
/// Implementations return non-<see langword="null"/> only when the
/// call site matches their pattern; otherwise they decline and the
/// chain moves on. The chain runs in priority order — see the
/// <c>_invocationRewriters</c> array in the extractor for the active
/// tier table.
/// <para>
/// The cascade is intentionally a chain-of-responsibility rather
/// than a kind-switch because the dispatch keys off symbol-level
/// predicates (delegate-invoke shape, BCL declaring type, attribute
/// presence, extension-lowering hooks), not <see cref="Microsoft.CodeAnalysis.CSharp.SyntaxKind"/>.
/// </para>
/// <para>
/// The method is named <c>Rewrite</c> (not <c>TryRewrite</c>) because
/// the project's <c>Try…</c> convention is reserved for the
/// <c>bool</c> + <c>out</c> shape that never throws — this one
/// returns the rewritten <see cref="IrExpression"/> or
/// <see langword="null"/> instead, matching the
/// match-or-decline contract.
/// </para>
/// </summary>
public interface IInvocationRewriter
{
    IrExpression? Rewrite(InvocationRewriteContext context);
}
