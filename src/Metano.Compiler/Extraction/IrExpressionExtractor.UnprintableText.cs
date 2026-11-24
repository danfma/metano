using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A value written at a default-text site whose type the target cannot print
/// (<see cref="IrExtractionContext.UnprintableTextReason"/>: TypeScript's
/// <c>[PlainObject]</c>, baseline FR-094.4) reports <c>MS0001</c> as a warning
/// with the target's reason: loud over silent.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>Reports an interpolation hole or concatenation operand the target cannot print.</summary>
    private void ReportUnprintableText(ExpressionSyntax operand, ITypeSymbol? type)
    {
        if (
            _context.UnprintableTextReason is not { } reasonOf
            || type?.UnwrapNullableValueType() is not INamedTypeSymbol named
            || reasonOf(named) is not { } reason
        )
            return;

        ReportUnsupportedFeature(operand, $"'{operand}': {reason}");
    }

    /// <summary>
    /// Reports <c>value.ToString()</c> on a value the target cannot print when the
    /// call binds to a <c>ToString()</c> nothing emits (<c>object</c>'s, or a
    /// record's synthesized one); a declared override is emitted and keeps working.
    /// </summary>
    private void ReportUnprintableToString(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method
    )
    {
        if (
            method is not { Name: nameof(ToString), IsStatic: false, Parameters.Length: 0 }
            || !(method.ContainingAssembly.IsBclAssembly() || method.IsImplicitlyDeclared)
            || QualifiedMemberSyntax.From(invocation.Expression) is not { } member
        )
            return;

        ReportUnprintableText(member.Receiver, _semantic.GetTypeInfo(member.Receiver).Type);
    }
}
