using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Naming conventions shared by the extension-member lowering pipeline.
/// Centralized so call-site rewriting (<see cref="IrExpressionExtractor"/>),
/// helper emission (<see cref="IrModuleFunctionExtractor"/>), and the
/// per-target import registry stay in lockstep when the conventions evolve.
/// </summary>
public static class IrExtensionConventions
{
    /// <summary>
    /// Suffix appended to a property's emitted name to disambiguate the
    /// read-accessor helper from a sibling method that shares the source
    /// name (<c>x.IsEven</c> → <c>isEven$get(x)</c>; <c>x.IsEven()</c>
    /// stays <c>isEven(x)</c>).
    /// </summary>
    public const string PropertyGetterSuffix = "$get";

    /// <summary>
    /// Suffix appended to the write-accessor helper. Reserved for Stage 2
    /// (extension property setters) — kept here so both stages share a
    /// single source of truth.
    /// </summary>
    public const string PropertySetterSuffix = "$set";

    /// <summary>
    /// Default base name for an extension indexer's helpers when the source
    /// declares no <c>[IndexerName]</c> override. Roslyn reports it as the
    /// indexer's <see cref="ISymbol.MetadataName"/> (its
    /// <see cref="ISymbol.Name"/> is <c>this[]</c>); the target's naming
    /// policy lower-cases it, so reads emit <c>item$get(self, index)</c> and
    /// writes emit <c>item$set(self, index, value)</c>. A custom name
    /// (<c>[IndexerName("Slot")]</c>) replaces the metadata name, giving
    /// <c>slot$get</c> / <c>slot$set</c>.
    /// </summary>
    public const string DefaultIndexerName = "Item";

    /// <summary>
    /// The name of the read-accessor helper of an extension property or
    /// indexer (<c>Count$get</c>, <c>Item$get</c>, <c>ItemByKey$get</c>).
    /// </summary>
    public static string GetterName(IPropertySymbol property, TargetLanguage? target) =>
        AccessorBaseName(property, target) + PropertyGetterSuffix;

    /// <summary>
    /// The name of the write-accessor helper of an extension property or
    /// indexer (<c>Count$set</c>, <c>Item$set</c>, <c>ItemByKey$set</c>).
    /// </summary>
    public static string SetterName(IPropertySymbol property, TargetLanguage? target) =>
        AccessorBaseName(property, target) + PropertySetterSuffix;

    /// <summary>
    /// The source name an accessor helper is built from: a property's name,
    /// or an indexer's metadata name (<see cref="DefaultIndexerName"/> unless
    /// <c>[IndexerName]</c> overrides it) followed by the suffix its
    /// <c>[Name]</c> pins when it shares that name with another indexer of its
    /// extension class (<see cref="PinnedIndexerSuffix"/>).
    /// </summary>
    public static string AccessorBaseName(IPropertySymbol property, TargetLanguage? target) =>
        property.IsIndexer
            ? property.MetadataName + PinnedIndexerSuffix(property, target)
            : property.Name;

    /// <summary>
    /// The suffix a <c>[Name("byKey")]</c> pins on an extension indexer that
    /// shares its metadata name with another indexer of its extension class
    /// (<c>this[int]</c> beside <c>this[string]</c>): the value capitalized,
    /// as spec 013 FR-005 pins an overload's suffix (<c>itemByKey$get</c>), so
    /// the two indexers' helpers keep distinct names. <c>null</c> for any
    /// other property or indexer, whose <c>[Name]</c> renames the whole helper.
    /// </summary>
    public static string? PinnedIndexerSuffix(IPropertySymbol property, TargetLanguage? target) =>
        SiblingIndexersOf(property).Any() && property.GetNameOverride(target) is { Length: > 0 } pin
            ? char.ToUpperInvariant(pin[0]) + pin[1..]
            : null;

    /// <summary>
    /// The <c>[Name]</c> that renames an extension property's or indexer's whole
    /// helper; <c>null</c> when there is none or it only pins the suffix of an
    /// indexer sharing its name with another (<see cref="PinnedIndexerSuffix"/>).
    /// </summary>
    public static string? WholeHelperNameOverride(
        IPropertySymbol property,
        TargetLanguage? target
    ) => PinnedIndexerSuffix(property, target) is null ? property.GetNameOverride(target) : null;

    /// <summary>
    /// The other public indexers of <paramref name="indexer"/>'s extension
    /// class with the same metadata name, across all of its extension blocks:
    /// their helpers would share one module function name.
    /// </summary>
    public static IEnumerable<IPropertySymbol> SiblingIndexersOf(IPropertySymbol indexer)
    {
        if (
            !indexer.IsIndexer
            || indexer.ContainingType?.ContainingType is not { IsStatic: true } owner
        )
            return [];

        var declared = indexer.OriginalDefinition;

        return owner
            .GetTypeMembers()
            .SelectMany(block => block.GetMembers())
            .OfType<IPropertySymbol>()
            .Where(other =>
                other is { IsIndexer: true, DeclaredAccessibility: Accessibility.Public }
                && other.MetadataName == indexer.MetadataName
                && !SymbolEqualityComparer.Default.Equals(other.OriginalDefinition, declared)
            );
    }
}
