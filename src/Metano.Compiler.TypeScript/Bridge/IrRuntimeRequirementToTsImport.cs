using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Maps target-agnostic <see cref="IrRuntimeRequirement"/> facts into concrete
/// TypeScript <see cref="TsImport"/> lines. The translation is TS-specific (choice
/// of package, import form) and lives in the TS target — the IR itself only says
/// "this module needs the HashCode helper"; each backend decides what that means
/// in its module system.
/// </summary>
public static class IrRuntimeRequirementToTsImport
{
    public static IReadOnlyList<TsImport> Convert(IReadOnlySet<IrRuntimeRequirement> requirements)
    {
        if (requirements.Count == 0)
            return [];

        // Group helpers that share the same target module so each import line keeps
        // the legacy single-line-per-module convention (e.g., one
        // `import { HashCode, Enumerable } from "metano-runtime"`); a type-only
        // name (a collection interface) keeps its own `type` qualifier.
        var byModuleName = new Dictionary<string, SortedDictionary<string, bool>>(
            StringComparer.Ordinal
        );

        foreach (var req in requirements)
        {
            if (Map(req) is not { } mapping)
                continue;

            if (!byModuleName.TryGetValue(mapping.ModuleName, out var bucket))
            {
                bucket = new SortedDictionary<string, bool>(StringComparer.Ordinal);
                byModuleName[mapping.ModuleName] = bucket;
            }

            bucket[mapping.Name] = bucket.GetValueOrDefault(mapping.Name, true) && mapping.TypeOnly;
        }

        return byModuleName
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => ImportOf(kvp.Key, kvp.Value))
            .ToList();
    }

    private static TsImport ImportOf(string module, SortedDictionary<string, bool> typeOnlyByName)
    {
        var typeOnly = typeOnlyByName
            .Where(entry => entry.Value)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var allTypeOnly = typeOnly.Count == typeOnlyByName.Count;

        return new TsImport(
            typeOnlyByName.Keys.ToArray(),
            new TsPackageModule(module),
            TypeOnly: allTypeOnly,
            TypeOnlyNames: !allTypeOnly && typeOnly.Count > 0 ? typeOnly : null
        );
    }

    /// <summary>
    /// Whether the requirement maps to a concrete import site at all;
    /// <see cref="ImportedNameOf"/> throws for the ones that don't.
    /// </summary>
    internal static bool HasMappedImport(IrRuntimeRequirement requirement) =>
        Map(requirement) is not null;

    /// <summary>
    /// The name a runtime helper is imported under in the TS target (e.g.
    /// <c>"RecordCopy"</c> → <c>copyRecordWith</c>), for the import walker's
    /// bypass lists to stay in sync with this table.
    /// </summary>
    internal static string ImportedNameOf(IrRuntimeRequirement requirement) =>
        Map(requirement)?.Name
        ?? throw new ArgumentOutOfRangeException(
            nameof(requirement),
            requirement.HelperName,
            "Unmapped helper."
        );

    /// <summary>
    /// <see cref="ImportedNameOf(IrRuntimeRequirement)"/> of a helper the
    /// target names directly (no collection kind).
    /// </summary>
    internal static string ImportedNameOf(string helperName) =>
        Map(new IrRuntimeRequirement(helperName, default))?.Name
        ?? throw new ArgumentOutOfRangeException(
            nameof(helperName),
            helperName,
            "Unmapped helper."
        );

    /// <summary>
    /// Maps a semantic runtime helper to its concrete import site in the TypeScript
    /// target. Returning <c>null</c> means "no import needed" (an unmapped
    /// helper; <c>Temporal</c> maps to the polyfill package).
    /// </summary>
    private static (string ModuleName, string Name, bool TypeOnly)? Map(IrRuntimeRequirement req) =>
        req.HelperName switch
        {
            "HashCode" => ("metano-runtime", "HashCode", false),
            "ValueEquals" => ("metano-runtime", "valueEquals", false),
            "DefaultEquals" => ("metano-runtime", "defaultEquals", false),
            "TupleEquals" => ("metano-runtime", "tupleEquals", false),
            "RecordCopy" => ("metano-runtime", "copyRecordWith", false),
            "IntegerDivision" => ("metano-runtime", "intDiv", false),
            "HashSet" => (
                RuntimeCollections.Module,
                RuntimeCollections.NameOf(IrCollectionKind.HashSet),
                false
            ),
            "UUID" => ("metano-runtime", "UUID", false),
            "Grouping" => ("metano-runtime", "Grouping", true),
            "Temporal" => ("@js-temporal/polyfill", "Temporal", false),
            // Event accessor helpers ship from metano-runtime under their own name.
            "delegateAdd" or "delegateRemove" => ("metano-runtime", req.HelperName, false),
            _ => CollectionImportOf(req),
        };

    /// <summary>
    /// A collection kind the scanner met in a type (spec 011 FR-001): its class
    /// or interface from the collections barrel, type-only for an interface.
    /// </summary>
    private static (string ModuleName, string Name, bool TypeOnly)? CollectionImportOf(
        IrRuntimeRequirement req
    ) =>
        req.Category == IrRuntimeCategory.Collection
        && Enum.TryParse<IrCollectionKind>(req.HelperName, out var kind)
            ? (
                RuntimeCollections.Module,
                RuntimeCollections.NameOf(kind),
                RuntimeCollections.IsInterface(kind)
            )
            : null;
}
