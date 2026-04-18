using Metano.Compiler;
using Microsoft.CodeAnalysis;

namespace Metano.Transformation;

/// <summary>
/// TypeScript-specific path / namespace helpers used by both the type emitter and the
/// import collector. Holds the discovered <see cref="RootNamespace"/> (longest common
/// dot-separated prefix across all transpilable types) so that subpath imports
/// (<c>#/foo/bar</c>) and on-disk file paths can both be computed consistently.
/// </summary>
public sealed class PathNaming(string rootNamespace)
{
    public string RootNamespace { get; } = rootNamespace;

    public static string GetNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        return ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
    }

    /// <summary>
    /// Strips the root namespace and converts remaining segments to a kebab-case file path.
    /// e.g., root="Orzano.Shared", ns="Orzano.Shared.Models", name="Money" → "models/money.ts"
    /// </summary>
    public string GetRelativePath(string ns, string typeName)
    {
        var relative = StripRootNamespace(ns);
        var segments =
            relative.Length > 0
                ? relative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
                : [];

        var fileName = SymbolHelper.ToKebabCase(typeName);
        return segments.Length > 0
            ? string.Join("/", segments) + "/" + fileName + ".ts"
            : fileName + ".ts";
    }

    public string StripRootNamespace(string ns)
    {
        if (RootNamespace.Length == 0 || ns.Length == 0)
            return ns;
        if (ns == RootNamespace)
            return "";
        if (ns.StartsWith(RootNamespace + "."))
            return ns[(RootNamespace.Length + 1)..];
        return ns;
    }

    /// <summary>
    /// Computes the absolute import path for a generated type using the local package alias.
    ///
    /// Preferred strategy is namespace-first:
    ///
    /// - different namespace → import the namespace barrel (<c>#/issues/domain</c>)
    /// - root namespace      → import the package root barrel (<c>#</c>)
    ///
    /// Same-namespace imports fall back to the concrete file path to avoid trivial cycles like:
    ///
    /// <c>issue.ts → #/issues/domain → issues/domain/index.ts → ./issue.ts</c>
    ///
    /// The <paramref name="typeName"/> parameter therefore remains necessary for the fallback.
    /// </summary>
    public string ComputeRelativeImportPath(string fromNs, string toNs, string typeName)
    {
        var toRelative = StripRootNamespace(toNs);
        var toParts =
            toRelative.Length > 0
                ? toRelative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
                : [];

        // Same namespace: use relative file import to avoid importing through the same
        // namespace barrel that re-exports the current file. This prevents trivial
        // cycles like: issue.ts → barrel → ./issue.ts
        if (fromNs == toNs)
        {
            return "./" + SymbolHelper.ToKebabCase(typeName);
        }

        // Different namespace: import the namespace barrel.
        if (toParts.Length == 0)
            return "#";
        return "#/" + string.Join("/", toParts);
    }

    /// <summary>
    /// Computes the package-relative namespace barrel subpath for a type whose source
    /// assembly has the given <paramref name="assemblyRootNamespace"/>.
    ///
    /// Examples:
    ///
    /// - root ns match  → <c>""</c>                → import from <c>"pkg"</c>
    /// - child namespace → <c>"domain"</c>         → import from <c>"pkg/domain"</c>
    /// - nested namespace → <c>"issues/domain"</c> → import from <c>"pkg/issues/domain"</c>
    ///
    /// No <c>#/</c> prefix and no <c>.ts</c> suffix.
    /// </summary>
    public static string ComputeSubPath(
        string assemblyRootNamespace,
        string typeNamespace,
        string typeName
    )
    {
        var relative = typeNamespace;
        if (assemblyRootNamespace.Length > 0)
        {
            if (typeNamespace == assemblyRootNamespace)
                relative = "";
            else if (typeNamespace.StartsWith(assemblyRootNamespace + "."))
                relative = typeNamespace[(assemblyRootNamespace.Length + 1)..];
        }

        var segments =
            relative.Length > 0
                ? relative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
                : [];

        return segments.Length > 0 ? string.Join("/", segments) : "";
    }

    /// <summary>
    /// Finds the longest common dot-separated namespace prefix across the given list.
    /// Used to discover the project's root namespace at the start of a transpilation run.
    /// </summary>
    public static string FindCommonNamespacePrefix(IReadOnlyList<string> namespaces) =>
        NamespaceUtilities.FindCommonPrefix(namespaces);
}
