using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Argument readers shared by the entries that rebuild a BCL call against a
/// canonical overload (the <c>Rounding</c>, <c>GetValueOrDefault</c> and
/// <c>StringJoin</c> partials). They read the bound operation, so named
/// arguments land in their declared parameter slot.
/// </summary>
public sealed partial class IntrinsicBclLoweringTable
{
    /// <summary>
    /// The call's argument values in parameter order (the receiver of an
    /// extension call first). Only used for overloads without <c>params</c>
    /// or optional parameters, where every argument value has its own source
    /// expression. Reordering named arguments into parameter order also
    /// reorders their evaluation: <c>Math.Round(mode: M(), value: F())</c>
    /// evaluates <c>F()</c> before <c>M()</c> once lowered, unlike C#.
    /// </summary>
    private static List<IrExpression> ArgumentValues(
        InvocationRewriteContext ctx,
        IInvocationOperation operation
    ) =>
        ArgumentsInParameterOrder(operation)
            .Select(a => ctx.Recurse((ExpressionSyntax)a.Value.Syntax))
            .ToList();

    private static List<IArgumentOperation> ArgumentsInParameterOrder(
        IInvocationOperation operation
    ) => operation.Arguments.OrderBy(a => a.Parameter?.Ordinal).ToList();

    /// <summary>
    /// The bound operation of a call site whose method symbol resolved,
    /// which Roslyn always produces for the BCL methods seeded here.
    /// </summary>
    private static IInvocationOperation BoundInvocation(InvocationRewriteContext ctx) =>
        ctx.SemanticModel.GetOperation(ctx.Invocation) as IInvocationOperation
        ?? throw new InvalidOperationException(
            $"No bound invocation for '{ctx.Invocation}' although its method resolved."
        );

    private static ITypeSymbol? UnconvertedType(IOperation value) =>
        value is IConversionOperation { IsImplicit: true } conversion
            ? conversion.Operand.Type
            : value.Type;
}
