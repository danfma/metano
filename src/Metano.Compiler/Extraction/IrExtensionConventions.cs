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
    /// declares no <c>[IndexerName]</c> override. Roslyn already reports
    /// <c>"Item"</c> as the default <see cref="Microsoft.CodeAnalysis.IPropertySymbol.Name"/>;
    /// the helper-name resolver lower-cases that to <c>item</c> so reads emit
    /// <c>item$get(self, index)</c> and writes emit <c>item$set(self, index, value)</c>.
    /// A custom name (<c>[IndexerName("Slot")]</c>) flows through the same
    /// pipeline because Roslyn rewrites <c>prop.Name</c> to the overridden
    /// value — the convention here only documents the fallback.
    /// </summary>
    public const string DefaultIndexerName = "Item";
}
