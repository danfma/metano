using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads from a bound LINQ method what a <c>Sum</c>, <c>Average</c>,
/// <c>Min</c>, <c>Max</c>, <c>MinBy</c> or <c>MaxBy</c> stage needs to
/// match .NET (#618, #657): whether it aggregates <c>decimal</c> values, and
/// whether an empty sequence gives <c>null</c> rather than an
/// <c>InvalidOperationException</c>. .NET decides both by the type the method
/// returns, and by the element type for <c>MinBy</c> / <c>MaxBy</c>: a
/// <c>Nullable&lt;T&gt;</c> or a reference type has <c>null</c> to return.
/// </summary>
internal static class LinqAggregateValues
{
    public static IrLinqAggregateValues? Of(IMethodSymbol method, IrLinqOperator op) =>
        op switch
        {
            IrLinqOperator.Sum => new IrLinqAggregateValues(IsDecimal(method.ReturnType), false),
            IrLinqOperator.Average => new IrLinqAggregateValues(
                IsDecimal(method.ReturnType),
                method.ReturnType.IsNullableValueType()
            ),
            IrLinqOperator.Min or IrLinqOperator.Max => new IrLinqAggregateValues(
                IsDecimal(method.ReturnType),
                HasNull(method.ReturnType)
            ),
            IrLinqOperator.MinBy or IrLinqOperator.MaxBy => new IrLinqAggregateValues(
                false,
                method.TypeArguments is [var element, ..] && HasNull(element)
            ),
            _ => null,
        };

    private static bool IsDecimal(ITypeSymbol type) =>
        NumericEndpoints.Of(type) is { IsEnum: false, Kind: IrNumericKind.Decimal };

    /// <summary>
    /// Whether <c>default(<paramref name="type"/>)</c> is <c>null</c>: a
    /// <c>Nullable&lt;T&gt;</c> or a reference type. A type parameter counts
    /// only when it is constrained to reference types.
    /// </summary>
    private static bool HasNull(ITypeSymbol type) =>
        type.IsNullableValueType()
        || type switch
        {
            ITypeParameterSymbol parameter => parameter.HasReferenceTypeConstraint,
            _ => type.IsReferenceType,
        };
}
