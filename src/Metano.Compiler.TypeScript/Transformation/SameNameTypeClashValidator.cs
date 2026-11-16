using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Rejects an emitted module that refers to two different project types printed under
/// one name (#504): <c>Na.R23</c> and <c>Nb.R23</c> both print as the bare <c>R23</c>,
/// so the one import line the file can hold binds a single module and the other
/// reference silently constructs or tests the wrong class. The second reference reports
/// MS0070 at its C# location; rename one of the two with <c>[Name]</c>. A <c>using</c>
/// alias prints under its own name and is not a reference to the bare name.
/// </summary>
internal sealed class SameNameTypeClashValidator(
    Compilation compilation,
    TypeScriptTransformContext context,
    IReadOnlyList<INamedTypeSymbol> transpilableTypes
)
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];
    private readonly Lazy<IReadOnlySet<string>> _ambiguousNames = new(() =>
        AmbiguousNames(transpilableTypes, context)
    );

    /// <summary>
    /// The MS0070 findings of one emitted file: <paramref name="fileTypes"/> are the
    /// types it declares, whose bodies and signatures are the references it holds.
    /// </summary>
    public IEnumerable<MetanoDiagnostic> Validate(
        string fileName,
        IReadOnlyList<INamedTypeSymbol> fileTypes
    )
    {
        if (_ambiguousNames.Value.Count == 0)
            yield break;

        var bound = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var type in fileTypes)
            bound[context.ResolveTsName(type)] = type;

        foreach (var reference in ReferencesIn(fileTypes))
        {
            var name = context.ResolveTsName(reference.Type);

            if (!bound.TryAdd(name, reference.Type) && !Same(bound[name], reference.Type))
                yield return Clash(name, fileName, bound[name], reference);
        }
    }

    private IEnumerable<(INamedTypeSymbol Type, Location Location)> ReferencesIn(
        IEnumerable<INamedTypeSymbol> fileTypes
    ) =>
        fileTypes
            .SelectMany(type => type.DeclaringSyntaxReferences)
            .Select(reference => reference.GetSyntax())
            .SelectMany(syntax => syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            .Where(identifier => _ambiguousNames.Value.Contains(identifier.Identifier.ValueText))
            .Select(ReferencedType)
            .OfType<(INamedTypeSymbol, Location)>();

    private (INamedTypeSymbol Type, Location Location)? ReferencedType(IdentifierNameSyntax node)
    {
        var symbol = ModelFor(node.SyntaxTree).GetSymbolInfo(node).Symbol;

        var type = symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol { MethodKind: MethodKind.Constructor } constructor =>
                constructor.ContainingType,
            _ => null,
        };

        // Only a type spelled by its own name: a `using` alias prints under the alias.
        return type is { ContainingType: null } own
            && own.Name == node.Identifier.ValueText
            && context.TranspilableTypes.ContainsKey(context.ResolveTsName(own))
                ? (own.OriginalDefinition, node.GetLocation())
                : null;
    }

    private static bool Same(INamedTypeSymbol first, INamedTypeSymbol second) =>
        SymbolEqualityComparer.Default.Equals(first.OriginalDefinition, second.OriginalDefinition);

    private static MetanoDiagnostic Clash(
        string name,
        string fileName,
        INamedTypeSymbol first,
        (INamedTypeSymbol Type, Location Location) second
    ) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.ImportedNameClash,
            $"'{name}' names both '{first.ToDisplayString()}' and "
                + $"'{second.Type.ToDisplayString()}' in '{fileName}'; the bare name is "
                + "ambiguous and binds one module. Rename one of the two with [Name(\"...\")].",
            second.Location
        );

    /// <summary>
    /// The emitted names more than one top-level project type prints under: the only
    /// names that can clash, so a project without any skips the whole check.
    /// </summary>
    private static IReadOnlySet<string> AmbiguousNames(
        IEnumerable<INamedTypeSymbol> types,
        TypeScriptTransformContext context
    ) =>
        types
            .Where(type => type.ContainingType is null)
            .GroupBy(type => context.ResolveTsName(type), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    private SemanticModel ModelFor(SyntaxTree tree)
    {
        if (!_models.TryGetValue(tree, out var model))
        {
            model = compilation.GetSemanticModel(tree);
            _models[tree] = model;
        }

        return model;
    }
}
