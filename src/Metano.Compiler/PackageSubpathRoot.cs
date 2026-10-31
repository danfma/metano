using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Compiler;

/// <summary>
/// The validated <c>[EmitPackage(SubpathRoot = …)]</c> of a package: a dotted C#
/// namespace (e.g. <c>VideoStudio.Domain</c>) that the package name stands for, so
/// targets can strip it from the package's public subpaths (spec 008). Target-agnostic:
/// each target decides how a namespace maps to a subpath.
/// <para>
/// <see cref="None"/> (no root declared) is the only empty state and equals
/// <c>default(PackageSubpathRoot)</c>, so an unset IR field means "no root". Every other
/// value is created through <see cref="TryFrom"/>, which validates once.
/// </para>
/// </summary>
public readonly record struct PackageSubpathRoot
{
    private readonly string? _namespace;

    private PackageSubpathRoot(string dottedNamespace) => _namespace = dottedNamespace;

    /// <summary>No root declared; equals <c>default</c>.</summary>
    public static PackageSubpathRoot None => default;

    /// <summary>The dotted namespace, or <c>""</c> for <see cref="None"/>.</summary>
    public string Namespace => _namespace ?? "";

    public bool IsNone => _namespace is null;

    /// <summary>
    /// Parses <paramref name="dottedNamespace"/>: one or more <c>.</c>-separated
    /// segments, each a valid C# identifier (no whitespace, no empty segment, no
    /// <c>global::</c>). <c>null</c> or empty yields <see cref="None"/> and succeeds;
    /// an invalid value yields <see cref="None"/> and fails.
    /// </summary>
    public static bool TryFrom(string? dottedNamespace, out PackageSubpathRoot root)
    {
        root = None;

        if (string.IsNullOrEmpty(dottedNamespace))
            return true;

        if (!dottedNamespace.Split('.').All(SyntaxFacts.IsValidIdentifier))
            return false;

        root = new PackageSubpathRoot(dottedNamespace);

        return true;
    }

    public override string ToString() => Namespace;
}
