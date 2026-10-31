using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// The code a member's declaration contributes to the output, by declaration shape:
/// bodies, initializers, parameter defaults and constructor-initializer arguments —
/// never the signature syntax, which is described by the member's symbol. Each
/// selector maps one declaring syntax node to the code regions it owns; regions
/// a declaration does not have come back as <c>null</c>.
/// </summary>
internal static class MemberContributedCode
{
    /// <summary>
    /// The initializer of a field, event field (declared by a
    /// <see cref="VariableDeclaratorSyntax"/>) or property.
    /// </summary>
    public static IEnumerable<SyntaxNode?> Initializers(SyntaxNode declaration) =>
        declaration switch
        {
            VariableDeclaratorSyntax variable => [variable.Initializer],
            PropertyDeclarationSyntax property => [property.Initializer],
            _ => [],
        };

    /// <summary>
    /// The executable code of a method or accessor. A record or class primary
    /// constructor is declared by the whole type declaration, so only its parameter
    /// defaults and base-constructor arguments belong to it — the type's members and
    /// nested types are separate declarations.
    /// </summary>
    public static IEnumerable<SyntaxNode?> Executable(SyntaxNode declaration) =>
        declaration switch
        {
            ConstructorDeclarationSyntax constructor =>
            [
                .. DefaultValuesOf(constructor.ParameterList),
                constructor.Initializer,
                constructor.Body,
                constructor.ExpressionBody,
            ],
            BaseMethodDeclarationSyntax method =>
            [
                .. DefaultValuesOf(method.ParameterList),
                method.Body,
                method.ExpressionBody,
            ],
            AccessorDeclarationSyntax accessor => [accessor.Body, accessor.ExpressionBody],
            ArrowExpressionClauseSyntax expressionBody => [expressionBody],
            TypeDeclarationSyntax type =>
            [
                .. DefaultValuesOf(type.ParameterList),
                .. PrimaryConstructorBaseArgumentsOf(type),
            ],
            _ => [],
        };

    private static IEnumerable<SyntaxNode?> DefaultValuesOf(BaseParameterListSyntax? parameters) =>
        parameters?.Parameters.Select(parameter => parameter.Default) ?? [];

    private static IEnumerable<SyntaxNode> PrimaryConstructorBaseArgumentsOf(
        TypeDeclarationSyntax type
    ) =>
        type.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>()
            .Select(baseType => baseType.ArgumentList)
        ?? [];
}
