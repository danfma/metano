using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>Recognizes <c>System.IEquatable&lt;T&gt;</c>.</summary>
internal static class EquatableInterfaces
{
    /// <summary>Whether <paramref name="candidate"/> is a construction of <c>System.IEquatable`1</c>.</summary>
    public static bool IsEquatable(INamedTypeSymbol candidate) =>
        candidate.OriginalDefinition
            is {
                MetadataName: "IEquatable`1",
                ContainingNamespace:
                { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
            };

    /// <summary>Whether <paramref name="candidate"/> is <c>System.IEquatable&lt;S&gt;</c> of <paramref name="type"/>.</summary>
    public static bool IsEquatableOf(INamedTypeSymbol candidate, INamedTypeSymbol type) =>
        IsEquatable(candidate)
        && candidate.TypeArguments is [var argument]
        && SymbolEqualityComparer.Default.Equals(
            argument.OriginalDefinition,
            type.OriginalDefinition
        );
}
