using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The compilation-wide facts that decide whether a non-public constructor
/// keeps its TypeScript modifier (FR-049): which classes code outside their
/// class body constructs, and which classes an emitted class extends. Only
/// types whose constructors all map to a non-public modifier are recorded;
/// every other type emits a public constructor anyway.
/// <para>
/// The facts cross file boundaries — a <c>SerializerContext</c> or a derived
/// class in another file changes a type's constructor — so
/// <see cref="FingerprintOf"/> feeds them into the per-group cache key.
/// </para>
/// </summary>
public sealed class ConstructorAccessFacts
{
    private readonly Dictionary<INamedTypeSymbol, OutsideConstruction> _outsideConstructions;
    private readonly HashSet<INamedTypeSymbol> _extended;

    private ConstructorAccessFacts(
        Dictionary<INamedTypeSymbol, OutsideConstruction> outsideConstructions,
        HashSet<INamedTypeSymbol> extended
    )
    {
        _outsideConstructions = outsideConstructions;
        _extended = extended;
    }

    /// <summary>No type is constructed outside its class body or extended.</summary>
    public static ConstructorAccessFacts Empty { get; } =
        new(
            new Dictionary<INamedTypeSymbol, OutsideConstruction>(SymbolEqualityComparer.Default),
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
        );

    /// <summary>
    /// Collects the facts over <paramref name="emittedTypes"/> (top-level and
    /// emitted nested types). <paramref name="jsonConstructedTypesOf"/> yields
    /// the classes a <c>SerializerContext</c> type constructs through its
    /// <c>TypeSpec</c>s.
    /// </summary>
    public static ConstructorAccessFacts Collect(
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> emittedTypes,
        Func<INamedTypeSymbol, IEnumerable<INamedTypeSymbol>> jsonConstructedTypesOf
    )
    {
        var restricted = emittedTypes
            .Where(DeclaresOnlyNonPublicConstructors)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (restricted.Count == 0)
            return Empty;

        return new ConstructorAccessFacts(
            OutsideConstructions(compilation, emittedTypes, jsonConstructedTypesOf, restricted),
            ExtendedTypes(emittedTypes, restricted)
        );
    }

    /// <summary>
    /// Where code outside the TypeScript class body of <paramref name="type"/>
    /// constructs it, or <c>null</c> when nothing does.
    /// </summary>
    public OutsideConstruction? OutsideConstructionOf(INamedTypeSymbol type) =>
        _outsideConstructions.GetValueOrDefault(type.OriginalDefinition);

    /// <summary>Whether an emitted class directly extends <paramref name="type"/>.</summary>
    public bool IsExtended(INamedTypeSymbol type) => _extended.Contains(type.OriginalDefinition);

    /// <summary>
    /// The facts recorded for <paramref name="groupTypes"/> and the types nested
    /// in them, as a stable string; empty when none is recorded.
    /// </summary>
    public string FingerprintOf(IEnumerable<INamedTypeSymbol> groupTypes)
    {
        var group = groupTypes.ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var entries = _outsideConstructions
            .Keys.Select(type => (Type: type, Fact: "outside"))
            .Concat(_extended.Select(type => (Type: type, Fact: "extended")))
            .Where(entry => group.Contains(OutermostType(entry.Type)))
            .Select(entry => $"{entry.Type.ToDisplayString()}:{entry.Fact}")
            .Order(StringComparer.Ordinal);

        return string.Join(";", entries);
    }

    /// <summary>
    /// The first site outside the class body that constructs each restricted
    /// type: a <c>SerializerContext</c>'s <c>TypeSpec</c>, or a <c>new</c> in a
    /// nested type.
    /// </summary>
    private static Dictionary<INamedTypeSymbol, OutsideConstruction> OutsideConstructions(
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> emittedTypes,
        Func<INamedTypeSymbol, IEnumerable<INamedTypeSymbol>> jsonConstructedTypesOf,
        IReadOnlySet<INamedTypeSymbol> restricted
    )
    {
        var constructions = new Dictionary<INamedTypeSymbol, OutsideConstruction>(
            SymbolEqualityComparer.Default
        );

        foreach (var type in emittedTypes)
        {
            var jsonSite = new OutsideConstruction(
                $"the TypeSpec of the JSON SerializerContext '{type.Name}'"
            );

            foreach (var constructed in jsonConstructedTypesOf(type).Where(restricted.Contains))
                constructions.TryAdd(constructed, jsonSite);

            var nestedSite = new OutsideConstruction($"the nested type '{type.ToDisplayString()}'");

            foreach (var constructed in ContainersConstructedBy(compilation, type, restricted))
                constructions.TryAdd(constructed, nestedSite);
        }

        return constructions;
    }

    private static HashSet<INamedTypeSymbol> ExtendedTypes(
        IReadOnlyList<INamedTypeSymbol> emittedTypes,
        IReadOnlySet<INamedTypeSymbol> restricted
    ) =>
        emittedTypes
            .Select(type => type.BaseType?.OriginalDefinition)
            .OfType<INamedTypeSymbol>()
            .Where(restricted.Contains)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    /// <summary>
    /// Whether <paramref name="type"/> declares constructors and none of them
    /// maps to a public one — the most permissive modifier, which the
    /// constructor is emitted with, is then non-public.
    /// </summary>
    private static bool DeclaresOnlyNonPublicConstructors(INamedTypeSymbol type) =>
        IrConstructorExtractor.ModeledConstructors(type) is { Count: > 0 } constructors
        && constructors.All(constructor =>
            ConstructorAccessPolicy.Map(constructor) is not TsAccessibility.Public
        );

    /// <summary>
    /// The restricted containers of <paramref name="nested"/> that a
    /// <c>new</c> in its declaration constructs: C# lets a nested type call its
    /// container's private or protected constructor, but the TypeScript target
    /// emits the nested type outside the container's class body.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ContainersConstructedBy(
        Compilation compilation,
        INamedTypeSymbol nested,
        IReadOnlySet<INamedTypeSymbol> restricted
    )
    {
        var containers = ContainersOf(nested)
            .Where(restricted.Contains)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (containers.Count == 0)
            return [];

        return nested
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .SelectMany(declaration => ConstructedTypesIn(declaration, compilation))
            .Where(containers.Contains);
    }

    /// <summary>
    /// The types a <c>new</c> in <paramref name="declaration"/> constructs,
    /// outside the declarations of its own nested types (each emitted nested
    /// type is scanned on its own).
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ConstructedTypesIn(
        SyntaxNode declaration,
        Compilation compilation
    )
    {
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);

        return declaration
            .DescendantNodes(node => node == declaration || node is not BaseTypeDeclarationSyntax)
            .OfType<BaseObjectCreationExpressionSyntax>()
            .Select(creation => model.GetSymbolInfo(creation).Symbol)
            .OfType<IMethodSymbol>()
            .Select(constructor => constructor.ContainingType.OriginalDefinition);
    }

    /// <summary>The types containing <paramref name="type"/>, innermost first.</summary>
    private static IEnumerable<INamedTypeSymbol> ContainersOf(INamedTypeSymbol type) =>
        type.ContainingType is { } container
            ? ContainersOf(container).Prepend(container.OriginalDefinition)
            : [];

    private static INamedTypeSymbol OutermostType(INamedTypeSymbol type) =>
        ContainersOf(type).LastOrDefault() ?? type.OriginalDefinition;
}

/// <summary>
/// Code outside a type's TypeScript class body that constructs it;
/// <paramref name="Site"/> names it for the <c>MS0061</c> message.
/// </summary>
public sealed record OutsideConstruction(string Site);
