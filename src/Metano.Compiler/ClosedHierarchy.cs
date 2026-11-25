using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// How a direct case of a C# 15 <c>closed</c> class relates to the closed class's
/// own type parameters.
/// </summary>
public enum ClosedCaseShape
{
    /// <summary>
    /// The case derives from the closed class instantiated with exactly its own type
    /// parameters, each once (<c>Leaf&lt;U&gt; : Tree&lt;U&gt;</c>), or neither is
    /// generic: the case is the same type for every instantiation of the closed class.
    /// </summary>
    SameTypeParameters,

    /// <summary>
    /// A non-generic case of a generic closed class instantiated with closed type
    /// arguments (<c>IntLeaf : Tree&lt;int&gt;</c>): a case only of that instantiation.
    /// </summary>
    FixedTypeArguments,

    /// <summary>Any other relation (an extra type parameter, a constructed argument).</summary>
    Other,
}

/// <summary>
/// One direct case of a closed class.
/// </summary>
/// <param name="Case">The case's definition.</param>
/// <param name="Shape">How the case relates to the closed class's type parameters.</param>
/// <param name="AsSeenFromClosedType">The case written with the closed class's type
/// parameters (<c>Leaf&lt;U&gt; : Tree&lt;U&gt;</c> → <c>Leaf&lt;T&gt;</c>) for
/// <see cref="ClosedCaseShape.SameTypeParameters"/>; the definition otherwise.</param>
public sealed record ClosedCase(
    INamedTypeSymbol Case,
    ClosedCaseShape Shape,
    INamedTypeSymbol AsSeenFromClosedType
)
{
    /// <summary>
    /// The closed class's type arguments the case derives from, for
    /// <see cref="ClosedCaseShape.FixedTypeArguments"/> (<c>IntLeaf</c> → <c>[int]</c>);
    /// empty otherwise.
    /// </summary>
    public IReadOnlyList<ITypeSymbol> FixedTypeArguments =>
        Shape is ClosedCaseShape.FixedTypeArguments ? Case.BaseType!.TypeArguments : [];
}

/// <summary>
/// The direct cases of a closed class (Roslyn's <c>GetClosedDerivedTypeInfo</c>), in
/// declaration order.
/// </summary>
/// <param name="Cases">The direct cases.</param>
/// <param name="IsComplete">Whether <paramref name="Cases"/> lists every possible
/// direct case (false, for example, for a generic closed class with an unspeakable
/// derived type).</param>
public sealed record ClosedCaseSet(IReadOnlyList<ClosedCase> Cases, bool IsComplete);

/// <summary>
/// Target-agnostic facts about C# 15 <c>closed</c> class hierarchies.
/// </summary>
public static class ClosedHierarchy
{
    /// <summary>
    /// The direct cases of <paramref name="closedType"/>, which must be a closed class
    /// (<see cref="ITypeSymbol.IsClosed"/>).
    /// </summary>
    public static ClosedCaseSet CasesOf(INamedTypeSymbol closedType)
    {
        var definition = closedType.OriginalDefinition;
        var info = definition.GetClosedDerivedTypeInfo(CancellationToken.None);

        var cases = info
            .ClosedDerivedTypes.OfType<INamedTypeSymbol>()
            .Select(derived => derived.OriginalDefinition)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(DeclarationOrder)
            .Select(derived => CaseOf(definition, derived))
            .ToList();

        return new ClosedCaseSet(cases, info.IsComplete);
    }

    private static ClosedCase CaseOf(INamedTypeSymbol closedType, INamedTypeSymbol derived)
    {
        var baseArguments = derived.BaseType?.TypeArguments ?? [];

        if (MapsOntoOwnTypeParameters(derived, baseArguments))
        {
            return new ClosedCase(
                derived,
                ClosedCaseShape.SameTypeParameters,
                AsSeenFrom(closedType, derived, baseArguments)
            );
        }

        var isFixed =
            derived.TypeParameters.Length == 0
            && baseArguments.Length > 0
            && baseArguments.All(argument => !MentionsTypeParameter(argument));

        return new ClosedCase(
            derived,
            isFixed ? ClosedCaseShape.FixedTypeArguments : ClosedCaseShape.Other,
            derived
        );
    }

    private static bool MapsOntoOwnTypeParameters(
        INamedTypeSymbol derived,
        IReadOnlyList<ITypeSymbol> baseArguments
    )
    {
        if (baseArguments.Count != derived.TypeParameters.Length)
            return false;

        var owned = baseArguments
            .OfType<ITypeParameterSymbol>()
            .Where(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.DeclaringType, derived)
            )
            .Distinct<ITypeParameterSymbol>(SymbolEqualityComparer.Default)
            .Count();

        return owned == baseArguments.Count;
    }

    private static INamedTypeSymbol AsSeenFrom(
        INamedTypeSymbol closedType,
        INamedTypeSymbol derived,
        IReadOnlyList<ITypeSymbol> baseArguments
    )
    {
        if (derived.TypeParameters.Length == 0)
            return derived;

        var arguments = derived
            .TypeParameters.Select(parameter =>
                (ITypeSymbol)closedType.TypeParameters[IndexOf(baseArguments, parameter)]
            )
            .ToArray();

        return derived.Construct(arguments);
    }

    private static int IndexOf(IReadOnlyList<ITypeSymbol> arguments, ITypeParameterSymbol parameter)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(arguments[i], parameter))
                return i;
        }

        return -1;
    }

    private static bool MentionsTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => MentionsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(MentionsTypeParameter),
            _ => false,
        };

    /// <summary>
    /// Source order (file path, then position) for source cases; metadata order
    /// (name) for cases read from a referenced assembly, which has no positions.
    /// </summary>
    private static (string File, int Position, string Name) DeclarationOrder(INamedTypeSymbol type)
    {
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);

        return location is null
            ? (string.Empty, 0, type.MetadataName)
            : (
                location.SourceTree?.FilePath ?? string.Empty,
                location.SourceSpan.Start,
                type.MetadataName
            );
    }
}
