using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Rejects an emitted module that refers to two different project types printed under
/// one name (#504): <c>Na.R23</c> and <c>Nb.R23</c> both print as the bare <c>R23</c>,
/// so the one import line the file can hold binds a single module and the other
/// reference silently constructs or tests the wrong class. The second reference reports
/// MS0070 at its C# location; rename one of the two with <c>[Name]</c>. A <c>using</c>
/// alias prints under its own name, so a reference through one never clashes.
/// </summary>
internal sealed class SameNameTypeClashValidator(TypeScriptTransformContext context)
{
    /// <summary>
    /// The MS0070 findings of one emitted file: <paramref name="fileTypes"/> are the
    /// types it declares, and <paramref name="references"/> the references their bodies
    /// and signatures hold (<see cref="SameNameTypeReferences.In"/>).
    /// </summary>
    public IEnumerable<MetanoDiagnostic> Validate(
        string fileName,
        IReadOnlyList<INamedTypeSymbol> fileTypes,
        IReadOnlyList<SameNameReference> references
    )
    {
        if (references.Count == 0)
            yield break;

        var bound = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var type in fileTypes)
            bound[context.ResolveTsName(type)] = type;

        foreach (var reference in references.Where(reference => !reference.IsAlias))
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
        SameNameReference second
    ) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.ImportedNameClash,
            $"'{name}' names both '{first.ToDisplayString()}' and "
                + $"'{second.Type.ToDisplayString()}' in '{fileName}'; the bare name is "
                + "ambiguous and binds one module. "
                + RenameHint(first, second.Type),
            second.Location
        );

    /// <summary>
    /// The fix MS0070 names (#776): rename one of the two types with <c>[Name]</c>,
    /// with a suggested name for the one a rename fits best — a static class,
    /// usually a factory beside its generic (<c>Signal</c> → <c>Signals</c>), else
    /// the type with more type parameters (<c>Box&lt;T1, T2&gt;</c> → <c>Box2</c>),
    /// else the second type, prefixed by its namespace's last segment
    /// (<c>Nb.R23</c> → <c>NbR23</c>). The types are never merged or renamed
    /// automatically.
    /// </summary>
    private static string RenameHint(INamedTypeSymbol first, INamedTypeSymbol second)
    {
        var renamed = TypeToRename(first, second);

        return $"Rename one of them with [Name(\"...\")], for example "
            + $"[Name(\"{SuggestedName(renamed)}\")] on '{renamed.ToDisplayString()}'.";
    }

    private static INamedTypeSymbol TypeToRename(INamedTypeSymbol first, INamedTypeSymbol second)
    {
        if (first.IsStatic != second.IsStatic)
            return first.IsStatic ? first : second;

        return first.Arity > second.Arity ? first : second;
    }

    private static string SuggestedName(INamedTypeSymbol type)
    {
        if (type.IsStatic)
            return type.Name + "s";

        if (type.Arity > 0)
            return type.Name + type.Arity;

        return type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.Name + type.Name
            : type.Name + "2";
    }
}
