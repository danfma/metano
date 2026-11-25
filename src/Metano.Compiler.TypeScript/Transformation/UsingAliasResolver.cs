using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Reads the <c>using X = Y;</c> aliases in scope of one emitted file's types: the
/// compilation unit's and those of every <c>namespace { … }</c> block that encloses
/// one of the types (Roslyn exposes both via <see cref="CompilationUnitSyntax.Usings"/>
/// and <see cref="BaseNamespaceDeclarationSyntax.Usings"/>). Each alias keeps the type
/// it names (<see cref="UsingAlias"/>), so the TS bridge substitutes it for that type
/// only (#754) on every emit site (declarations, body identifiers, static member
/// access).
/// </summary>
public static class UsingAliasResolver
{
    /// <param name="declaresHere">Whether the emitted file declares a type: an alias
    /// of such a type is dropped, since the file binds the type under its own name and
    /// imports nothing to bind the alias (#635).</param>
    /// <param name="fileTypes">The types the emitted file declares: only the
    /// namespace blocks that enclose one of them contribute their aliases. Every block
    /// of the tree does when it is <c>null</c>.</param>
    public static IReadOnlyList<UsingAlias> ResolveForTree(
        SyntaxTree? tree,
        Compilation compilation,
        Func<INamedTypeSymbol, bool>? declaresHere = null,
        IReadOnlyList<INamedTypeSymbol>? fileTypes = null
    )
    {
        if (tree is null)
            return [];

        var aliases = new List<UsingAlias>();
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();
        var excluded = declaresHere ?? (_ => false);

        Collect(root.Usings, semanticModel, excluded, aliases);

        foreach (var ns in NamespaceBlocksInScope(root, tree, fileTypes))
            Collect(ns.Usings, semanticModel, excluded, aliases);

        return aliases;
    }

    /// <summary>
    /// The <c>global using</c> aliases of every tree of <paramref name="compilation"/>,
    /// read once per compilation and filtered per emitted file by
    /// <see cref="ResolveForGroup"/>.
    /// </summary>
    public static IReadOnlyList<UsingAlias> GlobalAliases(Compilation compilation)
    {
        var aliases = new List<UsingAlias>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var globals = tree.GetCompilationUnitRoot().Usings.Where(IsGlobal).ToList();

            if (globals.Count > 0)
                Collect(globals, compilation.GetSemanticModel(tree), _ => false, aliases);
        }

        return aliases;
    }

    /// <summary>
    /// The aliases in scope of one emitted file: the <paramref name="globalAliases"/>
    /// (<see cref="GlobalAliases"/>), then, for every tree that declares one of
    /// <paramref name="fileTypes"/> (a partial type, an <c>[EmitInFile]</c> group),
    /// its compilation unit's aliases and those of the namespace blocks enclosing the
    /// types. A later alias of the same type wins.
    /// </summary>
    public static IReadOnlyList<UsingAlias> ResolveForGroup(
        Compilation compilation,
        IReadOnlyList<UsingAlias> globalAliases,
        IReadOnlyList<INamedTypeSymbol> fileTypes,
        Func<INamedTypeSymbol, bool> declaresHere
    )
    {
        var aliases = globalAliases.Where(alias => !declaresHere(alias.Symbol)).ToList();

        var declaringTrees = fileTypes
            .SelectMany(type => type.DeclaringSyntaxReferences)
            .Select(reference => reference.SyntaxTree)
            .Distinct();

        foreach (var tree in declaringTrees)
        {
            var root = tree.GetCompilationUnitRoot();
            var semanticModel = compilation.GetSemanticModel(tree);

            Collect(
                root.Usings.Where(directive => !IsGlobal(directive)),
                semanticModel,
                declaresHere,
                aliases
            );

            foreach (var ns in NamespaceBlocksInScope(root, tree, fileTypes))
                Collect(ns.Usings, semanticModel, declaresHere, aliases);
        }

        return aliases;
    }

    private static bool IsGlobal(UsingDirectiveSyntax directive) =>
        directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword);

    private static IEnumerable<BaseNamespaceDeclarationSyntax> NamespaceBlocksInScope(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        IReadOnlyList<INamedTypeSymbol>? fileTypes
    )
    {
        var blocks = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>();

        if (fileTypes is null)
            return blocks;

        var enclosing = fileTypes
            .SelectMany(type => type.DeclaringSyntaxReferences)
            .Where(reference => reference.SyntaxTree == tree)
            .SelectMany(reference =>
                reference.GetSyntax().Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            )
            .ToHashSet();

        return blocks.Where(enclosing.Contains);
    }

    private static void Collect(
        IEnumerable<UsingDirectiveSyntax> usings,
        SemanticModel semanticModel,
        Func<INamedTypeSymbol, bool> excluded,
        List<UsingAlias> aliases
    )
    {
        foreach (var directive in usings)
        {
            if (AliasOf(directive, semanticModel, excluded) is { } alias)
                aliases.Add(alias);
        }
    }

    /// <summary>
    /// The alias <paramref name="directive"/> declares for a type, or <c>null</c> when
    /// it declares none, names no type, names a type the file declares, or spells the
    /// type's own name (which renames nothing).
    /// </summary>
    private static UsingAlias? AliasOf(
        UsingDirectiveSyntax directive,
        SemanticModel semanticModel,
        Func<INamedTypeSymbol, bool> excluded
    )
    {
        if (directive.Alias is null || directive.Name is null)
            return null;

        var aliasName = directive.Alias.Name.Identifier.ValueText;

        if (string.IsNullOrEmpty(aliasName))
            return null;

        if (semanticModel.GetSymbolInfo(directive.Name).Symbol is not INamedTypeSymbol symbol)
            return null;

        if (excluded(symbol.OriginalDefinition) || symbol.Name == aliasName)
            return null;

        var definition = symbol.OriginalDefinition;

        var @namespace = definition.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        return new UsingAlias(
            aliasName,
            new UsingAliasTarget(@namespace, definition.Name),
            definition
        )
        {
            Location = directive.GetLocation(),
        };
    }

    /// <summary>
    /// The aliases of one emitted file that reuse an earlier alias's name for another
    /// type: two files of one <c>[EmitInFile]</c> group (or of one partial type) may
    /// each declare <c>using N = …;</c> for a different type, but the emitted module
    /// binds the name once.
    /// </summary>
    public static IEnumerable<(UsingAlias First, UsingAlias Second)> Conflicts(
        IReadOnlyList<UsingAlias> aliases
    )
    {
        var byName = new Dictionary<string, UsingAlias>(StringComparer.Ordinal);

        foreach (var alias in aliases)
        {
            if (
                !byName.TryAdd(alias.Alias, alias)
                && !SymbolEqualityComparer.Default.Equals(byName[alias.Alias].Symbol, alias.Symbol)
            )
                yield return (byName[alias.Alias], alias);
        }
    }
}
