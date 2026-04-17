using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

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
        // `import { HashCode, Enumerable } from "metano-runtime"`).
        var byModuleName = new Dictionary<string, (List<string> Names, bool TypeOnly)>(
            StringComparer.Ordinal
        );

        foreach (var req in requirements)
        {
            var mapping = Map(req);
            if (mapping is null)
                continue;

            if (!byModuleName.TryGetValue(mapping.Value.ModuleName, out var bucket))
            {
                bucket = (new List<string>(), mapping.Value.TypeOnly);
                byModuleName[mapping.Value.ModuleName] = bucket;
            }
            if (!bucket.Names.Contains(mapping.Value.Name))
                bucket.Names.Add(mapping.Value.Name);
        }

        return byModuleName
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new TsImport(
                kvp.Value.Names.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                kvp.Key,
                TypeOnly: kvp.Value.TypeOnly
            ))
            .ToList();
    }

    /// <summary>
    /// Maps a semantic runtime helper to its concrete import site in the TypeScript
    /// target. Returning <c>null</c> means "no import needed" (e.g., `Temporal` is
    /// already imported elsewhere in the current implementation).
    /// </summary>
    private static (string ModuleName, string Name, bool TypeOnly)? Map(IrRuntimeRequirement req) =>
        req.HelperName switch
        {
            "HashCode" => ("metano-runtime", "HashCode", false),
            "Enumerable" => ("metano-runtime", "Enumerable", false),
            "HashSet" => ("metano-runtime", "HashSet", false),
            "UUID" => ("metano-runtime", "UUID", false),
            "Grouping" => ("metano-runtime", "Grouping", true),
            "Temporal" => ("@js-temporal/polyfill", "Temporal", false),
            // Event accessor helpers and overload-dispatcher type checks all
            // ship from metano-runtime under their own name.
            "delegateAdd" or "delegateRemove" => ("metano-runtime", req.HelperName, false),
            "isChar"
            or "isString"
            or "isByte"
            or "isInt16"
            or "isInt32"
            or "isInt64"
            or "isFloat32"
            or "isFloat64"
            or "isBool"
            or "isBigInt" => ("metano-runtime", req.HelperName, false),
            _ => null,
        };
}
