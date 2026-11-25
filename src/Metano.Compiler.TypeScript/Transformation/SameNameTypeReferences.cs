using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The references an emitted file holds to project types whose emitted name another
/// top-level project type prints under too (<c>App.Node&lt;T&gt;</c> and
/// <c>App.Cyc.Node</c> both print as <c>Node</c>, and so does a <c>[Name("Node")]</c>
/// type), each resolved to its symbol. The bare name alone cannot say which module to
/// import: the clash check (#504) reports a file that names two of them, and the import
/// collector binds the name to the one type the file names (#743). A reference may
/// spell the type's C# name or a <c>using</c> alias of it; an alias prints under its own
/// name and binds its import by the symbol it names (#754), so only the spelled
/// references can clash or bind the name.
/// </summary>
internal sealed class SameNameTypeReferences(
    Compilation compilation,
    TypeScriptTransformContext context,
    IReadOnlyList<INamedTypeSymbol> transpilableTypes
)
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    private readonly Lazy<IReadOnlyList<INamedTypeSymbol>> _ambiguousTypes = new(() =>
        AmbiguousTypes(transpilableTypes, context)
    );

    private HashSet<string>? _referenceSpellings;

    /// <summary>Whether no two top-level project types print under one name.</summary>
    public bool IsEmpty => _ambiguousTypes.Value.Count == 0;

    /// <summary>
    /// Every spelling a reference to an ambiguous type can take: the types' C# names
    /// (a <c>[Name]</c>-renamed type is spelled by its C# name) and the names of the
    /// <c>using</c> aliases that name one of them.
    /// </summary>
    private IReadOnlySet<string> ReferenceSpellings =>
        _referenceSpellings ??= [
            .. _ambiguousTypes.Value.Select(type => type.Name),
            .. compilation
                .SyntaxTrees.SelectMany(tree =>
                    tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
                )
                .Where(directive => directive.Alias is not null && NamesAmbiguousType(directive))
                .Select(directive => directive.Alias!.Name.Identifier.ValueText),
        ];

    /// <summary>
    /// The references <paramref name="fileTypes"/> (the types one file declares) hold to
    /// a type whose emitted name is ambiguous, in source order.
    /// </summary>
    public IReadOnlyList<SameNameReference> In(IEnumerable<INamedTypeSymbol> fileTypes) =>
        IsEmpty
            ? []
            :
            [
                .. fileTypes
                    .SelectMany(type => type.DeclaringSyntaxReferences)
                    .SelectMany(reference => ReferencesIn(reference.GetSyntax())),
            ];

    /// <summary>
    /// The references one declaration holds, in source order: the names it spells and
    /// the types its inferred lambda parameter annotations print.
    /// </summary>
    private IEnumerable<SameNameReference> ReferencesIn(SyntaxNode declaration)
    {
        var spelled = declaration
            .DescendantNodesAndSelf()
            .OfType<SimpleNameSyntax>()
            .Where(name => ReferenceSpellings.Contains(name.Identifier.ValueText))
            .Select(ReferencedType)
            .OfType<SameNameReference>();

        var inferred = declaration
            .DescendantNodes()
            .OfType<ParameterSyntax>()
            .SelectMany(InferredLambdaReferences);

        return spelled.Concat(inferred).OrderBy(reference => reference.Location.SourceSpan.Start);
    }

    /// <summary>
    /// The ambiguous types the inferred type of a lambda parameter written without one
    /// (<c>n =&gt; n.Value</c>) names: the emitted annotation prints them
    /// (<c>(n: Node&lt;number&gt;) =&gt; …</c>), so they bind the import like a spelled
    /// reference (#751).
    /// </summary>
    private IEnumerable<SameNameReference> InferredLambdaReferences(ParameterSyntax parameter)
    {
        if (parameter.Type is not null)
            return [];

        if (
            ModelFor(parameter.SyntaxTree).GetDeclaredSymbol(parameter)
            is not IParameterSymbol symbol
        )
            return [];

        return TypesNamedBy(symbol.Type)
            .Where(IsAmbiguous)
            .Select(type => new SameNameReference(type, parameter.GetLocation(), IsAlias: false));
    }

    /// <summary>
    /// The type definitions <paramref name="type"/> names: itself, its type arguments
    /// and its array element types, recursively.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> TypesNamedBy(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => TypesNamedBy(array.ElementType),
            INamedTypeSymbol named =>
            [
                named.OriginalDefinition,
                .. named.TypeArguments.SelectMany(TypesNamedBy),
            ],
            _ => [],
        };

    /// <summary>
    /// The project type each ambiguous name of one file binds: a type the file declares,
    /// else the first one its spelled <paramref name="references"/> name. A reference
    /// through a <c>using</c> alias binds its import by the alias's symbol instead
    /// (#754), so it binds no name here. Empty when the project has no ambiguous name.
    /// </summary>
    public IReadOnlyDictionary<string, IrTranspilableTypeRef> BindingsIn(
        IReadOnlyList<INamedTypeSymbol> fileTypes,
        IReadOnlyList<SameNameReference> references
    )
    {
        var bindings = new Dictionary<string, IrTranspilableTypeRef>(StringComparer.Ordinal);

        if (IsEmpty)
            return bindings;

        var declared = fileTypes.Where(IsAmbiguous);

        var spelled = references
            .Where(reference => !reference.IsAlias)
            .Select(reference => reference.Type);

        foreach (var type in declared.Concat(spelled))
            bindings.TryAdd(Emitted(type), TranspilableTypeProjection.Of(type, Emitted(type)));

        return bindings;
    }

    private bool NamesAmbiguousType(UsingDirectiveSyntax directive) =>
        (ModelFor(directive.SyntaxTree).GetDeclaredSymbol(directive) as IAliasSymbol)?.Target
            is INamedTypeSymbol target
        && IsAmbiguous(target.OriginalDefinition);

    private bool IsAmbiguous(INamedTypeSymbol type) =>
        _ambiguousTypes.Value.Contains(type, SymbolEqualityComparer.Default);

    private string Emitted(INamedTypeSymbol type) => context.ResolveTsName(type);

    private SameNameReference? ReferencedType(SimpleNameSyntax node)
    {
        var symbol = ModelFor(node.SyntaxTree).GetSymbolInfo(node).Symbol;

        var type = symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol { MethodKind: MethodKind.Constructor } constructor =>
                constructor.ContainingType,
            _ => null,
        };

        if (type?.OriginalDefinition is not { } definition || !IsAmbiguous(definition))
            return null;

        return new SameNameReference(
            definition,
            node.GetLocation(),
            IsAlias: definition.Name != node.Identifier.ValueText
        );
    }

    /// <summary>
    /// The top-level project types whose emitted name another one prints under too: the
    /// only types whose references can clash, so a project without any skips every lookup.
    /// </summary>
    private static IReadOnlyList<INamedTypeSymbol> AmbiguousTypes(
        IEnumerable<INamedTypeSymbol> types,
        TypeScriptTransformContext context
    ) =>
        [
            .. types
                .Where(type => type.ContainingType is null)
                .GroupBy(type => context.ResolveTsName(type), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group),
        ];

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

/// <summary>
/// One reference to a type whose emitted name is ambiguous: the type, where the file
/// names it, and whether it names it through a <c>using</c> alias.
/// </summary>
internal sealed record SameNameReference(INamedTypeSymbol Type, Location Location, bool IsAlias);
