using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a conversion operator only returns the wrapped value of its
/// <c>[Branded]</c> operand (<c>implicit operator string(Slug s) =&gt; s.Value</c>,
/// or a block whose one statement returns it), decided from its body (baseline
/// FR-078). Once the wrapper lowers to the branded primitive that conversion is
/// the identity; a conversion out of the wrapper with any other body
/// (<c>=&gt; c.Value * 10</c>), or to another type than the value's, is not.
/// </summary>
internal static class WrappedValueConversion
{
    /// <summary>
    /// Read from the declaration's syntax, so the type's shape extraction
    /// (which runs without a semantic model) answers the same as its full
    /// extraction: the returned expression is <c>operand.Value</c>, where
    /// <c>operand</c> is the conversion's parameter and <c>Value</c> the
    /// wrapper's single value member.
    /// </summary>
    public static bool Returns(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Conversion || !ReturnsWrappedType(method))
            return false;

        var valueMember = SymbolHelper.GetBrandedValueMember(
            method.ContainingType.OriginalDefinition
        );

        return ReturnedExpressionOf(method)
                is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: var receiver },
                    Name.Identifier.ValueText: var member,
                }
            && receiver == method.Parameters[0].Name
            && member == valueMember?.Name;
    }

    /// <summary>
    /// The conversion returns the wrapped value's own type (or its nullable
    /// form), so returning the value converts nothing: <c>long</c> out of a
    /// wrapper over <c>int</c> still widens.
    /// </summary>
    private static bool ReturnsWrappedType(IMethodSymbol method) =>
        SymbolHelper.GetBrandedValueType(method.ContainingType.OriginalDefinition) is { } valueType
        && SymbolEqualityComparer.Default.Equals(
            method.ReturnType.UnwrapNullableValueType(),
            valueType
        );

    private static ExpressionSyntax? ReturnedExpressionOf(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is not ConversionOperatorDeclarationSyntax declaration
            ? null
            : declaration.ExpressionBody?.Expression
                ?? (
                    declaration.Body?.Statements
                        is [ReturnStatementSyntax { Expression: var returned }]
                        ? returned
                        : null
                );
}
