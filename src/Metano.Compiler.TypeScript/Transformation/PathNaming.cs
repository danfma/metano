using Metano.Compiler;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// TypeScript-specific path / namespace helpers used by both the type emitter and the
/// import collector, so that subpath imports (<c>#/foo/bar</c>) and on-disk file paths
/// are computed consistently. Under the full-namespace layout policy (D1, spec 005 /
/// ADR-0025) <see cref="RootNamespace"/> is empty — nothing is stripped and a type's
/// path is its complete namespace — so layout never shifts when sibling types change.
/// The stripping logic is retained for a target that may opt into a non-empty root.
/// </summary>
public sealed class PathNaming(string rootNamespace)
{
    public string RootNamespace { get; } = rootNamespace;

    /// <summary>
    /// Normalizes a raw <c>--import-alias</c> value into a subpath-imports key prefix.
    /// Blank (null/empty/whitespace) → <c>#</c> (the default). A single leading <c>#</c>
    /// is optional, so both <c>contracts</c> and <c>#contracts</c> yield <c>#contracts</c>.
    /// This is the single source of truth for the alias key shape, shared by
    /// <see cref="ModuleSpecifierRenderer"/>, <see cref="CyclicReferenceDetector"/> and
    /// <c>PackageJsonWriter</c>.
    /// </summary>
    public static string NormalizeImportAliasPrefix(string? importAlias)
    {
        if (string.IsNullOrWhiteSpace(importAlias))
            return "#";

        var name = importAlias.Trim();
        if (name.StartsWith('#'))
            name = name[1..].Trim();

        return name.Length == 0 ? "#" : "#" + name;
    }

    public static string GetNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        return ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
    }

    /// <summary>
    /// Strips the root namespace and converts remaining segments to a kebab-case file path.
    /// e.g., root="Orzano.Shared", ns="Orzano.Shared.Models", name="Money" → "models/money.ts".
    /// When <paramref name="isJsx"/> is true the file gets a <c>.tsx</c> extension instead
    /// of <c>.ts</c> (the default keeps every current caller emitting <c>.ts</c>).
    /// </summary>
    public string GetRelativePath(string ns, string typeName, bool isJsx = false)
    {
        var folder = ToKebabPath(StripRootNamespace(ns));
        var extension = isJsx ? ".tsx" : ".ts";
        var fileName = SymbolHelper.ToKebabCase(typeName);

        return folder.Length > 0 ? folder + "/" + fileName + extension : fileName + extension;
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
    /// The module of the generated file that declares <paramref name="fileName"/> in
    /// <paramref name="ns"/>: always the defining FILE, never a namespace barrel (D3,
    /// ADR-0025). Routing internal references directly at files keeps internal
    /// correctness independent of barrel generation/pruning and structurally prevents ESM
    /// cycles such as <c>issue.ts → issues/domain/index.ts → ./issue.ts</c>. The text of
    /// the specifier is decided by <see cref="ModuleSpecifierRenderer"/>.
    /// </summary>
    public TsLocalModule LocalModule(string ns, string fileName) => new(FileKey(ns, fileName));

    /// <summary>
    /// The file key of <paramref name="fileName"/> in <paramref name="ns"/>: its
    /// package-relative path without extension (<c>shared-kernel/user-id</c>), the path
    /// <see cref="GetRelativePath"/> gives minus the extension.
    /// </summary>
    public string FileKey(string ns, string fileName)
    {
        var folder = ToKebabPath(StripRootNamespace(ns));
        var kebabName = SymbolHelper.ToKebabCase(fileName);

        return folder.Length > 0 ? folder + "/" + kebabName : kebabName;
    }

    /// <summary>
    /// Computes the cross-package subpath a consumer imports for a type in
    /// <paramref name="typeNamespace"/> of a package whose <c>[EmitPackage]</c>
    /// declares <paramref name="packageSubpathRoot"/>: the full kebab namespace path
    /// (D1) with the root stripped on whole segments (spec 008). Matches the key the
    /// producing package writes to <c>package.json#exports</c>; <c>""</c> means the
    /// bare package.
    /// </summary>
    public static string ComputeExportedSubPath(
        PackageSubpathRoot packageSubpathRoot,
        string typeNamespace
    ) => PackageExportSubpaths.Strip(packageSubpathRoot, ToKebabPath(typeNamespace));

    /// <summary>
    /// Maps a dotted namespace to its folder path, one kebab-cased segment per
    /// namespace segment: <c>VideoStudio.Domain</c> → <c>video-studio/domain</c>,
    /// <c>""</c> → <c>""</c>. The single namespace-to-path rule shared by the on-disk
    /// layout, internal imports and the exported subpaths.
    /// </summary>
    public static string ToKebabPath(string dottedNamespace) =>
        dottedNamespace.Length > 0
            ? string.Join("/", dottedNamespace.Split('.').Select(SymbolHelper.ToKebabCase))
            : "";
}
