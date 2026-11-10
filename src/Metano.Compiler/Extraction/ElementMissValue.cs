using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The value a lookup returns when no element qualifies (#601): C# defines it
/// as <c>default(T)</c> of the element type. One policy for the
/// <c>OrDefault</c> LINQ terminals and <c>List&lt;T&gt;.Find</c>.
/// </summary>
internal static class ElementMissValue
{
    /// <summary>
    /// <c>default(T)</c> as IR, or <c>null</c> when no literal spells it: a
    /// struct without a target-agnostic default (a user struct, a tuple, a
    /// <c>struct</c>-constrained type parameter) and a <c>[StringEnum]</c>
    /// that declares no zero member (#621). A numeric enum without a zero
    /// member gets a <c>0</c> asserted to the enum.
    /// </summary>
    public static IrExpression? Of(
        ITypeSymbol elementType,
        Func<ISymbol?, IrMemberOrigin?> buildOrigin,
        Func<ITypeSymbol, IrTypeRef> mapType
    )
    {
        var elementDefault = DefaultValueLiterals.For(elementType, buildOrigin);

        return elementDefault switch
        {
            IrLiteral { Kind: IrLiteralKind.Default } => null,
            IrLiteral { Kind: IrLiteralKind.Int32 } when elementType.TypeKind is TypeKind.Enum =>
                elementType.HasStringEnum()
                    ? null
                    : new IrCastExpression(elementDefault, mapType(elementType), IsAssertion: true),
            _ => elementDefault,
        };
    }

    public static void ReportUnrepresentable(
        SyntaxNode call,
        IMethodSymbol method,
        ITypeSymbol elementType
    ) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'default({elementType.ToDisplayString()})' has no literal in the target: when no "
                    + $"element qualifies, '{method.Name}' returns null instead of the default value.",
                call.GetLocation()
            )
        );
}
