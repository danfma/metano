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
    /// indexer (<c>Count$get</c>, <c>Item$get</c>).
    /// </summary>
    public static string GetterName(IPropertySymbol property) =>
        AccessorBaseName(property) + PropertyGetterSuffix;

    /// <summary>
    /// The name of the write-accessor helper of an extension property or
    /// indexer (<c>Count$set</c>, <c>Item$set</c>).
    /// </summary>
    public static string SetterName(IPropertySymbol property) =>
        AccessorBaseName(property) + PropertySetterSuffix;

    /// <summary>
    /// The source name an accessor helper is built from: a property's name,
    /// or an indexer's metadata name (<see cref="DefaultIndexerName"/> unless
    /// <c>[IndexerName]</c> overrides it).
    /// </summary>
    public static string AccessorBaseName(IPropertySymbol property) =>
        property.IsIndexer ? property.MetadataName : property.Name;
}
