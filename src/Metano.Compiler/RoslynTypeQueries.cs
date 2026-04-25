using Microsoft.CodeAnalysis;

namespace Metano.Compiler;

/// <summary>
/// Target-agnostic Roslyn-side predicates and formatters used across the
/// pipeline. Keeps the "is this a Dictionary-shaped BCL type?" / version
/// formatting logic in one place so the IR extractor and the per-target
/// transformers don't drift on the answer.
/// </summary>
public static class RoslynTypeQueries
{
    /// <summary>
    /// Formats an assembly's version as an npm-compatible specifier.
    /// Assemblies that don't declare a version (Roslyn defaults to
    /// <c>0.0.0.0</c>) get <c>workspace:*</c>, which is the right call for
    /// sibling projects in a Bun monorepo. Anything with a real version
    /// becomes <c>^Major.Minor.Patch</c>.
    /// </summary>
    public static string FormatAssemblyVersion(IAssemblySymbol assembly)
    {
        var v = assembly.Identity.Version;
        if (v.Major == 0 && v.Minor == 0 && v.Build <= 0)
            return "workspace:*";
        var build = v.Build > 0 ? v.Build : 0;
        return $"^{v.Major}.{v.Minor}.{build}";
    }

    public static bool IsDictionaryLike(this INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.Dictionary")
            || fullName.StartsWith("System.Collections.Generic.IDictionary")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyDictionary")
            || fullName.StartsWith("System.Collections.Concurrent.ConcurrentDictionary");
    }

    public static bool IsSetLike(this INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.HashSet")
            || fullName.StartsWith("System.Collections.Generic.ISet")
            || fullName.StartsWith("System.Collections.Generic.SortedSet")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableHashSet");
    }

    /// <summary>
    /// True for BCL types that map to a TypeScript <c>Array</c> at the
    /// IR level. <c>IEnumerable&lt;T&gt;</c> is intentionally excluded
    /// — it represents a lazy sequence and lowers to
    /// <c>IrIterableTypeRef</c> (TS <c>Iterable&lt;T&gt;</c>) instead;
    /// see the dedicated branch in <see cref="Metano.Compiler.Extraction.IrTypeRefMapper"/>.
    /// </summary>
    public static bool IsCollectionLike(this INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName.StartsWith("System.Collections.Generic.List")
            || fullName.StartsWith("System.Collections.Generic.IList")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyList")
            || fullName.StartsWith("System.Collections.Generic.ICollection")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableList")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableArray")
            || fullName.StartsWith("System.Collections.Generic.Queue")
            || fullName.StartsWith("System.Collections.Generic.Stack");
    }
}
