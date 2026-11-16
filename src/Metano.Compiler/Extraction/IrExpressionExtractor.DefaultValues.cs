using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A target-typed <c>default</c> literal and <c>default(T)</c> (#627, #372,
/// #612). Neither carries a surface type: the value depends on the type the
/// compiler converts it to, so every position (a local initializer, a
/// conditional arm, a <c>??</c> operand, an argument, a collection element)
/// resolves it here, through the same policy as the compiler-synthesized
/// defaults of <see cref="DefaultValueLiterals"/>.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>default</c> of <paramref name="type"/> as IR: the literal or member
    /// <see cref="ElementMissValue.Of"/> picks for it. A type with no literal
    /// (a user struct, a tuple, a <c>[StringEnum]</c> without a zero member)
    /// reports MS0001 and keeps the opaque <see cref="IrLiteralKind.Default"/>
    /// marker, which a target prints as its own placeholder.
    /// </summary>
    private IrExpression DefaultValueOf(ExpressionSyntax syntax, ITypeSymbol? type)
    {
        if (type is null)
            return new IrLiteral(null, IrLiteralKind.Default);

        if (type.IsReferenceType || type is ITypeParameterSymbol || IsNullableType(type))
            return new IrLiteral(null, IrLiteralKind.Null);

        var value = ElementMissValue.Of(type, BuildOrigin, MapElementType);

        if (value is not null)
            return value;

        ReportUnrepresentableDefaultLiteral(syntax, type);

        return new IrLiteral(null, IrLiteralKind.Default);
    }

    private static bool IsNullableType(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated || type.IsNullableValueType();

    private static void ReportUnrepresentableDefaultLiteral(SyntaxNode syntax, ITypeSymbol type) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'default({type.ToDisplayString()})' has no literal in the target: it is left as an "
                    + "unresolved placeholder.",
                syntax.GetLocation()
            )
        );
}
