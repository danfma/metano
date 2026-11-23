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
    /// <c>default</c> of <paramref name="type"/> as IR: the value
    /// <see cref="DefaultValuePolicy.Of"/> picks for it. A type no expression
    /// spells (a struct that declares a constructor, a tuple, a
    /// <c>[StringEnum]</c> without a zero member) reports MS0001 and keeps the opaque <see cref="IrLiteralKind.Default"/>
    /// marker, which a target prints as its own placeholder.
    /// </summary>
    private IrExpression DefaultValueOf(ExpressionSyntax syntax, ITypeSymbol? type)
    {
        if (type is null)
            return new IrLiteral(null, IrLiteralKind.Default);

        if (type is ITypeParameterSymbol tp)
            return new IrReifiedDefaultExpression(MapTypeParameterWithScope(tp, syntax));

        if (type.IsReferenceType || IsNullableType(type))
            return new IrLiteral(null, IrLiteralKind.Null);

        var value = DefaultOf(type);

        if (value is IrNewExpression structDefaultNew)
        {
            ReportUnsetStructFields(syntax, type);

            if (type is INamedTypeSymbol namedStruct)
            {
                var reifiedArgs = _context.ReifiedTypeAnalysis.ReifiedTypeArgumentsForNew(
                    namedStruct,
                    t => MapTypeWithReifiedScope(t, syntax)
                );

                if (reifiedArgs is { Count: > 0 })
                {
                    return structDefaultNew with
                    {
                        ReifiedTypeArguments = reifiedArgs,
                        IsStructDefault = true,
                    };
                }
            }

            return structDefaultNew;
        }

        if (value is not null)
            return value;

        if (
            type is INamedTypeSymbol { IsValueType: true } genericStruct
            && _context.ReifiedTypeAnalysis.TypeArgumentSlotOf(genericStruct) is not null
        )
        {
            var reifiedArgs = _context.ReifiedTypeAnalysis.ReifiedTypeArgumentsForNew(
                genericStruct,
                t => MapTypeWithReifiedScope(t, syntax)
            );

            if (reifiedArgs is { Count: > 0 })
            {
                return new IrNewExpression(
                    IrTypeRefMapper.Map(genericStruct, _originResolver, _target),
                    []
                )
                {
                    ReifiedTypeArguments = reifiedArgs,
                    IsStructDefault = true,
                };
            }
        }

        ReportUnrepresentableDefaultLiteral(syntax, type);

        return new IrLiteral(null, IrLiteralKind.Default);
    }

    /// <summary>
    /// <c>default(T)</c> of <paramref name="type"/> as IR through the core's
    /// policy (<see cref="DefaultValuePolicy"/>), or <c>null</c> when no
    /// expression spells it.
    /// </summary>
    internal IrExpression? DefaultOf(ITypeSymbol type) =>
        DefaultValuePolicy.Of(type, BuildOrigin, MapElementType);

    /// <summary>
    /// <see cref="DefaultOf"/>, or the opaque <see cref="IrLiteralKind.Default"/>
    /// marker a target prints as its placeholder when no expression spells it.
    /// </summary>
    private IrExpression DefaultOrMarker(ITypeSymbol type) =>
        DefaultOf(type) ?? new IrLiteral(null, IrLiteralKind.Default);

    private static bool IsNullableType(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated || type.IsNullableValueType();

    /// <summary>
    /// Reports MS0001 when a field of the struct has no target default
    /// (<see cref="DefaultValuePolicy.UnsetFieldsOf"/>), so <c>default(S)</c> does
    /// not give it its C# value.
    /// </summary>
    private void ReportUnsetStructFields(SyntaxNode syntax, ITypeSymbol type)
    {
        var unset = DefaultValuePolicy.UnsetFieldsOf(type, MapElementType);

        if (unset.Count == 0)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'default({type.ToDisplayString()})' has no target default for "
                    + $"{(unset.Count == 1 ? "field" : "fields")} "
                    + $"{string.Join(", ", unset.Select(name => $"'{name}'"))}.",
                syntax.GetLocation()
            )
        );
    }

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
