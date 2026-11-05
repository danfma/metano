using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads the 64-bit values of a LINQ stage from its bound method (spec 012
/// FR-036, FR-041): <c>LongCount</c> counts in <c>long</c>; <c>Sum</c>,
/// <c>Min</c> and <c>Max</c> return the type of the values they aggregate;
/// <c>Average</c> returns a <c>double</c>, so its values are its selector's
/// result or the source's elements.
/// </summary>
internal static class LinqSixtyFourBitValues
{
    public static IrLinqSixtyFourBitValues? Of(IMethodSymbol method, IrLinqOperator op) =>
        TakesAComparer(method)
            ? null
            : op switch
            {
                IrLinqOperator.Count
                or IrLinqOperator.Sum
                or IrLinqOperator.Min
                or IrLinqOperator.Max => SixtyFourBit(method.ReturnType),
                IrLinqOperator.Average => SixtyFourBit(
                    SelectorResult(method) ?? SourceElement(method)
                ),
                _ => null,
            };

    /// <summary>
    /// <c>Min</c> / <c>Max</c> with an <c>IComparer</c> order by it, which
    /// the <c>bigint</c> terminals do not take: such a stage keeps the generic
    /// terminal.
    /// </summary>
    private static bool TakesAComparer(IMethodSymbol method) =>
        method.Parameters.Any(parameter =>
            parameter.Type.OriginalDefinition.ToDisplayString()
                is "System.Collections.Generic.IComparer<T>"
        );

    private static IrLinqSixtyFourBitValues? SixtyFourBit(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { IsEnum: false } endpoint && endpoint.IsSixtyFourBitValue()
            ? new IrLinqSixtyFourBitValues(endpoint.Kind, type.IsNullableValueType())
            : null;

    /// <summary>The result type of the stage's selector (a delegate, or an expression tree over one).</summary>
    private static ITypeSymbol? SelectorResult(IMethodSymbol method) =>
        method
            .Parameters.Select(parameter => DelegateOf(parameter.Type)?.DelegateInvokeMethod)
            .FirstOrDefault(invoke => invoke is not null)
            ?.ReturnType;

    private static INamedTypeSymbol? DelegateOf(ITypeSymbol type) =>
        type switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } function => function,
            INamedTypeSymbol { Name: "Expression", TypeArguments: [var lambda] } => DelegateOf(
                lambda
            ),
            _ => null,
        };

    /// <summary>The element type of the sequence the extension method extends (<c>IEnumerable&lt;long&gt;</c> → <c>long</c>).</summary>
    private static ITypeSymbol? SourceElement(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).Parameters.FirstOrDefault()?.Type
            is INamedTypeSymbol { TypeArguments: [var element] }
            ? element
            : null;
}
