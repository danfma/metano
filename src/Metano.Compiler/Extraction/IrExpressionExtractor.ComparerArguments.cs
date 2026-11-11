using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The comparer argument of a BCL call: a LINQ stage (#648) or a
/// <c>System.MemoryExtensions</c> search (#657). Both families name their
/// <c>IEqualityComparer&lt;T&gt;</c> / <c>IComparer&lt;T&gt;</c> parameter
/// <see cref="ComparerParameter.Name"/>, and a comparer that is the
/// <c>null</c> constant asks for the default comparison.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The argument <paramref name="call"/> passes as its comparer, or
    /// <c>null</c> when it passes none.
    /// </summary>
    private ExpressionSyntax? ComparerArgumentOf(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    ) =>
        call
            .ArgumentList.Arguments.FirstOrDefault(argument =>
                BoundParameterOf(argument, method).Name == ComparerParameter.Name
            )
            ?.Expression;

    /// <summary>
    /// Whether <paramref name="expression"/> is the <c>null</c> constant:
    /// <c>null</c> or <c>default</c>, in parentheses or cast to the comparer
    /// type, or a <c>const</c> holding <c>null</c>.
    /// </summary>
    private bool IsNullConstant(ExpressionSyntax expression) =>
        _semantic.GetConstantValue(expression) is { HasValue: true, Value: null };

    private static bool TakesComparer(IMethodSymbol method) =>
        method.Parameters.Any(parameter => parameter.Name == ComparerParameter.Name);
}
