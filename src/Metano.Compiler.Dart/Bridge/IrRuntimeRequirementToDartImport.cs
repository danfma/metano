using Metano.Compiler.IR;
using Metano.Compiler.Dart.AST;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// Maps target-agnostic <see cref="IrRuntimeRequirement"/> facts into concrete
/// Dart <see cref="DartImport"/> lines. The translation is Dart-specific (choice
/// of package, <c>show</c> clauses) and lives in the Dart target — the IR itself
/// only says "this module needs the HashCode helper"; each backend decides what
/// that means in its module system.
/// </summary>
public static class IrRuntimeRequirementToDartImport
{
    private const string MetanoRuntimePath = "package:metano_runtime/metano_runtime.dart";

    public static IReadOnlyList<DartImport> Convert(IReadOnlySet<IrRuntimeRequirement> requirements)
    {
        if (requirements.Count == 0)
            return [];

        // Group helpers that share the same target module so each import line
        // collapses to a single `import '...' show A, B;`. Dart's `show` clause
        // keeps the import surface explicit and lets the analyzer flag stale
        // requirements during refactors.
        var byPath = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var req in requirements)
        {
            foreach (var (importPath, symbol) in Map(req))
            {
                if (!byPath.TryGetValue(importPath, out var names))
                {
                    names = new SortedSet<string>(StringComparer.Ordinal);
                    byPath[importPath] = names;
                }
                names.Add(symbol);
            }
        }

        return byPath
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new DartImport(kvp.Key, ShowNames: kvp.Value.ToArray()))
            .ToList();
    }

    /// <summary>
    /// Maps a semantic runtime helper to one or more Dart import symbols.
    /// Returning an empty array means "no import needed" — either the helper
    /// is satisfied by a Dart built-in (<c>DateTime</c>, <c>Set&lt;T&gt;</c>,
    /// the <c>is</c> operator) or the helper isn't yet ported to
    /// <c>metano_runtime</c>.
    /// </summary>
    private static IReadOnlyList<(string ImportPath, string Symbol)> Map(
        IrRuntimeRequirement req
    ) =>
        req.HelperName switch
        {
            "HashCode" => [(MetanoRuntimePath, "HashCode")],
            _ => Array.Empty<(string, string)>(),
        };
}
