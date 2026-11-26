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
            AssignmentExpressionSyntax assignment when assignment.Right == value => AssignedTypeOf(
                assignment.Left
            ),
            ArgumentSyntax argument => ParameterTypeOf(argument),
            ArrowExpressionClauseSyntax arrow => ReturnTypeOf(arrow.Parent),
            ReturnStatementSyntax statement => ReturnTypeOf(
                statement.Ancestors().FirstOrDefault(IsReturnScope)
            ),
            _ => null,
        };

    /// <summary>
    /// The declared type of an assignment target. The target's type info follows
    /// Roslyn's flow state, which is maybe-null for a non-nullable member inside a
    /// constructor or after a null test, so a symbol's declared type wins; an element
    /// of an array (no symbol) falls back to the type info. A local is typed by the
    /// declaration the target emits for it instead (<see cref="EmittedLocalType"/>).
    /// </summary>
    private ITypeSymbol? AssignedTypeOf(ExpressionSyntax target) =>
        _semantic.GetSymbolInfo(target).Symbol switch
        {
            ILocalSymbol local => EmittedLocalType(local),
            var symbol => TypeOfDeclared(symbol) ?? _semantic.GetTypeInfo(target).Type,
        };

    /// <summary>
    /// The type of a local as its emitted declaration states it. Roslyn declares
    /// every <c>var</c> local of a reference type nullable, but the declaration
    /// carries that only for a simple <c>var</c> local later assigned a value that
    /// may be null (#796); any other <c>var</c> local and every deconstructed or
    /// pattern-declared local is typed by its initializer, which rejects a bare
    /// <c>null</c>. An explicitly typed local keeps its declared type.
    /// </summary>
    private ITypeSymbol EmittedLocalType(ILocalSymbol local)
    {
        var declarator =
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as VariableDeclaratorSyntax;

        if (declarator?.Parent is VariableDeclarationSyntax { Type.IsVar: false })
            return local.Type;

        var declaresNullable =
            declarator?.Parent is VariableDeclarationSyntax declaration
            && MaybeNullLocalAssignments.AnyAfterDeclaration(_semantic, declaration, declarator);

        return local.Type.WithNullableAnnotation(
            declaresNullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated
        );
    }

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
            IParameterSymbol parameter => parameter.Type,
            _ => null,
        };
}
