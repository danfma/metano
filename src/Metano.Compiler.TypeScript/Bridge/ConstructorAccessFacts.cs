using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The compilation-wide facts <see cref="ConstructorAccessPolicy"/> needs
/// beyond a type's IR (FR-049): the declared constructors of every reference
/// type with a non-public one (to name them in <c>MS0060</c> / <c>MS0061</c>),
/// which classes code outside their class body constructs, and which classes
/// an emitted class extends. Accessibility comes from
/// <see cref="IrConstructorExtractor.DeclaredVisibility"/>, the function the IR
/// is built from, so the facts and the IR agree. Value types are left out:
/// their implicit parameterless constructor is always public.
/// <para>
/// The facts cross file boundaries — a <c>SerializerContext</c> or a derived
/// class in another file changes a type's constructor — so
/// <see cref="FingerprintOf"/> feeds them into the per-group cache key.
/// </para>
/// </summary>
public sealed class ConstructorAccessFacts
{
    private const string OutsideConstructionFact = "outside";
    private const string ExtendedFact = "extended";

    private readonly Dictionary<INamedTypeSymbol, IReadOnlyList<DeclaredConstructor>> _constructors;
    private readonly Dictionary<INamedTypeSymbol, OutsideConstruction> _outsideConstructions;
    private readonly HashSet<INamedTypeSymbol> _extended;

    private ConstructorAccessFacts(
        Dictionary<INamedTypeSymbol, IReadOnlyList<DeclaredConstructor>> constructors,
        Dictionary<INamedTypeSymbol, OutsideConstruction> outsideConstructions,
        HashSet<INamedTypeSymbol> extended
    )
    {
        _constructors = constructors;
        _outsideConstructions = outsideConstructions;
        _extended = extended;
    }

    /// <summary>No type declares a non-public constructor.</summary>
    public static ConstructorAccessFacts Empty { get; } =
        new(
            new Dictionary<INamedTypeSymbol, IReadOnlyList<DeclaredConstructor>>(
                SymbolEqualityComparer.Default
            ),
            new Dictionary<INamedTypeSymbol, OutsideConstruction>(SymbolEqualityComparer.Default),
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
        );

    /// <summary>
    /// The declared constructors of <paramref name="type"/> when one of them
    /// is non-public; empty otherwise.
    /// </summary>
    public IReadOnlyList<DeclaredConstructor> ConstructorsOf(INamedTypeSymbol type) =>
        _constructors.GetValueOrDefault(type.OriginalDefinition) ?? [];

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
    /// in them that another file can change, as a stable string; empty when
    /// none is recorded.
    /// </summary>
    public string FingerprintOf(IEnumerable<INamedTypeSymbol> groupTypes)
    {
        var group = groupTypes.ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var entries = _outsideConstructions
            .Keys.Select(type => (Type: type, Fact: OutsideConstructionFact))
            .Concat(_extended.Select(type => (Type: type, Fact: ExtendedFact)))
            .Where(entry => group.Contains(OutermostType(entry.Type)))
            .Select(entry => $"{entry.Type.ToDisplayString()}:{entry.Fact}")
            .Order(StringComparer.Ordinal);

        return string.Join(";", entries);
    }

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
        var constructors = emittedTypes
            .Where(type => !type.IsValueType)
            .Select(type =>
                (Type: type.OriginalDefinition, Constructors: DeclaredConstructors(type))
            )
            .Where(entry => entry.Constructors.Any(IsNonPublic))
            .ToDictionary(
                entry => entry.Type,
                entry => entry.Constructors,
                (IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default
            );

        if (constructors.Count == 0)
            return Empty;

        var restricted = constructors
            .Where(entry => entry.Value.All(IsNonPublic))
            .Select(entry => entry.Key)
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        return new ConstructorAccessFacts(
            constructors,
            OutsideConstructions(compilation, emittedTypes, jsonConstructedTypesOf, restricted),
            ExtendedTypes(emittedTypes, restricted)
        );
    }

    private static IReadOnlyList<DeclaredConstructor> DeclaredConstructors(INamedTypeSymbol type) =>
        IrConstructorExtractor.AllExplicitConstructors(type).Select(Describe).ToList();

    private static DeclaredConstructor Describe(IMethodSymbol constructor)
    {
        var parameters = constructor.Parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        );

        return new DeclaredConstructor(
            TsConstructorModifier.Of(IrConstructorExtractor.DeclaredVisibility(constructor)),
            $"'{constructor.ContainingType.Name}({string.Join(", ", parameters)})' "
                + $"({SyntaxFacts.GetText(constructor.DeclaredAccessibility)})",
            constructor.Locations.FirstOrDefault()
        );
    }

    private static bool IsNonPublic(DeclaredConstructor constructor) =>
        constructor.Modifier is not TsAccessibility.Public;

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
            .SelectMany(declaration => ConstructedTypesIn(compilation, declaration))
            .Where(containers.Contains);
    }

    /// <summary>
    /// The types a <c>new</c> in <paramref name="declaration"/> constructs,
    /// outside the declarations of its own nested types (each emitted nested
    /// type is scanned on its own).
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ConstructedTypesIn(
        Compilation compilation,
        SyntaxNode declaration
    )
    {
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);

        bool StaysInThisTypeBody(SyntaxNode node) =>
            node == declaration || node is not BaseTypeDeclarationSyntax;

        return declaration
            .DescendantNodes(StaysInThisTypeBody)
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
/// A declared constructor as the FR-049 diagnostics name it: its TypeScript
/// <paramref name="Modifier"/>, a <paramref name="Description"/> such as
/// <c>'Size(int, int)' (private)</c>, and its source location.
/// </summary>
public sealed record DeclaredConstructor(
    TsAccessibility Modifier,
    string Description,
    Location? Location
);

/// <summary>
/// Code outside a type's TypeScript class body that constructs it;
/// <paramref name="Site"/> names it for the <c>MS0061</c> message.
/// </summary>
public sealed record OutsideConstruction(string Site);
