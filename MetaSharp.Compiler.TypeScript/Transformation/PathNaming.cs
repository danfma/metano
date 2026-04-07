using MetaSharp.Compiler;
using Microsoft.CodeAnalysis;

namespace MetaSharp.Transformation;

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
        var segments = relative.Length > 0
            ? relative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
            : [];

        var fileName = SymbolHelper.ToKebabCase(typeName);
        return segments.Length > 0
            ? string.Join("/", segments) + "/" + fileName + ".ts"
            : fileName + ".ts";
    }

    public string StripRootNamespace(string ns)
    {
        if (RootNamespace.Length == 0 || ns.Length == 0) return ns;
        if (ns == RootNamespace) return "";
        if (ns.StartsWith(RootNamespace + "."))
            return ns[(RootNamespace.Length + 1)..];
        return ns;
    }

    /// <summary>
    /// Computes the absolute import path for a type using the <c>#/</c> subpath import alias.
    /// All cross-file imports use <c>#/&lt;namespace-path&gt;/&lt;type-name&gt;</c>; consumers
    /// configure <c>package.json#imports</c> and <c>tsconfig#paths</c> to resolve <c>#/*</c>
    /// to <c>./src/*</c>. The <paramref name="fromNs"/> is unused — paths are always rooted.
    /// </summary>
    public string ComputeRelativeImportPath(string fromNs, string toNs, string typeName)
    {
        var toRelative = StripRootNamespace(toNs);
        var toParts = toRelative.Length > 0
            ? toRelative.Split('.').Select(SymbolHelper.ToKebabCase).ToArray()
            : [];

        var parts = new List<string> { "#" };
        parts.AddRange(toParts);
        parts.Add(SymbolHelper.ToKebabCase(typeName));
        return string.Join("/", parts);
    }

    /// <summary>
    /// Finds the longest common dot-separated namespace prefix across the given list.
    /// Used to discover the project's root namespace at the start of a transpilation run.
    /// </summary>
    public static string FindCommonNamespacePrefix(IReadOnlyList<string> namespaces)
    {
        if (namespaces.Count == 0) return "";
        if (namespaces.Count == 1) return namespaces[0];

        var parts = namespaces[0].Split('.');
        var commonLength = parts.Length;

        for (var i = 1; i < namespaces.Count; i++)
        {
            var otherParts = namespaces[i].Split('.');
            commonLength = Math.Min(commonLength, otherParts.Length);

            for (var j = 0; j < commonLength; j++)
            {
                if (parts[j] != otherParts[j])
                {
                    commonLength = j;
                    break;
                }
            }
        }

        return string.Join(".", parts.Take(commonLength));
    }
}
