using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Reads <c>using X = Y;</c> aliases from each C# source file and projects
/// them into a per-file <see cref="UsingAliasScope"/> the TS bridge uses to
/// substitute canonical type names with the user-declared alias on every
/// emit site (declarations, body identifiers, static member access).
/// <para>
/// Aliases declared at compilation-unit scope and inside a
/// <c>namespace { … }</c> block both qualify — Roslyn exposes both via
/// <see cref="CompilationUnitSyntax.Usings"/> and
/// <see cref="BaseNamespaceDeclarationSyntax.Usings"/>. The keys of the map
/// are the canonical type names produced by the IR (matching
/// <c>IrNamedTypeRef.Name</c>), and the values are the local alias the
/// printed TS module should use.
/// </para>
/// </summary>
public static class UsingAliasResolver
{
    public static UsingAliasScope? ResolveForTree(SyntaxTree? tree, Compilation compilation)
    {
        if (tree is null)
            return null;
        var canonicalToAlias = new Dictionary<string, string>(StringComparer.Ordinal);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();

        Collect(root.Usings, semanticModel, canonicalToAlias);
        foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            Collect(ns.Usings, semanticModel, canonicalToAlias);

        return UsingAliasScope.Create(canonicalToAlias);
    }

    private static void Collect(
        SyntaxList<UsingDirectiveSyntax> usings,
        SemanticModel semanticModel,
        Dictionary<string, string> canonicalToAlias
    )
    {
        foreach (var directive in usings)
        {
            if (directive.Alias is null)
                continue;

            var aliasName = directive.Alias.Name.Identifier.ValueText;
            if (string.IsNullOrEmpty(aliasName))
                continue;

            if (directive.Name is null)
                continue;
            var symbol = semanticModel.GetSymbolInfo(directive.Name).Symbol as INamedTypeSymbol;
            if (symbol is null)
                continue;

            var canonicalName = symbol.Name;
            if (canonicalName == aliasName)
                continue;

            canonicalToAlias[canonicalName] = aliasName;
        }
    }
}
