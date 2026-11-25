using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The references an emitted file holds to project types whose emitted name another
/// top-level project type prints under too (<c>App.Node&lt;T&gt;</c> and
/// <c>App.Cyc.Node</c> both print as <c>Node</c>), each resolved to its symbol. The
/// bare name alone cannot say which module to import: the clash check (#504) reports a
/// file that names both, and the import collector binds the name to the one type the
/// file names (#743). A <c>using</c> alias prints under its own name and is not a
/// reference to the bare name.
/// </summary>
internal sealed class SameNameTypeReferences(
    Compilation compilation,
    TypeScriptTransformContext context,
    IReadOnlyList<INamedTypeSymbol> transpilableTypes
)
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    private readonly Lazy<IReadOnlySet<string>> _ambiguousNames = new(() =>
        AmbiguousNames(transpilableTypes, context)
    );

    /// <summary>Whether no two top-level project types print under one name.</summary>
    public bool IsEmpty => _ambiguousNames.Value.Count == 0;

    /// <summary>
    /// The references <paramref name="fileTypes"/> (the types one file declares) spell
    /// to an ambiguous name, in source order, with the type each one binds.
    /// </summary>
    public IEnumerable<(INamedTypeSymbol Type, Location Location)> In(
        IEnumerable<INamedTypeSymbol> fileTypes
    ) =>
        IsEmpty
            ? []
            : fileTypes
                .SelectMany(type => type.DeclaringSyntaxReferences)
                .Select(reference => reference.GetSyntax())
                .SelectMany(syntax => syntax.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
                .Where(name => _ambiguousNames.Value.Contains(name.Identifier.ValueText))
                .Select(ReferencedType)
                .OfType<(INamedTypeSymbol, Location)>();

    /// <summary>
    /// The project type each ambiguous name of one file binds: a type the file declares,
    /// else the first one it names. Empty when the project has no ambiguous name.
    /// </summary>
    public IReadOnlyDictionary<string, IrTranspilableTypeRef> BindingsIn(
        IReadOnlyList<INamedTypeSymbol> fileTypes
    )
    {
        var bindings = new Dictionary<string, IrTranspilableTypeRef>(StringComparer.Ordinal);

        if (IsEmpty)
            return bindings;

        var declared = fileTypes.Where(type => _ambiguousNames.Value.Contains(Emitted(type)));

        foreach (var type in declared.Concat(In(fileTypes).Select(reference => reference.Type)))
            bindings.TryAdd(Emitted(type), TranspilableTypeProjection.Of(type, Emitted(type)));

        return bindings;
    }

    private string Emitted(INamedTypeSymbol type) => context.ResolveTsName(type);

    private (INamedTypeSymbol Type, Location Location)? ReferencedType(SimpleNameSyntax node)
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
        return
            type is { ContainingType: null } own
            && own.Name == node.Identifier.ValueText
            && context.TranspilableTypes.ContainsKey(Emitted(own))
            ? (own.OriginalDefinition, node.GetLocation())
            : null;
    }

    /// <summary>
    /// The emitted names more than one top-level project type prints under: the only
    /// names that can clash, so a project without any skips every lookup.
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
