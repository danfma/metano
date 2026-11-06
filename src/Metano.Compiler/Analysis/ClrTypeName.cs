using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The name .NET's <c>Type.ToString()</c> prints for a runtime type: a
/// namespace-qualified metadata name, <c>+</c> between a nested type and its
/// container, and the type arguments of the whole chain in brackets at the
/// end: <c>System.Int32</c>, <c>MyApp.Outer+Inner</c>,
/// <c>MyApp.Pair`1[System.Int32]</c> (measured on .NET 10). The
/// <c>base.ToString()</c> default (#506) and System.Text.Json's conversion
/// errors print this text.
/// </summary>
public static class ClrTypeName
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

        var containingNamespace = type.ContainingNamespace;

        return containingNamespace is null || containingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : $"{NamespaceMetadataName(containingNamespace)}.{type.MetadataName}";
    }

    /// <summary>
    /// The dotted metadata names of the namespace chain, so an
    /// <c>@</c>-escaped keyword segment (<c>@event</c>) prints plain, as the
    /// runtime type does.
    /// </summary>
    private static string NamespaceMetadataName(INamespaceSymbol containingNamespace) =>
        containingNamespace.ContainingNamespace is { IsGlobalNamespace: false } parent
            ? $"{NamespaceMetadataName(parent)}.{containingNamespace.MetadataName}"
            : containingNamespace.MetadataName;

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
