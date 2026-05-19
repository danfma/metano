using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Bundle every <see cref="IInvocationRewriter"/> needs to inspect a
/// call site without dragging the whole <see cref="IrExpressionExtractor"/>
/// surface into rewriter scope. The context stays narrow on purpose —
/// rewriters that need shape helpers (params spread, argument
/// normalisation, origin building) take an
/// <see cref="InvocationLoweringHelpers"/> constructor-injected
/// instead so the context cannot drift into a god-interface.
/// </summary>
/// <param name="Invocation">The Roslyn call-site syntax.</param>
/// <param name="Symbol">
/// The method symbol the semantic model resolved for the call site
/// (already cast to <see cref="IMethodSymbol"/> where applicable).
/// <see langword="null"/> means the symbol could not be resolved —
/// most rewriters bail in that case.
/// </param>
/// <param name="SemanticModel">The active semantic model.</param>
/// <param name="Recurse">
/// Bound to the extractor's recursive <c>Extract</c> entry point so a
/// rewriter can lower its sub-expressions without holding a reference
/// to the extractor itself.
/// </param>
/// <param name="ExtractArgument">
/// Bound to the extractor's <c>ExtractArgument</c> helper. Used by
/// rewriters that preserve the full <see cref="IrArgument"/> shape
/// (named / ref / default).
/// </param>
public readonly record struct InvocationRewriteContext(
    InvocationExpressionSyntax Invocation,
    IMethodSymbol? Symbol,
    SemanticModel SemanticModel,
    Func<ExpressionSyntax, IrExpression> Recurse,
    Func<ArgumentSyntax, IrArgument> ExtractArgument
);
