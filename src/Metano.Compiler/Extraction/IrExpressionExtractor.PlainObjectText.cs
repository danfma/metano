using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A <c>[PlainObject]</c> type has no class, so it has no <c>toString()</c> and
/// prints <c>[object Object]</c> where C# prints its text. A value of such a
/// type written at a default-text site reports <c>MS0001</c> as a warning
/// (baseline FR-094.4): loud over silent.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>Reports an interpolation hole or concatenation operand of a <c>[PlainObject]</c> type.</summary>
    private static void ReportPlainObjectText(ExpressionSyntax operand, ITypeSymbol? type)
    {
        if (!IsPlainObject(type))
            return;

        ReportUnsupportedFeature(
            operand,
            $"'{operand}' is a [PlainObject] '{type!.Name}', which has no class and so no "
                + "ToString(): it prints '[object Object]' instead of its C# text."
        );
    }

    /// <summary>
    /// Reports <c>value.ToString()</c> on a <c>[PlainObject]</c> value when the call
    /// binds to a <c>ToString()</c> nothing emits (<c>object</c>'s, or a record's
    /// synthesized one); a declared override is emitted and keeps working.
    /// </summary>
    private void ReportPlainObjectToString(
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

        ReportPlainObjectText(member.Receiver, _semantic.GetTypeInfo(member.Receiver).Type);
    }

    private static bool IsPlainObject(ITypeSymbol? type) =>
        type?.UnwrapNullableValueType() is INamedTypeSymbol named && named.HasPlainObject();
}
