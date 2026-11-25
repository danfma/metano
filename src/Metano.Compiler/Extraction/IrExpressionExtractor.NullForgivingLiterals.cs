using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>null!</c> and <c>default!</c> (#794): a target that checks nullability
/// statically rejects the bare <c>null</c> where C# declares a non-nullable
/// reference, so the assertion is kept unless the destination is known to
/// accept <c>null</c>. A destination this cannot place keeps it too: a
/// redundant assertion is noise, a missing one breaks the type check.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrExpression WithLiteralAssertion(
        PostfixUnaryExpressionSyntax suppress,
        IrExpression literal
    )
    {
        var literalType = _semantic.GetTypeInfo(suppress.Operand).ConvertedType
            ?? DestinationTypeOf(suppress);

        return literalType is { IsReferenceType: true } && !DestinationAcceptsNull(suppress)
            ? new IrUnaryExpression(IrUnaryOp.NonNullAssertion, literal, IsPrefix: false)
            : literal;
    }

    private bool DestinationAcceptsNull(PostfixUnaryExpressionSyntax suppress) =>
        DestinationTypeOf(suppress)?.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>
    /// The type the value of <paramref name="value"/> flows into, as C# declared
    /// it: a cast, a variable, field or property initializer, an assignment, an
    /// argument or a return of a method; <c>null</c> for any other position.
    /// </summary>
    private ITypeSymbol? DestinationTypeOf(ExpressionSyntax value) =>
        value.Parent switch
        {
            ParenthesizedExpressionSyntax parenthesized => DestinationTypeOf(parenthesized),
            CastExpressionSyntax cast => _semantic.GetTypeInfo(cast.Type).Type,
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } => TypeOfDeclared(
                _semantic.GetDeclaredSymbol(variable)
            ),
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } =>
                TypeOfDeclared(_semantic.GetDeclaredSymbol(property)),
            AssignmentExpressionSyntax assignment when assignment.Right == value => _semantic
                .GetTypeInfo(assignment.Left)
                .Type,
            ArgumentSyntax argument => ParameterTypeOf(argument),
            ArrowExpressionClauseSyntax arrow => ReturnTypeOf(arrow.Parent),
            ReturnStatementSyntax statement => ReturnTypeOf(
                statement.Ancestors().FirstOrDefault(IsReturnScope)
            ),
            _ => null,
        };

    private static bool IsReturnScope(SyntaxNode node) =>
        node
            is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax
                or ConstructorDeclarationSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax;

    private ITypeSymbol? ParameterTypeOf(ArgumentSyntax argument) =>
        _semantic.GetOperation(argument)
            is Microsoft.CodeAnalysis.Operations.IArgumentOperation { Parameter: { } parameter }
            ? parameter.Type
            : null;

    private ITypeSymbol? ReturnTypeOf(SyntaxNode? scope) =>
        scope switch
        {
            MethodDeclarationSyntax or LocalFunctionStatementSyntax => (
                _semantic.GetDeclaredSymbol(scope) as IMethodSymbol
            )?.ReturnType,
            PropertyDeclarationSyntax or IndexerDeclarationSyntax => TypeOfDeclared(
                _semantic.GetDeclaredSymbol(scope)
            ),
            AccessorDeclarationSyntax { Parent.Parent: { } owner } => TypeOfDeclared(
                _semantic.GetDeclaredSymbol(owner)
            ),
            _ => null,
        };

    private static ITypeSymbol? TypeOfDeclared(ISymbol? symbol) =>
        symbol switch
        {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
}
