using Metano.Compiler.Analysis;
using Metano.Compiler.Caching;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Caching;

/// <summary>
/// The per-group cache input for direct constructor call sites (spec 013
/// FR-017, ADR-0023). A body's <c>new Money(…)</c> now spells the factory
/// Money's claim pass named, and a group's closure hash tracks signatures,
/// not body references, so a caller would keep a stale name when Money
/// renames a constructor parameter or gains a colliding member. Every group
/// key therefore folds in the shape of every type with constructor selectors,
/// and of the bases its claims read: conservative, since any such change
/// re-transforms every group.
/// </summary>
internal static class DispatchedConstructionFingerprint
{
    /// <summary>
    /// The hash over the constructor signatures and type signatures of each
    /// type in <paramref name="types"/> with constructor selectors and of
    /// its base chain; empty when none has selectors.
    /// </summary>
    public static string Of(IEnumerable<INamedTypeSymbol> types)
    {
        var entries = types
            .Where(type =>
                DispatchedConstructorCalls.HasSelectors(
                    type,
                    TsConstructorSelectorShapes.EmitsSelectors
                )
            )
            .Select(EntryOf)
            .Order(StringComparer.Ordinal)
            .ToList();

        return entries.Count == 0 ? "" : FileMetadataExtractor.Sha256(string.Join("\n", entries));
    }

    private static string EntryOf(INamedTypeSymbol type)
    {
        var constructors = IrConstructorExtractor
            .AllExplicitConstructors(type)
            .Select(OverloadSignatureText.Of);

        var shapes = BaseChainOf(type).Select(IrTypeSignatureHasher.Hash);

        return $"{IrTypeDependencyGraph.QualifiedName(type)}:{string.Join(",", constructors)}:"
            + string.Join(",", shapes);
    }

    private static IEnumerable<INamedTypeSymbol> BaseChainOf(INamedTypeSymbol type)
    {
        for (
            var current = type.OriginalDefinition;
            current is not null;
            current = current.BaseType?.OriginalDefinition
        )
            yield return current;
    }
}
