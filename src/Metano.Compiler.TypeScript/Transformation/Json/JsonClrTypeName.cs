using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The name System.Text.Json prints for a type in its conversion errors:
/// <c>Type.ToString()</c> of the runtime type. A namespace-qualified
/// metadata name, <c>+</c> between a nested type and its container, and the
/// type arguments of the whole chain in brackets at the end:
/// <c>System.Int32</c>, <c>MyApp.Outer+Inner</c>,
/// <c>MyApp.Pair`1[System.Int32]</c> (measured on .NET 10).
/// </summary>
internal static class JsonClrTypeName
{
    public static string Of(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => Of(array.ElementType) + ArraySuffix(array.Rank),
            INamedTypeSymbol named => NamedTypeName(named),
            _ => type.MetadataName,
        };

    private static string NamedTypeName(INamedTypeSymbol type)
    {
        var typeArguments = TypeArgumentChainOf(type).Select(Of).ToList();
        var name = QualifiedMetadataName(type);

        return typeArguments.Count == 0 ? name : $"{name}[{string.Join(",", typeArguments)}]";
    }

    private static string QualifiedMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } container)
            return $"{QualifiedMetadataName(container)}+{type.MetadataName}";

        var ns = type.ContainingNamespace;

        return ns is null || ns.IsGlobalNamespace
            ? type.MetadataName
            : $"{ns.ToDisplayString()}.{type.MetadataName}";
    }

    /// <summary>The type arguments of the containers, outermost first, then the type's own.</summary>
    private static IEnumerable<ITypeSymbol> TypeArgumentChainOf(INamedTypeSymbol type)
    {
        var containerArguments = type.ContainingType is { } container
            ? TypeArgumentChainOf(container)
            : [];

        return containerArguments.Concat(type.TypeArguments);
    }

    private static string ArraySuffix(int rank) => $"[{new string(',', rank - 1)}]";
}
