using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads the body of a type's static constructor (<c>static C() { … }</c>, #426).
/// C# runs it once, after the static field initializers, before the type is
/// first used; a target that can run code when the type is initialized lowers
/// the statements, and one that cannot reports the constructor.
/// </summary>
public static class IrStaticConstructorExtractor
{
    /// <summary>
    /// The statements of the static constructor of <paramref name="type"/>;
    /// <c>null</c> when it declares none, or when no compilation is supplied
    /// (shape-only mode) to read its syntax from.
    /// </summary>
    public static IReadOnlyList<IrStatement>? Extract(
        INamedTypeSymbol type,
        IrExtractionContext context
    )
    {
        if (context.Compilation is not { } compilation)
            return null;

        var syntax = DeclaringSyntaxOf(type);

        if (syntax is null || (syntax.Body is null && syntax.ExpressionBody is null))
            return null;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var extractor = new IrStatementExtractor(model, context, inheritedLocalNames: null);

        return extractor.ExtractBody(syntax.Body, syntax.ExpressionBody, isVoid: true).Statements;
    }

    /// <summary>
    /// The declared static constructor of <paramref name="type"/>, for a
    /// diagnostic that points at it; <c>null</c> when it declares none.
    /// </summary>
    public static IMethodSymbol? DeclaredBy(INamedTypeSymbol type) =>
        type.StaticConstructors.FirstOrDefault(ctor => !ctor.IsImplicitlyDeclared);

    private static ConstructorDeclarationSyntax? DeclaringSyntaxOf(INamedTypeSymbol type) =>
        DeclaredBy(type)?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
        as ConstructorDeclarationSyntax;
}
