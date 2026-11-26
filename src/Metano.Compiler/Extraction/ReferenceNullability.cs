using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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
        if (NeverNullVarDeclaration(model, target) is not var (local, declaration))
        {
            return false;
        }

        var member = declaration.Ancestors().FirstOrDefault(IsMemberBody);

        return member is not null
            && model.GetOperation(member) is { } body
            && body.DescendantsAndSelf()
                .OfType<ILocalReferenceOperation>()
                .Where(reference => SymbolEqualityComparer.Default.Equals(reference.Local, local))
                .All(reference => !IsWrite(reference) || IsNeverNullWrite(model, reference));
    }

    /// <summary>
    /// The local and declaration of a <c>var</c> local whose initializer is a never-null
    /// string expression; <c>null</c> for any other target.
    /// </summary>
    private static (
        ILocalSymbol Local,
        VariableDeclarationSyntax Declaration
    )? NeverNullVarDeclaration(SemanticModel model, ExpressionSyntax target) =>
        model.GetSymbolInfo(target).Symbol is ILocalSymbol local
        && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax
            {
                Initializer: { } initializer,
                Parent: VariableDeclarationSyntax { Type.IsVar: true } declaration
            }
        && IsNeverNull(model, initializer.Value)
            ? (local, declaration)
            : null;

    private static bool IsMemberBody(SyntaxNode node) =>
        node is MemberDeclarationSyntax or AccessorDeclarationSyntax;

    /// <summary>
    /// Whether the reference is written: the target of any assignment (simple, compound,
    /// coalescing, an element of a deconstruction), an <c>out</c> / <c>ref</c> argument
    /// or an increment. Every other use reads the local.
    /// </summary>
    private static bool IsWrite(ILocalReferenceOperation reference)
    {
        IOperation current = reference;

        while (current.Parent is ITupleOperation tuple)
            current = tuple;

        return current.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == current,
            IArgumentOperation argument => argument.Parameter?.RefKind != RefKind.None,
            IIncrementOrDecrementOperation => true,
            _ => false,
        };
    }

    /// <summary>
    /// A write that keeps the local non-null: a simple <c>=</c> of a never-null string
    /// expression, or a <c>+=</c> that is the built-in concatenation (which never yields
    /// null; a user-defined <c>operator +</c> may). Any other write (a deconstruction,
    /// <c>out</c>, <c>ref</c>, <c>??=</c>, an increment) is not classified and keeps the
    /// guard.
    /// </summary>
    private static bool IsNeverNullWrite(SemanticModel model, ILocalReferenceOperation reference) =>
        reference.Parent switch
        {
            ISimpleAssignmentOperation { Value.Syntax: ExpressionSyntax value } assignment =>
                assignment.Target == reference && IsNeverNull(model, value),
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                OperatorMethod: null
            } compound => compound.Target == reference,
            _ => false,
        };

    private static bool IsNeverNull(SemanticModel model, ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsNeverNull(
                model,
                parenthesized.Expression
            ),
            InterpolatedStringExpressionSyntax => true,
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } =>
                IsBuiltInStringConcatenation(model, expression),
            _ => model.GetConstantValue(expression) is { HasValue: true, Value: string },
        };

    /// <summary>
    /// Whether <paramref name="expression"/> is the built-in string <c>+</c>, which never
    /// yields <c>null</c>. A user-defined <c>operator +</c> (an <c>OperatorMethod</c>) can
    /// return <c>null</c> even when its result type reads as <c>string</c>.
    /// </summary>
    private static bool IsBuiltInStringConcatenation(
        SemanticModel model,
        ExpressionSyntax expression
    ) =>
        model.GetOperation(expression)
            is IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                OperatorMethod: null,
                Type.SpecialType: SpecialType.System_String
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
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } => AdditionAnnotation(
                model,
                expression
            ),
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

    /// <summary>
    /// A <c>+</c>: the built-in string concatenation never yields <c>null</c>; a
    /// user-defined operator answers with its declared return annotation.
    /// </summary>
    private static NullableAnnotation AdditionAnnotation(
        SemanticModel model,
        ExpressionSyntax expression
    ) =>
        IsBuiltInStringConcatenation(model, expression)
            ? NullableAnnotation.NotAnnotated
            : AnnotationOfSymbol(model, expression);

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
