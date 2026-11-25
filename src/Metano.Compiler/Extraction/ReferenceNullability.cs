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

    /// <summary>
    /// <see cref="MayBeNull(SemanticModel, ExpressionSyntax)"/> for a string
    /// compound assignment's target, where a <c>var</c> local that is never null
    /// by construction needs no guard: its initializer and every simple assignment
    /// to it in its scope (closures included) is a string expression that cannot
    /// yield <c>null</c> (a constant, an interpolation, a concatenation). Roslyn's
    /// flow state proves nothing here: it is the state where a closure is created,
    /// and an element of <c>new string[n]</c> is not-null by type.
    /// </summary>
    public static bool MayBeNullAsTarget(SemanticModel model, ExpressionSyntax target) =>
        MayBeNull(model, target) && !IsNeverNullVarLocal(model, target);

    private static bool IsNeverNullVarLocal(SemanticModel model, ExpressionSyntax target)
    {
        if (
            model.GetSymbolInfo(target).Symbol is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax
                {
                    Initializer: { } initializer,
                    Parent: VariableDeclarationSyntax { Type.IsVar: true } declaration
                }
        )
            return false;

        var scope = declaration.Parent is ForStatementSyntax or UsingStatementSyntax
            ? declaration.Parent
            : declaration.Parent?.Parent;

        return IsNeverNull(model, initializer.Value)
            && scope is not null
            && scope.DescendantNodes().All(node => KeepsNeverNull(model, node, local));
    }

    private static bool KeepsNeverNull(SemanticModel model, SyntaxNode node, ILocalSymbol local) =>
        node switch
        {
            AssignmentExpressionSyntax assignment when IsLocal(model, assignment.Left, local) =>
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)
                    || IsNeverNull(model, assignment.Right),
            ArgumentSyntax { RefKindKeyword.RawKind: not 0 } argument => !IsLocal(
                model,
                argument.Expression,
                local
            ),
            _ => true,
        };

    private static bool IsLocal(
        SemanticModel model,
        ExpressionSyntax expression,
        ILocalSymbol local
    ) => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(expression).Symbol, local);

    private static bool IsNeverNull(SemanticModel model, ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsNeverNull(
                model,
                parenthesized.Expression
            ),
            InterpolatedStringExpressionSyntax => true,
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } => model
                .GetTypeInfo(expression)
                .Type?.SpecialType == SpecialType.System_String,
            _ => model.GetConstantValue(expression) is { HasValue: true, Value: string },
        };

    /// <summary>
    /// Whether a value read from a symbol of <paramref name="type"/> may hold
    /// <c>null</c>: the declared-annotation rule of the expression form, for a
    /// read the compiler synthesizes (a record member in its text, baseline FR-093).
    /// </summary>
    public static bool MayBeNull(ITypeSymbol type)
    {
        if (type.IsNullableValueType())
            return true;

        if (type is { IsValueType: true } && type is not ITypeParameterSymbol)
            return false;

        return type.NullableAnnotation != NullableAnnotation.NotAnnotated;
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
