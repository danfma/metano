using Metano.Compiler;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The types whose guard is synthesized (original definitions) and their
/// cross-assembly origin keys, which join the guardable type keys so guard
/// references resolve to them: the variants of a structural JSON hierarchy
/// and the <c>[PlainObject]</c> types a runtime type test recognizes
/// (spec 011 FR-020).
/// </summary>
internal sealed record JsonStructuralGuardSet(
    IReadOnlySet<INamedTypeSymbol> Types,
    IReadOnlySet<string> OriginKeys
)
{
    /// <summary>This set with <paramref name="types"/> added.</summary>
    public JsonStructuralGuardSet With(IEnumerable<INamedTypeSymbol> types)
    {
        var added = types.Select(type => type.OriginalDefinition).ToList();

        return new JsonStructuralGuardSet(
            Types.Concat(added).ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            OriginKeys
                .Concat(added.Select(type => type.GetCrossAssemblyOriginKey()))
                .ToHashSet(StringComparer.Ordinal)
        );
    }
}
