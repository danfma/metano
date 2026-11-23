using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The generic types a nested class, struct or interface is declared in. C# lets
/// the nested type use their type parameters without declaring them
/// (<c>Gen&lt;T&gt;.Sub : Gen&lt;T&gt;</c>), and a constructed reference carries
/// their type arguments (<c>Gen&lt;int&gt;.Sub</c>); a target that declares the
/// nested type outside its container must bind them itself (#436).
/// </summary>
public static class GenericContainers
{
    /// <summary>
    /// The type parameters of the generic containers of <paramref name="type"/>,
    /// outermost first; empty when no container is generic, and for an enum or a
    /// delegate, which cannot use them.
    /// </summary>
    public static IReadOnlyList<ITypeParameterSymbol> TypeParametersOf(INamedTypeSymbol type) =>
        [.. ContainersOf(type).SelectMany(container => container.TypeParameters)];

    /// <summary>
    /// The type arguments of the generic containers of the constructed
    /// <paramref name="type"/>, outermost first, matching
    /// <see cref="TypeParametersOf"/>.
    /// </summary>
    public static IReadOnlyList<ITypeSymbol> TypeArgumentsOf(INamedTypeSymbol type) =>
        [.. ContainersOf(type).SelectMany(container => container.TypeArguments)];

    private static IEnumerable<INamedTypeSymbol> ContainersOf(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface))
            return [];

        var containers = new List<INamedTypeSymbol>();

        for (
            var current = type.ContainingType;
            current is not null;
            current = current.ContainingType
        )
            containers.Insert(0, current);

        return containers;
    }
}
