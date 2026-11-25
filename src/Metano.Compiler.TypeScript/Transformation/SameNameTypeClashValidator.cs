using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Rejects an emitted module that refers to two different project types printed under
/// one name (#504): <c>Na.R23</c> and <c>Nb.R23</c> both print as the bare <c>R23</c>,
/// so the one import line the file can hold binds a single module and the other
/// reference silently constructs or tests the wrong class. The second reference reports
/// MS0070 at its C# location; rename one of the two with <c>[Name]</c>.
/// </summary>
internal sealed class SameNameTypeClashValidator(
    TypeScriptTransformContext context,
    SameNameTypeReferences references
)
{
    /// <summary>
    /// The MS0070 findings of one emitted file: <paramref name="fileTypes"/> are the
    /// types it declares, whose bodies and signatures are the references it holds.
    /// </summary>
    public IEnumerable<MetanoDiagnostic> Validate(
        string fileName,
        IReadOnlyList<INamedTypeSymbol> fileTypes
    )
    {
        if (references.IsEmpty)
            yield break;

        var bound = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var type in fileTypes)
            bound[context.ResolveTsName(type)] = type;

        foreach (var reference in references.In(fileTypes))
        {
            var name = context.ResolveTsName(reference.Type);

            if (!bound.TryAdd(name, reference.Type) && !Same(bound[name], reference.Type))
                yield return Clash(name, fileName, bound[name], reference);
        }
    }

    private static bool Same(INamedTypeSymbol first, INamedTypeSymbol second) =>
        SymbolEqualityComparer.Default.Equals(first.OriginalDefinition, second.OriginalDefinition);

    private static MetanoDiagnostic Clash(
        string name,
        string fileName,
        INamedTypeSymbol first,
        (INamedTypeSymbol Type, Location Location) second
    ) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.ImportedNameClash,
            $"'{name}' names both '{first.ToDisplayString()}' and "
                + $"'{second.Type.ToDisplayString()}' in '{fileName}'; the bare name is "
                + "ambiguous and binds one module. Rename one of the two with [Name(\"...\")].",
            second.Location
        );
}
