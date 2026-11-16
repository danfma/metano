using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether an expression may hold <c>null</c>, for the lowerings that guard
/// against it (string concatenation and interpolation, list patterns, the
/// message of an exception, #649). Roslyn's flow state alone cannot decide
/// that: it is optimistic after <c>x!</c>, after a null check of a field or
/// property that a call then clears, and for a local a lambda clears, and it
/// is absent in nullable-disabled code. The guard therefore stays unless the
/// operand cannot be <c>null</c> by construction: a constant, an
/// interpolated string, a concatenation, an object creation, a value of a
/// non-nullable value type, or a symbol declared with a non-nullable
/// reference type in a nullable-enabled context.
/// </summary>
internal static class ReferenceNullability
{
    public static bool MayBeNull(SemanticModel model, ExpressionSyntax expression)
    {
        var info = model.GetTypeInfo(expression);

        if (info.Type is null || info.Type.IsNullableValueType())
            return true;

        if (info.Type is { IsValueType: true } && info.Type is not ITypeParameterSymbol)
            return false;

        if (info.Nullability.FlowState == NullableFlowState.MaybeNull)
            return true;

        return DeclaredAnnotation(model, expression) != NullableAnnotation.NotAnnotated;
    }

    private static NullableAnnotation DeclaredAnnotation(
        SemanticModel model,
        ExpressionSyntax expression
    )
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: not null })
            return NullableAnnotation.NotAnnotated;

        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => DeclaredAnnotation(
                model,
                parenthesized.Expression
            ),
            PostfixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SuppressNullableWarningExpression
            } forgiven => DeclaredAnnotation(model, forgiven.Operand),
            LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                NullableAnnotation.NotAnnotated,
            InterpolatedStringExpressionSyntax => NullableAnnotation.NotAnnotated,
            BaseObjectCreationExpressionSyntax => NullableAnnotation.NotAnnotated,
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } =>
                NullableAnnotation.NotAnnotated,
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce =>
                DeclaredAnnotation(model, coalesce.Right),
            ConditionalExpressionSyntax conditional => Either(
                DeclaredAnnotation(model, conditional.WhenTrue),
                DeclaredAnnotation(model, conditional.WhenFalse)
            ),
            ConditionalAccessExpressionSyntax => NullableAnnotation.Annotated,
            _ => AnnotationOfSymbol(model, expression),
        };
    }

    private static NullableAnnotation Either(NullableAnnotation first, NullableAnnotation second) =>
        first == NullableAnnotation.NotAnnotated ? second : first;

    private static NullableAnnotation AnnotationOfSymbol(
        SemanticModel model,
        ExpressionSyntax expression
    ) =>
        model.GetSymbolInfo(expression).Symbol switch
        {
            ILocalSymbol local => local.Type.NullableAnnotation,
            IParameterSymbol parameter => parameter.Type.NullableAnnotation,
            IFieldSymbol field => field.Type.NullableAnnotation,
            IPropertySymbol property => property.Type.NullableAnnotation,
            IMethodSymbol method => method.ReturnType.NullableAnnotation,
            _ => model.GetTypeInfo(expression).Nullability.Annotation,
        };
}
